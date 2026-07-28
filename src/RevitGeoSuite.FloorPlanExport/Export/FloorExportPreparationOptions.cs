using System.Collections.Generic;
using RevitGeoSuite.FloorPlanExport.Core.Geometry;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Schema;
using RevitGeoSuite.FloorPlanExport.Core.Validation;

namespace RevitGeoSuite.FloorPlanExport.Export;

public sealed class FloorExportPreparationOptions
{
    public IReadOnlyDictionary<string, string>? FloorCategoryOverrides { get; set; }

    public IReadOnlyDictionary<string, string>? RoomCategoryOverrides { get; set; }

    public IReadOnlyDictionary<string, string>? FamilyCategoryOverrides { get; set; }

    public IReadOnlyList<string>? AcceptedOpeningFamilies { get; set; }

    public IReadOnlyList<string>? InitialWarnings { get; set; }

    /// <summary>
    /// Geometry repair settings for export preparation.
    /// Unit-level and level-boundary gap/hole controls are configured separately via <see cref="GeometryRepairOptions"/>.
    /// </summary>
    public GeometryRepairOptions? GeometryRepairOptions { get; set; }

    public UnitSource UnitSource { get; set; } = UnitSource.Floors;

    public UnitGeometrySource UnitGeometrySource { get; set; } = UnitGeometrySource.Unset;

    public UnitAttributeSource UnitAttributeSource { get; set; } = UnitAttributeSource.Unset;

    public string RoomCategoryParameterName { get; set; } = "Name";

    public LinkExportOptions LinkExportOptions { get; set; } = new();

    public bool SimplifyStairUnits { get; set; }

    public bool SimplifyEscalatorUnits { get; set; }

    /// <summary>
    /// Include list of unit categories to export. Empty or null exports every category.
    /// Applied to the final unit layer only; openings and level boundaries still see all units.
    /// </summary>
    public IReadOnlyList<string>? UnitCategories { get; set; }

    public SchemaProfile ActiveSchemaProfile { get; set; } = SchemaProfile.CreateCoreProfile();

    public ValidationPolicyProfile ActiveValidationPolicyProfile { get; set; } = ValidationPolicyProfile.CreateRecommendedProfile();

    internal IReadOnlyList<ViewExportContext>? ViewContexts { get; set; }
}
