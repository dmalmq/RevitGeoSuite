using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Models;

public enum LinkCoverageProblemKind
{
    NotLoaded,
    NoModelId,
    ReadFailed,
}

/// <summary>One selected linked model that an export cannot fully and stably cover.</summary>
public sealed class LinkCoverageProblem
{
    public LinkCoverageProblem(string linkName, LinkCoverageProblemKind kind, string? detail = null)
    {
        LinkName = string.IsNullOrWhiteSpace(linkName) ? "(unnamed link)" : linkName.Trim();
        Kind = kind;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail!.Trim();
    }

    public string LinkName { get; }

    public LinkCoverageProblemKind Kind { get; }

    public string? Detail { get; }
}

/// <summary>
/// Stops a station export that would leave out a linked model or key it per PC. Kanizu reads a row
/// missing from a new export as deleted in Revit, so a partial export would plan real deletions.
/// </summary>
public static class LinkCoverage
{
    public static void ThrowIfIncomplete(IEnumerable<LinkCoverageProblem> problems)
    {
        string? message = DescribeProblems(problems);
        if (message != null)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>The message naming every affected link and its fix, or null when there are none.</summary>
    public static string? DescribeProblems(IEnumerable<LinkCoverageProblem> problems)
    {
        List<LinkCoverageProblem> all = (problems ?? throw new ArgumentNullException(nameof(problems))).ToList();
        if (all.Count == 0)
        {
            return null;
        }

        List<string> parts = new() { "The export stopped because Kanizu would read this linked model data as deleted or changed." };
        AddPart(parts, all, LinkCoverageProblemKind.NotLoaded, "Not loaded", "Reload them in Manage Links.");
        AddPart(
            parts,
            all,
            LinkCoverageProblemKind.NoModelId,
            "No model id",
            "Open each linked model, georeference it if needed, run one export, save it, and reload the link.");
        AddPart(
            parts,
            all,
            LinkCoverageProblemKind.ReadFailed,
            "Could not be read",
            "Check that the link is visible in those views, or remove the link from this export.");
        return string.Join(" ", parts);
    }

    private static void AddPart(
        ICollection<string> parts,
        IEnumerable<LinkCoverageProblem> problems,
        LinkCoverageProblemKind kind,
        string label,
        string fix)
    {
        List<string> links = problems
            .Where(problem => problem.Kind == kind)
            .Select(problem => problem.Detail == null ? problem.LinkName : $"{problem.LinkName} ({problem.Detail})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(link => link, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (links.Count > 0)
        {
            parts.Add($"{label}: {string.Join(", ", links)}. {fix}");
        }
    }
}
