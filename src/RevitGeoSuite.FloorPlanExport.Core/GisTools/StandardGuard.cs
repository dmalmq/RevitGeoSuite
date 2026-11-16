using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>
/// G4: the rules of the warning Revit shows the moment an element outside the standard is placed or
/// re-typed on an exported level. The same rules as <see cref="GisAudit"/>'s guesses, for one element.
/// A warning, never a block.
/// </summary>
public static class StandardGuard
{
    /// <summary>Whether a floor type is outside the standard: it has no unit category the standard knows.</summary>
    public static bool FloorTypeOutside(string? gisCategory, GisToolStandard standard) =>
        !(standard ?? throw new ArgumentNullException(nameof(standard))).IsKnownCategory(gisCategory);

    /// <summary>
    /// The kind of a family that is outside the standard, or null when it is inside or is not a kind the
    /// standard lists: its gis_kind names a kind but the family is not listed under it, or it has no
    /// gis_kind and no unit category, the export dialog does not map it by hand, and its name suggests
    /// a kind it is not listed under. A listed family is silent whatever its gis_kind: a wrong or
    /// missing gis_kind on a listed family is the audit's to report, not a placement mistake.
    /// </summary>
    public static string? FamilyOutside(string familyName, string typeName, string? gisKind, string? gisCategory, GisToolStandard standard, IReadOnlyCollection<string>? mappedFamilies = null)
    {
        if (standard is null)
        {
            throw new ArgumentNullException(nameof(standard));
        }

        string kind = GisToolStandard.Normalize(gisKind).ToLowerInvariant();
        StandardFamilyKind? listed = standard.Families.KindOf(familyName);
        if (kind.Length > 0)
        {
            return GisAudit.GisKinds.Contains(kind) && listed == null ? (kind == "movingwalkway" ? "moving_walkway" : kind) : null;
        }

        if (listed != null || standard.IsKnownCategory(gisCategory) || (mappedFamilies?.Contains(familyName.Trim()) ?? false))
        {
            return null;
        }

        return GisAudit.SuggestKind(familyName + " " + typeName);
    }

    /// <summary>"j EV": the standard families of a kind, for the warning; empty when the standard lists none yet.</summary>
    public static string StandardFamilies(GisToolStandard standard, string kind)
    {
        StandardFamilyKind? listed = standard.Families.Kind(kind);
        return listed == null || listed.Families.Count == 0 ? string.Empty : string.Join(", ", listed.Families);
    }
}
