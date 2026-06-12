namespace RevitGeoSuite.Core.Plateau.Terrain;

/// <summary>
/// The decoded core of a quantized-mesh-1.0 tile: per-vertex normalized horizontal position
/// (u, v in [0,1], origin south-west) and absolute height in metres, plus the triangle index list.
/// Edge indices, vertex normals, water mask, and metadata extensions are intentionally skipped — only
/// what's needed to sample ground elevation is decoded.
/// </summary>
public sealed class QuantizedMeshTile
{
    public QuantizedMeshTile(
        double[] u,
        double[] v,
        double[] heightMeters,
        int[] triangleIndices,
        double minimumHeightMeters,
        double maximumHeightMeters)
    {
        U = u;
        V = v;
        HeightMeters = heightMeters;
        TriangleIndices = triangleIndices;
        MinimumHeightMeters = minimumHeightMeters;
        MaximumHeightMeters = maximumHeightMeters;
    }

    /// <summary>Normalized east-west position per vertex, 0 = west edge, 1 = east edge.</summary>
    public double[] U { get; }

    /// <summary>Normalized south-north position per vertex, 0 = south edge, 1 = north edge.</summary>
    public double[] V { get; }

    /// <summary>Absolute height per vertex, in metres (decoded against the tile's min/max height).</summary>
    public double[] HeightMeters { get; }

    /// <summary>Flat triangle list: indices.Length is a multiple of 3.</summary>
    public int[] TriangleIndices { get; }

    public double MinimumHeightMeters { get; }

    public double MaximumHeightMeters { get; }

    public int VertexCount => U.Length;

    public int TriangleCount => TriangleIndices.Length / 3;
}
