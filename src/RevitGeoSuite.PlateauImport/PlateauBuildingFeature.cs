namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauBuildingFeature : PlateauContextFeature
{
    public PlateauBuildingFeature()
    {
        FeatureType = PlateauFeatureType.Building;
    }

    public double? BaseElevationMeters { get; set; }

    public double? TopElevationMeters { get; set; }
}
