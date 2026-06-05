using System;

namespace RevitGeoSuite.PlateauImport;

public static class PlateauFeatureTypeExtensions
{
    public static string GetDisplayName(this PlateauFeatureType featureType)
    {
        switch (featureType)
        {
            case PlateauFeatureType.Building:
                return "Building";
            case PlateauFeatureType.Bridge:
                return "Bridge";
            case PlateauFeatureType.Road:
                return "Road";
            case PlateauFeatureType.Sidewalk:
                return "Sidewalk";
            case PlateauFeatureType.Vegetation:
                return "Vegetation";
            case PlateauFeatureType.Relief:
                return "Relief";
            case PlateauFeatureType.LandUse:
                return "Land use";
            default:
                throw new ArgumentOutOfRangeException(nameof(featureType), featureType, "Unsupported PLATEAU feature type.");
        }
    }

    public static string GetPluralDisplayName(this PlateauFeatureType featureType)
    {
        switch (featureType)
        {
            case PlateauFeatureType.Building:
                return "Buildings";
            case PlateauFeatureType.Bridge:
                return "Bridges";
            case PlateauFeatureType.Road:
                return "Roads";
            case PlateauFeatureType.Sidewalk:
                return "Sidewalks";
            case PlateauFeatureType.Vegetation:
                return "Vegetation";
            case PlateauFeatureType.Relief:
                return "Relief";
            case PlateauFeatureType.LandUse:
                return "Land use";
            default:
                throw new ArgumentOutOfRangeException(nameof(featureType), featureType, "Unsupported PLATEAU feature type.");
        }
    }
}
