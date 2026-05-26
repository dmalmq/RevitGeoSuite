using System;
using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class RevitModelFootprintFeature
{
    public RevitModelFootprintFeature(
        string layer,
        string category,
        bool isPolygon,
        IReadOnlyList<(double X, double Y)> verticesMetres,
        long elementId,
        string? elementName = null)
    {
        Layer = layer ?? throw new ArgumentNullException(nameof(layer));
        Category = category ?? string.Empty;
        IsPolygon = isPolygon;
        VerticesMetres = verticesMetres ?? throw new ArgumentNullException(nameof(verticesMetres));
        ElementId = elementId;
        ElementName = elementName ?? string.Empty;
    }

    public string Layer { get; }

    public string Category { get; }

    public bool IsPolygon { get; }

    public IReadOnlyList<(double X, double Y)> VerticesMetres { get; }

    public long ElementId { get; }

    public string ElementName { get; }
}
