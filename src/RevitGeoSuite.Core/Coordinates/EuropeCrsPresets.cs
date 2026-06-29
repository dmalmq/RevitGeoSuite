using System.Collections.Generic;

namespace RevitGeoSuite.Core.Coordinates;

public static class EuropeCrsPresets
{
    public static IReadOnlyList<CrsDefinition> CreateDefinitions()
    {
        return new[]
        {
            Utm(25832, "ETRS89 / UTM zone 32N", 9.0, "ETRS89", "Germany (west), Netherlands, Denmark, Austria"),
            Utm(25833, "ETRS89 / UTM zone 33N", 15.0, "ETRS89", "Germany (east), Poland, Czech Republic, Italy (north)"),
            Utm(25834, "ETRS89 / UTM zone 34N", 21.0, "ETRS89", "Greece, Romania, Baltic states"),
            Utm(25835, "ETRS89 / UTM zone 35N", 27.0, "ETRS89", "Finland, Ukraine, Turkey"),

            new CrsDefinition
            {
                EpsgCode = 27700,
                Name = "OSGB 1936 / British National Grid",
                DatumName = "OSGB 1936",
                UnitName = "metre",
                RegionGroup = "Europe",
                LatitudeOfOrigin = 49.0,
                CentralMeridian = -2.0,
                ScaleFactor = 0.9996012717,
                FalseEasting = 400000.0,
                FalseNorthing = -100000.0,
                AreaSummary = "United Kingdom"
            },

            new CrsDefinition
            {
                EpsgCode = 2154,
                Name = "RGF93 v1 / Lambert-93",
                DatumName = "RGF93 v1",
                UnitName = "metre",
                RegionGroup = "Europe",
                ProjectionMethod = "Lambert_Conformal_Conic_2SP",
                LatitudeOfOrigin = 46.5,
                CentralMeridian = 3.0,
                ScaleFactor = 1.0,
                FalseEasting = 700000.0,
                FalseNorthing = 6600000.0,
                StandardParallel1 = 44.0,
                StandardParallel2 = 49.0,
                AreaSummary = "France"
            },

            // RD New uses Oblique Stereographic — needs WKT since ProjNet can't build it from parameters.
            new CrsDefinition
            {
                EpsgCode = 28992,
                Name = "Amersfoort / RD New",
                DatumName = "Amersfoort",
                UnitName = "metre",
                RegionGroup = "Europe",
                ProjectionMethod = "Oblique_Stereographic",
                LatitudeOfOrigin = 52.15616055555555,
                CentralMeridian = 5.38763888888889,
                ScaleFactor = 0.9999079,
                FalseEasting = 155000.0,
                FalseNorthing = 463000.0,
                AreaSummary = "Netherlands",
                Wkt = "PROJCS[\"Amersfoort / RD New\",GEOGCS[\"Amersfoort\",DATUM[\"Amersfoort\",SPHEROID[\"Bessel 1841\",6377397.155,299.1528128]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Oblique_Stereographic\"],PARAMETER[\"latitude_of_origin\",52.15616055555555],PARAMETER[\"central_meridian\",5.38763888888889],PARAMETER[\"scale_factor\",0.9999079],PARAMETER[\"false_easting\",155000],PARAMETER[\"false_northing\",463000],UNIT[\"metre\",1]]"
            },

            // CH1903+ / LV95 uses Hotine Oblique Mercator — needs WKT.
            new CrsDefinition
            {
                EpsgCode = 2056,
                Name = "CH1903+ / LV95",
                DatumName = "CH1903+",
                UnitName = "metre",
                RegionGroup = "Europe",
                ProjectionMethod = "Hotine_Oblique_Mercator_Azimuth_Center",
                LatitudeOfOrigin = 46.95240555555556,
                CentralMeridian = 7.439583333333333,
                ScaleFactor = 1.0,
                FalseEasting = 2600000.0,
                FalseNorthing = 1200000.0,
                AreaSummary = "Switzerland, Liechtenstein",
                Wkt = "PROJCS[\"CH1903+ / LV95\",GEOGCS[\"CH1903+\",DATUM[\"CH1903+\",SPHEROID[\"Bessel 1841\",6377397.155,299.1528128]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Hotine_Oblique_Mercator_Azimuth_Center\"],PARAMETER[\"latitude_of_center\",46.95240555555556],PARAMETER[\"longitude_of_center\",7.439583333333333],PARAMETER[\"azimuth\",90],PARAMETER[\"rectified_grid_angle\",90],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",2600000],PARAMETER[\"false_northing\",1200000],UNIT[\"metre\",1]]"
            },

            Utm(3006, "SWEREF99 TM", 15.0, "SWEREF99", "Sweden"),
            Utm(3067, "ETRS89 / TM35FIN(E,N)", 27.0, "ETRS89", "Finland"),
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
            RegionGroup = "Europe",
            LatitudeOfOrigin = 0.0,
            CentralMeridian = centralMeridian,
            ScaleFactor = 0.9996,
            FalseEasting = 500000.0,
            FalseNorthing = 0.0,
            AreaSummary = areaSummary
        };
    }
}
