namespace RevitGeoSuite.FloorPlanExport.Core.Assignments;

/// <summary>
/// The standard parameters a floor's category comes from: its own <c>category</c>, and its type's
/// <c>gis_category</c> and <c>gis_restriction</c>.
/// </summary>
public sealed class FloorStandardParameters
{
    public FloorStandardParameters(string? instanceCategory, string? typeCategory, string? typeRestriction)
    {
        InstanceCategory = instanceCategory;
        TypeCategory = typeCategory;
        TypeRestriction = typeRestriction;
    }

    public string? InstanceCategory { get; }

    public string? TypeCategory { get; }

    public string? TypeRestriction { get; }
}
