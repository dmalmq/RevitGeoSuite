using System;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Models;

public static class ImdfRestrictionNormalizer
{
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
