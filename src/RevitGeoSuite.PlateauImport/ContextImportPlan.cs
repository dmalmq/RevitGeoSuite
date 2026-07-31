using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class ContextImportPlan
{
    public string SourceFolderPath { get; set; } = string.Empty;

    public PlateauImportReferenceContext ReferenceContext { get; set; } = new PlateauImportReferenceContext();

    public PlateauGeometryImportMode GeometryImportMode { get; set; } = PlateauGeometryImportMode.LightweightExtrusion;

    public IReadOnlyCollection<PlateauCityModel> SourceModels { get; set; } = new PlateauCityModel[0];

    public IReadOnlyCollection<PlateauFeatureType> SelectedFeatureTypes { get; set; } = new PlateauFeatureType[0];

    public IReadOnlyCollection<string> SelectedTileIds { get; set; } = new string[0];

    public IReadOnlyCollection<ContextShapePlan> Shapes { get; set; } = new ContextShapePlan[0];

    public IReadOnlyCollection<string> WarningMessages { get; set; } = new string[0];

    public int SourceFeatureCount { get; set; }

    public int PreparedSurfaceCount { get; set; }

    public int PreparedTriangleCount { get; set; }
}
