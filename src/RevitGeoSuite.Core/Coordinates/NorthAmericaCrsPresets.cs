using System.Collections.Generic;

namespace RevitGeoSuite.Core.Coordinates;

public static class NorthAmericaCrsPresets
{
    public static IReadOnlyList<CrsDefinition> CreateDefinitions()
    {
        return new[]
        {
            Utm(26917, "NAD83 / UTM zone 17N", -81.0, "NAD83", "Eastern US (Ohio, Michigan, Florida)"),
            Utm(26918, "NAD83 / UTM zone 18N", -75.0, "NAD83", "Eastern US (New York, Pennsylvania, Virginia)"),
            Utm(26919, "NAD83 / UTM zone 19N", -69.0, "NAD83", "Northeastern US (Maine, Massachusetts)"),
            Utm(32617, "WGS 84 / UTM zone 17N", -81.0, "WGS 84", "Eastern US (UTM/WGS84)"),
            Utm(32618, "WGS 84 / UTM zone 18N", -75.0, "WGS 84", "Eastern US (UTM/WGS84)"),
        };
    }

    private static CrsDefinition Utm(int epsgCode, string name, double centralMeridian, string datumName, string areaSummary)
    {
        return new CrsDefinition
        {
            EpsgCode = epsgCode,
            Name = name,
            DatumName = datumName,
            UnitName = "metre",
            RegionGroup = "North America",
            LatitudeOfOrigin = 0.0,
            CentralMeridian = centralMeridian,
            ScaleFactor = 0.9996,
            FalseEasting = 500000.0,
            FalseNorthing = 0.0,
            AreaSummary = areaSummary
        };
    }
}
