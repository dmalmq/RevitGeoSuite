namespace RevitGeoSuite.Core.Coordinates;

public sealed class CrsDefinition
{
    public int EpsgCode { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DatumName { get; set; } = string.Empty;

    public string UnitName { get; set; } = string.Empty;

    public int JapanZoneNumber { get; set; }

    public string ZoneLabel { get; set; } = string.Empty;

    public double LatitudeOfOrigin { get; set; }

    public double CentralMeridian { get; set; }

    public double ScaleFactor { get; set; }

    public double FalseEasting { get; set; }

    public double FalseNorthing { get; set; }

    public string AreaSummary { get; set; } = string.Empty;

    public CrsReference ToReference()
    {
        return new CrsReference
        {
            EpsgCode = EpsgCode,
            NameSnapshot = Name
        };
    }
}
