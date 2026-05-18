using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DExportReferenceContext
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

    public Tiles3DExportReferenceContext Copy()
    {
        return new Tiles3DExportReferenceContext
        {
            Title = Title,
            Description = Description,
            ProjectCrs = new CrsReference
            {
                EpsgCode = ProjectCrs.EpsgCode,
                NameSnapshot = ProjectCrs.NameSnapshot
            },
            AnchorProjectedCoordinate = AnchorProjectedCoordinate,
            AnchorLatitude = AnchorLatitude,
            AnchorLongitude = AnchorLongitude,
            AnchorElevationMeters = AnchorElevationMeters,
            AnchorXFeet = AnchorXFeet,
            AnchorYFeet = AnchorYFeet,
            AnchorZFeet = AnchorZFeet
        };
    }
}
