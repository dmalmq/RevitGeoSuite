using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>An id several elements hold: the original keeps it, each copy gets a new one.</summary>
public sealed class CopiedId
{
    public CopiedId(string id, long original, IReadOnlyList<long> copies)
    {
        Id = id;
        Original = original;
        Copies = copies;
    }

    public string Id { get; }

    /// <summary>The oldest element: Revit numbers elements in the order it makes them.</summary>
    public long Original { get; }

    public IReadOnlyList<long> Copies { get; }
}

/// <summary>
/// Elements that share an id because one was copied from the other: copy and paste, a placed group,
/// a mirrored or arrayed element. Revit copies the id with the element.
/// </summary>
public static class CopiedIds
{
    public static IReadOnlyList<CopiedId> Find(IEnumerable<AuditIdValue> values) =>
        values
            .Where(entry => entry.Value.Length > 0)
            .GroupBy(entry => entry.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Entries: group.ToList(), Elements: group.Select(entry => entry.ElementId).Distinct().OrderBy(id => id).ToList()))
            .Where(shared => shared.Elements.Count > 1)
            .Select(shared => new CopiedId(
                shared.Entries.First(entry => entry.ElementId == shared.Elements[0]).Value,
                shared.Elements[0],
                shared.Elements.Skip(1).ToList()))
            .ToList();

    /// <summary>
    /// The copies that can take a new id. A copy whose id holds one value per group type cannot: a new
    /// value would reach every instance of its group, the original's copy too.
    /// </summary>
    public static (IReadOnlyList<long> Reissue, IReadOnlyList<long> Kept) Split(CopiedId copied, Func<long, bool> holdsOneValuePerGroupType)
    {
        List<long> kept = copied.Copies.Where(holdsOneValuePerGroupType).ToList();
        return (copied.Copies.Except(kept).ToList(), kept);
    }

    /// <summary>The export's note on the copies that got a new id.</summary>
    public static string ReissuedLine(CopiedId copied, IReadOnlyList<long> reissued) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Element {0} keeps id {1} as the oldest; {2} copied from it got a new id: {3}. If an earlier export took only a copy, the GDB row with id {1} now belongs to element {0}.",
            copied.Original,
            copied.Id,
            reissued.Count == 1 ? "element" : "elements",
            string.Join(", ", reissued));

    /// <summary>The export's note on copies left sharing the id.</summary>
    public static string StillSharedLine(CopiedId copied, IReadOnlyList<long> kept, string why) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} still {2} id {3} with element {4}: {5}. The GDB gets one row per element with that id.",
            kept.Count == 1 ? "Element" : "Elements",
            string.Join(", ", kept),
            kept.Count == 1 ? "shares" : "share",
            copied.Id,
            copied.Original,
            why.TrimEnd('.'));
}
