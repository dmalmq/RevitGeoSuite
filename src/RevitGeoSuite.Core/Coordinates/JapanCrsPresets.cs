using System.Collections.Generic;

namespace RevitGeoSuite.Core.Coordinates;

public static class JapanCrsPresets
{
    public static IReadOnlyList<CrsDefinition> CreateDefinitions()
    {
        return new[]
        {
            CreateDefinition(6669, 1, "I", "JGD2011 / Japan Plane Rectangular CS I", 33.0, 129.5, "Nagasaki and western Kagoshima"),
            CreateDefinition(6670, 2, "II", "JGD2011 / Japan Plane Rectangular CS II", 33.0, 131.0, "Fukuoka, Saga, Kumamoto, Oita, Miyazaki and most of Kagoshima"),
            CreateDefinition(6671, 3, "III", "JGD2011 / Japan Plane Rectangular CS III", 36.0, 132.16666666666666, "Yamaguchi, Shimane and Hiroshima"),
            CreateDefinition(6672, 4, "IV", "JGD2011 / Japan Plane Rectangular CS IV", 33.0, 133.5, "Kagawa, Ehime, Tokushima and Kochi"),
            CreateDefinition(6673, 5, "V", "JGD2011 / Japan Plane Rectangular CS V", 36.0, 134.33333333333334, "Hyogo, Tottori and Okayama"),
            CreateDefinition(6674, 6, "VI", "JGD2011 / Japan Plane Rectangular CS VI", 36.0, 136.0, "Kyoto, Osaka, Fukui, Shiga, Mie, Nara and Wakayama"),
            CreateDefinition(6675, 7, "VII", "JGD2011 / Japan Plane Rectangular CS VII", 36.0, 137.16666666666666, "Ishikawa, Toyama, Gifu and Aichi"),
            CreateDefinition(6676, 8, "VIII", "JGD2011 / Japan Plane Rectangular CS VIII", 36.0, 138.5, "Niigata, Nagano, Yamanashi and Shizuoka"),
            CreateDefinition(6677, 9, "IX", "JGD2011 / Japan Plane Rectangular CS IX", 36.0, 139.83333333333334, "Tokyo, Kanagawa, Chiba, Saitama, Ibaraki, Tochigi, Gunma and Fukushima"),
            CreateDefinition(6678, 10, "X", "JGD2011 / Japan Plane Rectangular CS X", 40.0, 140.83333333333334, "Aomori, Akita, Yamagata, Iwate and Miyagi"),
            CreateDefinition(6679, 11, "XI", "JGD2011 / Japan Plane Rectangular CS XI", 44.0, 140.25, "Southwestern Hokkaido"),
            CreateDefinition(6680, 12, "XII", "JGD2011 / Japan Plane Rectangular CS XII", 44.0, 142.25, "Central and northern Hokkaido"),
            CreateDefinition(6681, 13, "XIII", "JGD2011 / Japan Plane Rectangular CS XIII", 44.0, 144.25, "Eastern Hokkaido"),
            CreateDefinition(6682, 14, "XIV", "JGD2011 / Japan Plane Rectangular CS XIV", 26.0, 142.0, "Tokyo islands east of 140°30' E"),
            CreateDefinition(6683, 15, "XV", "JGD2011 / Japan Plane Rectangular CS XV", 26.0, 127.5, "Central Okinawa"),
            CreateDefinition(6684, 16, "XVI", "JGD2011 / Japan Plane Rectangular CS XVI", 26.0, 124.0, "Western Okinawa"),
            CreateDefinition(6685, 17, "XVII", "JGD2011 / Japan Plane Rectangular CS XVII", 26.0, 131.0, "Eastern Okinawa"),
            CreateDefinition(6686, 18, "XVIII", "JGD2011 / Japan Plane Rectangular CS XVIII", 20.0, 136.0, "Tokyo islands west of 140°30' E"),
            CreateDefinition(6687, 19, "XIX", "JGD2011 / Japan Plane Rectangular CS XIX", 26.0, 154.0, "Tokyo islands east of 143° E")
        };
    }

    private static CrsDefinition CreateDefinition(int epsgCode, int zoneNumber, string zoneLabel, string name, double latitudeOfOrigin, double centralMeridian, string areaSummary)
    {
        return new CrsDefinition
        {
            EpsgCode = epsgCode,
            Name = name,
            DatumName = "JGD2011",
            UnitName = "metre",
            JapanZoneNumber = zoneNumber,
            ZoneLabel = zoneLabel,
            LatitudeOfOrigin = latitudeOfOrigin,
            CentralMeridian = centralMeridian,
            ScaleFactor = 0.9999,
            FalseEasting = 0.0,
            FalseNorthing = 0.0,
            AreaSummary = areaSummary
        };
    }
}
