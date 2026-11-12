using System;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Models;

public static class ImdfRestrictionNormalizer
{
    /// <summary>
    /// The unit attribute that carries the unit's side of the ticket gates as the export resolves it
    /// (<c>rachi_nai</c> inside, <c>rachi_gai</c> outside), which <c>restrict</c> leaves out: the
    /// type's <c>gis_restriction</c>, or for a type without one the side its name gives. Kanizu reads it to pick the station's category code (<c>walkway_sta</c>,
    /// <c>store</c>) and does not write it to the GDB.
    /// </summary>
    public const string FloorTypeAttributeName = "gis_restriction";

    /// <summary>The floor type's restriction, trimmed, or null when it has none.</summary>
    public static string? FloorTypeRestriction(string? restriction)
    {
        return string.IsNullOrWhiteSpace(restriction) ? null : restriction.Trim();
    }

    public static string? NormalizeUnitRestriction(string? restriction)
    {
        if (string.IsNullOrWhiteSpace(restriction))
        {
            return null;
        }

        string trimmed = restriction.Trim();
        string compact = new(trimmed.Where(ch => ch != '_' && ch != '-' && !char.IsWhiteSpace(ch)).ToArray());
        return compact.Equals("rachigai", StringComparison.OrdinalIgnoreCase) ||
               compact.Equals("rachinai", StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }
}
