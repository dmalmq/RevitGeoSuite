namespace RevitGeoSuite.FloorPlanExport.Core.Assignments;

public enum FloorCategoryResolutionSource
{
    Catalog,
    Override,
    FallbackUnspecified,

    /// <summary>The floor type's <c>gis_category</c> parameter.</summary>
    TypeParameter,

    /// <summary>The floor's own <c>category</c> parameter, which overrides its type.</summary>
    InstanceParameter,
}
