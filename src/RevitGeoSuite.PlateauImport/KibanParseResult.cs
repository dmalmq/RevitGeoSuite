using System;
using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class KibanParseResult
{
    public KibanParseResult(
        IReadOnlyList<KibanParsedFeature> lines,
        IReadOnlyList<KibanParsedPolygonFeature> polygons)
    {
        Lines = lines ?? throw new ArgumentNullException(nameof(lines));
        Polygons = polygons ?? throw new ArgumentNullException(nameof(polygons));
    }

    public IReadOnlyList<KibanParsedFeature> Lines { get; }

    public IReadOnlyList<KibanParsedPolygonFeature> Polygons { get; }
}
