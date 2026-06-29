using System.Collections.Generic;

namespace RevitGeoSuite.Core.Coordinates;

public static class AsiaPacificCrsPresets
{
    public static IReadOnlyList<CrsDefinition> CreateDefinitions()
    {
        return new[]
        {
            new CrsDefinition
            {
                EpsgCode = 7855,
                Name = "GDA2020 / MGA zone 55",
                DatumName = "GDA2020",
                UnitName = "metre",
                RegionGroup = "Asia-Pacific",
                LatitudeOfOrigin = 0.0,
                CentralMeridian = 147.0,
                ScaleFactor = 0.9996,
                FalseEasting = 500000.0,
                FalseNorthing = 10000000.0,
                AreaSummary = "Australia (Victoria, Tasmania)"
            },
            new CrsDefinition
            {
                EpsgCode = 7856,
                Name = "GDA2020 / MGA zone 56",
                DatumName = "GDA2020",
                UnitName = "metre",
                RegionGroup = "Asia-Pacific",
                LatitudeOfOrigin = 0.0,
                CentralMeridian = 153.0,
                ScaleFactor = 0.9996,
                FalseEasting = 500000.0,
                FalseNorthing = 10000000.0,
                AreaSummary = "Australia (New South Wales, Queensland)"
            },
            new CrsDefinition
            {
                EpsgCode = 2193,
                Name = "NZGD2000 / New Zealand Transverse Mercator",
                DatumName = "NZGD2000",
                UnitName = "metre",
                RegionGroup = "Asia-Pacific",
                LatitudeOfOrigin = 0.0,
                CentralMeridian = 173.0,
                ScaleFactor = 0.9996,
                FalseEasting = 1600000.0,
                FalseNorthing = 10000000.0,
                AreaSummary = "New Zealand"
            },
            new CrsDefinition
            {
                EpsgCode = 3414,
                Name = "SVY21 / Singapore TM",
                DatumName = "SVY21",
                UnitName = "metre",
                RegionGroup = "Asia-Pacific",
                LatitudeOfOrigin = 1.366666666666667,
                CentralMeridian = 103.8333333333333,
                ScaleFactor = 1.0,
                FalseEasting = 28001.642,
                FalseNorthing = 38744.572,
                AreaSummary = "Singapore"
            },
        };
    }
}
