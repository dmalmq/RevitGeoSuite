using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportReferenceContext
{
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public CrsReference ProjectCrs { get; set; } = new CrsReference();

    public ProjectedCoordinate AnchorProjectedCoordinate { get; set; }

    public double AnchorLatitude { get; set; }

    public double AnchorLongitude { get; set; }

    public double AnchorElevationMeters { get; set; }

    public double AnchorXFeet { get; set; }

    public double AnchorYFeet { get; set; }

    public double AnchorZFeet { get; set; }

    public double SharedEastToLocalX { get; set; } = 1d;

    public double SharedEastToLocalY { get; set; }

    public double SharedNorthToLocalX { get; set; }

    public double SharedNorthToLocalY { get; set; } = 1d;
}
