using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

public enum GisFixKind
{
    /// <summary>Write parameter values, shown first and applied in one transaction.</summary>
    Write = 0,

    /// <summary>Run Set up model, whose own row previews what it does.</summary>
    SetUp = 1,

    /// <summary>Add a <c>GIS_</c> plan for one level, with the export's template and crop.</summary>
    AddPlan = 2,
}

/// <summary>One parameter value a fix writes. An editable value is a suggestion the user can change before applying.</summary>
public sealed class GisFixWrite
{
    public GisFixWrite(long elementId, string elementName, string parameter, string oldValue, string newValue, bool editable = false, bool wholeNumber = false)
    {
        ElementId = elementId;
        ElementName = elementName ?? string.Empty;
        Parameter = parameter ?? string.Empty;
        OldValue = oldValue ?? string.Empty;
        NewValue = newValue ?? string.Empty;
        Editable = editable;
        WholeNumber = wholeNumber;
    }

    public long ElementId { get; }

    public string ElementName { get; }

    public string Parameter { get; }

    public string OldValue { get; }

    public string NewValue { get; }

    public bool Editable { get; }

    public bool WholeNumber { get; }

    public GisFixWrite With(string newValue) => new(ElementId, ElementName, Parameter, OldValue, newValue, Editable, WholeNumber);

    public string Label() => string.Format(
        CultureInfo.InvariantCulture,
        "{0}: {1} {2} -> {3}",
        ElementName,
        Parameter,
        OldValue.Length > 0 ? OldValue : "(empty)",
        NewValue.Length > 0 ? NewValue : "(empty)");
}

/// <summary>
/// What a finding's Fix button does. The id names the finding's subject, so the model is read again
/// when the fix is applied and a fix whose finding has gone is refused instead of applied stale.
/// </summary>
public sealed class GisFix
{
    private GisFix(string id, GisFixKind kind, IEnumerable<GisFixWrite>? writes, long? levelId)
    {
        Id = id;
        Kind = kind;
        Writes = (writes ?? Array.Empty<GisFixWrite>()).ToList();
        LevelId = levelId;
    }

    public string Id { get; }

    public GisFixKind Kind { get; }

    public IReadOnlyList<GisFixWrite> Writes { get; }

    /// <summary>The level an <see cref="GisFixKind.AddPlan"/> fix adds a plan for.</summary>
    public long? LevelId { get; }

    public static GisFix SetUp { get; } = new("setup", GisFixKind.SetUp, null, null);

    public static GisFix Write(string id, IEnumerable<GisFixWrite> writes) => new(id, GisFixKind.Write, writes, null);

    public static GisFix AddPlan(long levelId) => new("plan:" + levelId.ToString(CultureInfo.InvariantCulture), GisFixKind.AddPlan, null, levelId);

    /// <summary>
    /// The writes to make when the user applies this fix, read fresh from the model, with the values
    /// the user edited (keyed by element id and parameter; only editable writes take one). A write
    /// that would leave the value as it is drops out. Refused when an edited whole number is not
    /// one, or nothing is left to write.
    /// </summary>
    public (IReadOnlyList<GisFixWrite> Writes, string? Refused) Accept(IReadOnlyDictionary<(long ElementId, string Parameter), string>? edits)
    {
        List<GisFixWrite> writes = new();
        foreach (GisFixWrite write in Writes)
        {
            string value = write.Editable && edits != null && edits.TryGetValue((write.ElementId, write.Parameter), out string? edited)
                ? (edited ?? string.Empty).Trim()
                : write.NewValue;
            if (write.WholeNumber && value.Length > 0 && !int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            {
                return (Array.Empty<GisFixWrite>(), $"{write.ElementName}: {write.Parameter} takes a whole number, not '{value}'.");
            }

            if (value != write.OldValue)
            {
                writes.Add(write.With(value));
            }
        }

        return writes.Count == 0 ? (writes, "Nothing to change: every value is already what the fix would write.") : (writes, null);
    }
}
