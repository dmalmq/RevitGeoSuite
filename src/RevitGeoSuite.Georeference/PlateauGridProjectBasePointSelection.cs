using System;
using System.Collections.Generic;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.Georeference;

public sealed class PlateauGridProjectBasePointSelection
{
    public IReadOnlyCollection<string> SelectedMeshCodes { get; set; } = Array.Empty<string>();

    public double AnchorLatitude { get; set; }

    public double AnchorLongitude { get; set; }

    public ProjectedCoordinate? ProjectedCoordinate { get; set; }
}

