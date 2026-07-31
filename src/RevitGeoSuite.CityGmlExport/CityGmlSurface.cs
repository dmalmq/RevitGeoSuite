using System;
using System.Collections.Generic;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlSurface
{
    public IReadOnlyList<CityGmlCoordinate> ExteriorRing { get; set; } = Array.Empty<CityGmlCoordinate>();
}

