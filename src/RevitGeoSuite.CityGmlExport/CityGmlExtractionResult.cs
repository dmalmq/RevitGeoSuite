using System;
using System.Collections.Generic;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlExtractionResult
{
    public IReadOnlyCollection<CityGmlFeature> Features { get; set; } = Array.Empty<CityGmlFeature>();

    public IReadOnlyCollection<string> Warnings { get; set; } = Array.Empty<string>();
}
