using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;

namespace RevitGeoSuite.FloorPlanExport.Core.Models;

/// <summary>
/// The part of a drift file that is about the open model itself. Kanizu files a model's drift
/// with the rows of the models linked into it, whose keys read <c>model@LinkUniqueId|UniqueId|part</c>.
/// Review GDB edits finds an element by its UniqueId in the open model, and a model forked from
/// its link by Save As shares UniqueIds with it, so a linked row looked up there would change the
/// wrong element. Such rows are listed as problems and never reach the lookup.
/// </summary>
public sealed class GisDriftHostScope
{
    private GisDriftHostScope(GisDriftFile file, IReadOnlyList<string> problems)
    {
        File = file;
        Problems = problems;
    }

    /// <summary>The drift file with only the rows and shapes of the open model.</summary>
    public GisDriftFile File { get; }

    /// <summary>One line for each row and shape left out, saying whose it is.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>
    /// Scopes <paramref name="file"/> to the model whose features are keyed
    /// <paramref name="hostModelKey"/>: the model part the export writes into every
    /// <c>revit_key</c>, so the model id in lower case, or <c>doc:</c> plus the document key for a
    /// model without one. The file's own <c>model</c> is not that key (Kanizu files a model without
    /// an id under its bare document key), so it is never used to tell whose a row is.
    /// </summary>
    /// <remarks>
    /// Nothing passes unchecked. A row or shape is the open model's only when its key reads
    /// <c>model|UniqueId|part</c> and its model part is exactly the host's; without a host key, or
    /// with a key that is blank or not a key, it is left out. Only a unit drawn by hand in the GDB
    /// has no key, since no element made it. A split or merge is one edit: when any of its units is
    /// left out, all of them are, so the host is never redrawn to an outline that takes in a floor
    /// of another model.
    /// </remarks>
    public static GisDriftHostScope Of(GisDriftFile file, string? hostModelKey)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        string host = (hostModelKey ?? string.Empty).Trim();
        List<string> problems = new();
        List<GisDriftRow> rows = new();
        foreach (GisDriftRow row in file.Rows)
        {
            string? whyNot = WhyNotTheHosts(row.RevitKey, host);
            if (whyNot == null)
            {
                rows.Add(row);
            }
            else
            {
                problems.Add($"{row.Describe()}: {whyNot}");
            }
        }

        // Whether each shape alone is the host's, then whether its whole split or merge is.
        List<string?> whyNotShape = file.Shapes
            .Select(shape => host.Length > 0 && IsDrawnByHand(shape) ? null : WhyNotTheHosts(shape.RevitKey, host))
            .ToList();
        HashSet<string> brokenGroups = new(StringComparer.Ordinal);
        for (int index = 0; index < file.Shapes.Count; index++)
        {
            string group = GroupOf(file.Shapes[index]);
            if (whyNotShape[index] != null && group.Length > 0)
            {
                brokenGroups.Add(group);
            }
        }

        List<GisDriftShape> shapes = new();
        for (int index = 0; index < file.Shapes.Count; index++)
        {
            GisDriftShape shape = file.Shapes[index];
            string? whyNot = whyNotShape[index];
            if (whyNot == null && brokenGroups.Contains(GroupOf(shape)))
            {
                whyNot = $"its {shape.Edit} also takes in a unit that is not the open model's, so the whole edit is left out. Nothing is written here.";
            }

            if (whyNot == null)
            {
                shapes.Add(shape);
            }
            else
            {
                problems.Add($"{shape.Describe()}: {whyNot}");
            }
        }

        return problems.Count == 0
            ? new GisDriftHostScope(file, Array.Empty<string>())
            : new GisDriftHostScope(new GisDriftFile(file.Model, file.AppliedVersion, rows, file.Epsg, shapes), problems);
    }

    /// <summary>A unit drawn in the GDB that no element made: the only shape without a key.</summary>
    private static bool IsDrawnByHand(GisDriftShape shape) =>
        shape.Edit == GisDrift.HandDrawn && string.IsNullOrWhiteSpace(shape.RevitKey);

    /// <summary>What ties the shapes of one split or merge together, as the shape planner groups them; empty for a shape on its own.</summary>
    private static string GroupOf(GisDriftShape shape)
    {
        if (shape.Edit != GisDrift.Split && shape.Edit != GisDrift.Merged)
        {
            return string.Empty;
        }

        string group = shape.Group.Length > 0 ? shape.Group : shape.RevitKey;
        return group.Length > 0 ? shape.Edit + "\u001f" + group : string.Empty;
    }

    /// <summary>Null when the key names the host model itself; otherwise why it is left out.</summary>
    private static string? WhyNotTheHosts(string? revitKey, string host)
    {
        const string nothing = "Nothing is written here.";
        if (host.Length == 0)
        {
            return $"the open model has no key to compare it with. {nothing}";
        }

        string model = RevitKey.ModelPartOf(revitKey).Trim();
        if (model.Length == 0)
        {
            return $"it carries no element key (revitKey) that says which model it belongs to. {nothing}";
        }

        if (string.Equals(model, host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string link = RevitKey.LinkInstanceOf(model);
        return link.Length > 0
            ? $"it belongs to a linked model (link instance {link}), not to the open model; change it in the linked model. {nothing}"
            : $"it belongs to the model {model}, not to the open model ({host}). {nothing}";
    }
}
