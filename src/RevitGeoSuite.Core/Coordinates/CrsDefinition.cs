namespace RevitGeoSuite.Core.Coordinates;

public sealed class CrsDefinition
{
    public int EpsgCode { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DatumName { get; set; } = string.Empty;

    public string UnitName { get; set; } = string.Empty;

    public string RegionGroup { get; set; } = string.Empty;

    public string ProjectionMethod { get; set; } = "Transverse_Mercator";

    public int JapanZoneNumber { get; set; }

    public string ZoneLabel { get; set; } = string.Empty;

    public double LatitudeOfOrigin { get; set; }

    public double CentralMeridian { get; set; }

    public double ScaleFactor { get; set; }

    public double FalseEasting { get; set; }

    public double FalseNorthing { get; set; }

    public double StandardParallel1 { get; set; }

    public double StandardParallel2 { get; set; }

    public string AreaSummary { get; set; } = string.Empty;

    /// <summary>
    /// Optional OGC WKT definition. When set, the transformer uses this directly instead of
    /// building from projection parameters — needed for projection methods that ProjNet cannot
    /// construct from individual parameters (Oblique Stereographic, Hotine Oblique Mercator, etc.).
    /// </summary>
    public string? Wkt { get; set; }

    public CrsReference ToReference()
    {
        return new CrsReference
        {
            EpsgCode = EpsgCode,
            NameSnapshot = Name
        };
    }
}
