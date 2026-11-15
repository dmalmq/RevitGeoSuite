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

    /// <summary>The export's note on the ids it gave the copies.</summary>
    public static string ReissuedLine(CopiedId copied) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Element {0} keeps id {1}; {2} copied from it got a new id: {3}.",
            copied.Original,
            copied.Id,
            copied.Copies.Count == 1 ? "element" : "elements",
            string.Join(", ", copied.Copies));
}
