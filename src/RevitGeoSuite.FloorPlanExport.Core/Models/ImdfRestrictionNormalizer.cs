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

    /// <summary>
    /// The side of the gates as <c>rachi_nai</c> or <c>rachi_gai</c> however the type spells it
    /// (<c>RACHI_GAI</c>, <c>rachinai</c>, <c>rachi-nai</c>); any other restriction trimmed; null
    /// when there is none.
    /// </summary>
    public static string? FloorTypeRestriction(string? restriction)
    {
        if (string.IsNullOrWhiteSpace(restriction))
        {
            return null;
        }

        string trimmed = restriction.Trim();
        return Compact(trimmed) switch
        {
            "rachinai" => "rachi_nai",
            "rachigai" => "rachi_gai",
            _ => trimmed,
        };
    }

    public static string? NormalizeUnitRestriction(string? restriction)
    {
        if (string.IsNullOrWhiteSpace(restriction))
        {
            return null;
        }

        string trimmed = restriction.Trim();
        string compact = Compact(trimmed);
        return compact is "rachigai" or "rachinai" ? null : trimmed;
    }

    private static string Compact(string value) =>
        new string(value.Where(ch => ch != '_' && ch != '-' && !char.IsWhiteSpace(ch)).ToArray()).ToLowerInvariant();
}
