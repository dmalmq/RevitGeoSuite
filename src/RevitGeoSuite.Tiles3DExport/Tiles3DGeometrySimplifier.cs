using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DGeometrySimplifier
{
    public IReadOnlyCollection<Tiles3DMeshPrimitive> Simplify(IReadOnlyCollection<Tiles3DMeshPrimitive> meshes, Tiles3DLevelOfDetail levelOfDetail)
    {
        if (meshes is null)
        {
            throw new ArgumentNullException(nameof(meshes));
        }

        int stride = GetTriangleStride(levelOfDetail);
        if (stride <= 1)
        {
            return meshes.Select(ClonePrimitive).ToArray();
        }

        return meshes
            .Select(mesh => Simplify(mesh, stride))
            .Where(mesh => mesh.Triangles.Count > 0)
            .ToArray();
    }

    public static int GetTriangleStride(Tiles3DLevelOfDetail levelOfDetail)
    {
        return levelOfDetail switch
        {
            Tiles3DLevelOfDetail.Coarse => 4,
            Tiles3DLevelOfDetail.Medium => 2,
            _ => 1
        };
    }

    private static Tiles3DMeshPrimitive Simplify(Tiles3DMeshPrimitive source, int stride)
    {
        if (source.Triangles.Count <= 16)
        {
            return ClonePrimitive(source);
        }

        List<Tiles3DTriangle> retained = source.Triangles
            .Where((triangle, index) => index % stride == 0)
            .ToList();
        if (retained.Count == 0 && source.Triangles.Count > 0)
        {
            retained.Add(source.Triangles[0]);
        }

        return new Tiles3DMeshPrimitive
        {
            Metadata = source.Metadata.Clone(),
            Triangles = retained
        };
    }

    private static Tiles3DMeshPrimitive ClonePrimitive(Tiles3DMeshPrimitive source)
    {
        return new Tiles3DMeshPrimitive
        {
            Metadata = source.Metadata.Clone(),
            Triangles = source.Triangles.ToList()
        };
    }
}
