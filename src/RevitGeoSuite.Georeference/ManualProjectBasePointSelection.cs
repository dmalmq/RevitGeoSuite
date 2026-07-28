using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.Georeference;

public sealed class ManualProjectBasePointSelection
{
    public double AnchorLatitude { get; set; }

    public double AnchorLongitude { get; set; }

    public ProjectedCoordinate ProjectedCoordinate { get; set; }
}
