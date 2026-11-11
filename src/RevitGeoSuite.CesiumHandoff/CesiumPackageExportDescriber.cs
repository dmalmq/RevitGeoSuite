using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.CesiumHandoff;

/// <summary>Builds the manifest's <c>export</c> section from the settings a 3D Tiles export ran with.</summary>
public static class CesiumPackageExportDescriber
{
    /// <param name="isViewScope">True when the export covered the elements of one 3D view.</param>
    /// <param name="viewUniqueId">That view's UniqueId; ignored for a whole-model export.</param>
    /// <param name="viewName">That view's name, for display.</param>
    /// <param name="links">The link instances exported: the instance UniqueId and its name.</param>
    /// <param name="levelOfDetail">The level of detail's name, such as <c>Fine</c>.</param>
    /// <param name="exactPlacement">Whether every vertex was unprojected from the project CRS.</param>
    public static CesiumPackageExport Describe(
        bool isViewScope,
        string? viewUniqueId,
        string? viewName,
        IEnumerable<(string UniqueId, string Name)> links,
        string levelOfDetail,
        bool exactPlacement)
    {
        if (links is null)
        {
            throw new ArgumentNullException(nameof(links));
        }

        return new CesiumPackageExport
        {
            Scope = isViewScope ? CesiumExportScopes.View : CesiumExportScopes.Whole,
            View = isViewScope && !string.IsNullOrWhiteSpace(viewUniqueId)
                ? new CesiumPackageExportItem { UniqueId = viewUniqueId!, Name = viewName ?? string.Empty }
                : null,
            Links = links
                .Where(link => !string.IsNullOrWhiteSpace(link.UniqueId))
                .Select(link => new CesiumPackageExportItem { UniqueId = link.UniqueId, Name = link.Name ?? string.Empty })
                .OrderBy(link => link.UniqueId, StringComparer.Ordinal)
                .ToList(),
            LevelOfDetail = (levelOfDetail ?? string.Empty).Trim().ToLowerInvariant(),
            ExactPlacement = exactPlacement,
        };
    }
}
