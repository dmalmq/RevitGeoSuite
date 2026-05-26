using System.Collections.Generic;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

public sealed class PlateauTilesetTriangle
{
    public PlateauTilesetTriangle(Vector3d a, Vector3d b, Vector3d c)
    {
        A = a; B = b; C = c;
    }

    public Vector3d A { get; }
    public Vector3d B { get; }
    public Vector3d C { get; }
}

public sealed class PlateauTilesetFeatureMesh
{
    public PlateauTilesetFeatureMesh(int batchId, List<PlateauTilesetTriangle> triangles, IReadOnlyDictionary<string, object?> attributes)
    {
        BatchId = batchId;
        Triangles = triangles;
        Attributes = attributes;
    }

    public int BatchId { get; }

    public List<PlateauTilesetTriangle> Triangles { get; }

    public IReadOnlyDictionary<string, object?> Attributes { get; }

    public string? GmlId => GetStringAttribute("gml_id");

    public string? FeatureType => GetStringAttribute("feature_type");

    public string? GetStringAttribute(string key)
    {
        if (Attributes.TryGetValue(key, out object? value)) return value?.ToString();
        return null;
    }
}
