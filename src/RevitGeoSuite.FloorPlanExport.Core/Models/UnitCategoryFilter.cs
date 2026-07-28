using System;
using System.Collections.Generic;

namespace RevitGeoSuite.FloorPlanExport.Core.Models;

/// <summary>
/// Decides whether a unit feature's category passes the user-selected export
/// include list. An empty or missing list means no filtering.
/// </summary>
public static class UnitCategoryFilter
{
    public static bool ShouldInclude(string? category, IReadOnlyCollection<string>? includedCategories)
    {
        if (includedCategories == null || includedCategories.Count == 0)
        {
            return true;
        }

        string normalizedCategory = category?.Trim() ?? string.Empty;
        bool hasIncludedCategory = false;

        foreach (string includedCategory in includedCategories)
        {
            string normalizedIncludedCategory = includedCategory?.Trim() ?? string.Empty;
            if (normalizedIncludedCategory.Length == 0)
            {
                continue;
            }

            hasIncludedCategory = true;
            if (normalizedCategory.Length > 0 &&
                string.Equals(normalizedIncludedCategory, normalizedCategory, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return !hasIncludedCategory;
    }
}
