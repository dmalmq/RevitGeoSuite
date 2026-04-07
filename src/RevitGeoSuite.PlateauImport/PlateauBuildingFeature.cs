namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauBuildingFeature : PlateauContextFeature
{
    public PlateauBuildingFeature()
    {
        FeatureType = PlateauFeatureType.Building;
    }
}
