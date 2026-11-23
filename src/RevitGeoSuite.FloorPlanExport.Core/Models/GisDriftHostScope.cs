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
    /// Scopes <paramref name="file"/> to the model keyed <paramref name="hostModelKey"/> (its model
    /// id in lower case). A model without an id is taken to be the one the file was written for.
    /// </summary>
    public static GisDriftHostScope Of(GisDriftFile file, string? hostModelKey)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        string host = (hostModelKey ?? string.Empty).Trim();
        if (host.Length == 0)
        {
            host = (file.Model ?? string.Empty).Trim();
        }

        List<string> problems = new();
        List<GisDriftRow> rows = new();
        foreach (GisDriftRow row in file.Rows)
        {
            if (RevitKey.BelongsToModel(row.RevitKey, host))
            {
                rows.Add(row);
            }
            else
            {
                problems.Add($"{row.Describe()}: {WhoseItIs(row.RevitKey, host)}");
            }
        }

        List<GisDriftShape> shapes = new();
        foreach (GisDriftShape shape in file.Shapes)
        {
            if (RevitKey.BelongsToModel(shape.RevitKey, host))
            {
                shapes.Add(shape);
            }
            else
            {
                problems.Add($"{shape.Describe()}: {WhoseItIs(shape.RevitKey, host)}");
            }
        }

        return problems.Count == 0
            ? new GisDriftHostScope(file, Array.Empty<string>())
            : new GisDriftHostScope(new GisDriftFile(file.Model, file.AppliedVersion, rows, file.Epsg, shapes), problems);
    }

    private static string WhoseItIs(string revitKey, string host)
    {
        string model = RevitKey.ModelPartOf(revitKey);
        string link = RevitKey.LinkInstanceOf(model);
        return link.Length > 0
            ? $"it belongs to a linked model (link instance {link}), not to the open model; change it in the linked model. Nothing is written here."
            : $"it belongs to the model {model}, not to the open model ({host}). Nothing is written here.";
    }
}
