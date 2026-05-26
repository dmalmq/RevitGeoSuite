using NetTopologySuite.Operation.Buffer;

namespace RevitGeoSuite.PlateauImport;

public sealed class SidewalkStripOptions
{
    public double WidthMetres { get; set; } = 4.0d;

    public double CurvatureTurnThresholdRadians { get; set; } = 0.15d;

    public double RoadSearchDistance { get; set; } = 10.0d;

    public JoinStyle JoinStyle { get; set; } = JoinStyle.Mitre;

    public EndCapStyle EndCapStyle { get; set; } = EndCapStyle.Flat;

    public double MitreLimit { get; set; } = 5.0d;

    public int QuadrantSegments { get; set; } = 4;

    public double MinimumPolygonArea { get; set; } = 0.1d;

    public static SidewalkStripOptions Default => new SidewalkStripOptions();
}
