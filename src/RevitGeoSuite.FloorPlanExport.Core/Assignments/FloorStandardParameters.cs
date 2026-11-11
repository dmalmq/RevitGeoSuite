namespace RevitGeoSuite.FloorPlanExport.Core.Assignments;

/// <summary>
/// The standard parameters a floor's category comes from: its own <c>category</c>, and its type's
/// <c>gis_category</c> and <c>gis_restriction</c>.
/// </summary>
public sealed class FloorStandardParameters
{
    public FloorStandardParameters(
        string? instanceCategory,
        string? typeCategory,
        string? typeRestriction,
        bool modelSetUpWithStandard = false)
    {
        InstanceCategory = instanceCategory;
        TypeCategory = typeCategory;
        TypeRestriction = typeRestriction;
        ModelSetUpWithStandard = modelSetUpWithStandard;
    }

    public string? InstanceCategory { get; }

    public string? TypeCategory { get; }

    public string? TypeRestriction { get; }

    /// <summary>
    /// Whether the model was set up with the Kanizu GIS standard (Project Information has
    /// <c>gis_standard_version</c>). Only then is every floor <c>category</c> known to be one somebody set.
    /// </summary>
    public bool ModelSetUpWithStandard { get; }
}
