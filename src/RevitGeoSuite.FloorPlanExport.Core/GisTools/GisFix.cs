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
/// when the fix is applied, and the fix is applied only when the fresh one is still what the user
/// was shown: a fix whose finding has gone, or whose values changed, is refused instead of applied stale.
/// </summary>
public sealed class GisFix
{
    /// <summary>Why a fix is refused when the model changed between showing it and applying it.</summary>
    public const string Changed = "Nothing was changed: the model changed since this fix was shown. Check again, then look at the fix before applying it.";

    private GisFix(string id, GisFixKind kind, IEnumerable<GisFixWrite>? writes, long? levelId, IEnumerable<string>? notes)
    {
        Id = id;
        Kind = kind;
        Writes = (writes ?? Array.Empty<GisFixWrite>()).ToList();
        LevelId = levelId;
        Notes = (notes ?? Array.Empty<string>()).ToList();
    }

    public string Id { get; }

    public GisFixKind Kind { get; }

    public IReadOnlyList<GisFixWrite> Writes { get; }

    /// <summary>The level an <see cref="GisFixKind.AddPlan"/> fix adds a plan for.</summary>
    public long? LevelId { get; }

    /// <summary>What the fix does beyond its writes, shown before it is applied: an added plan's steps, or which level keeps a shared id.</summary>
    public IReadOnlyList<string> Notes { get; }

    public static GisFix SetUp { get; } = new("setup", GisFixKind.SetUp, null, null, null);

    public static GisFix Write(string id, IEnumerable<GisFixWrite> writes, IEnumerable<string>? notes = null) => new(id, GisFixKind.Write, writes, null, notes);

    /// <param name="notes">The plan's steps, as <see cref="ExportViewPlanner.Describe"/> gives them.</param>
    public static GisFix AddPlan(long levelId, IEnumerable<string> notes) =>
        new("plan:" + levelId.ToString(CultureInfo.InvariantCulture), GisFixKind.AddPlan, null, levelId, notes);

    /// <summary>True when the notes the user was shown are this fix's notes.</summary>
    public bool ShowsSameNotes(IEnumerable<string?>? shown) => Notes.SequenceEqual((shown ?? Array.Empty<string?>()).Select(note => note ?? string.Empty));

    /// <summary>
    /// The writes to make when the user applies this fix, read fresh from the model, against the
    /// writes the user was shown (with the values they edited). Refused when the shown writes are not
    /// this fix's (another element, parameter, old value or fixed new value: the model changed), when
    /// an edit would empty a value that is set, when an edited whole number is not one, or when
    /// nothing is left to write. A write that would leave the value as it is drops out.
    /// </summary>
    public (IReadOnlyList<GisFixWrite> Writes, string? Refused) Accept(IReadOnlyList<GisFixWrite?>? shown, IEnumerable<string?>? shownNotes)
    {
        shown ??= Array.Empty<GisFixWrite?>();
        if (shown.Count != Writes.Count || !ShowsSameNotes(shownNotes))
        {
            return (Array.Empty<GisFixWrite>(), Changed);
        }

        List<GisFixWrite> writes = new();
        for (int index = 0; index < Writes.Count; index++)
        {
            GisFixWrite write = Writes[index];
            // A shown write comes from the page; a missing one is a write the user did not see.
            if (shown[index] is not GisFixWrite seen || seen.ElementId != write.ElementId || seen.Parameter != write.Parameter || seen.OldValue != write.OldValue || (!write.Editable && seen.NewValue != write.NewValue))
            {
                return (Array.Empty<GisFixWrite>(), Changed);
            }

            string value = write.Editable ? seen.NewValue.Trim() : write.NewValue;
            if (write.Editable && value.Length == 0 && write.OldValue.Length > 0)
            {
                return (Array.Empty<GisFixWrite>(), $"{write.ElementName}: {write.Parameter} is '{write.OldValue}'; the fix does not empty it. Type a value.");
            }

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
