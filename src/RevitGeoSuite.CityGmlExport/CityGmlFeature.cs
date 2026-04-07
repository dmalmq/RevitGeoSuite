using System;
using System.Collections.Generic;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlFeature
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    public CityGmlSemanticType SemanticType { get; set; }

    public IReadOnlyList<CityGmlSurface> Surfaces { get; set; } = Array.Empty<CityGmlSurface>();

    public IReadOnlyList<CityGmlAttribute> Attributes { get; set; } = Array.Empty<CityGmlAttribute>();

    public CityGmlCodeAssignment? CodeAssignment { get; set; }
}

