using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DPreciseCrsAnchorRebaser
{
    private readonly ICoordinateTransformer coordinateTransformer;

    public Tiles3DPreciseCrsAnchorRebaser(ICoordinateTransformer coordinateTransformer)
    {
        this.coordinateTransformer = coordinateTransformer ?? throw new ArgumentNullException(nameof(coordinateTransformer));
    }

    public void Rebase(Tiles3DExportPackage package)
    {
        if (package is null)
        {
            throw new ArgumentNullException(nameof(package));
        }

        // BoundingBox layout: [cx, cy, cz, hx, 0, 0, 0, hy, 0, 0, 0, hz]
        // cx/cy/cz are the ENU-space offsets of the geometry centroid from the current anchor.
        double cx = package.BoundingBox[0];
        double cy = package.BoundingBox[1];
        double cz = package.BoundingBox[2];

        // Compute the centroid's true CRS projected coordinates.
        // Local EW/NS meters correspond directly to CRS easting/northing differences because
        // Revit's shared coordinate system is aligned to the project CRS.
        ProjectedCoordinate centroidProjected = new ProjectedCoordinate(
            package.ReferenceContext.AnchorProjectedCoordinate.Easting + cx,
            package.ReferenceContext.AnchorProjectedCoordinate.Northing + cy);

        // Unproject through the real Transverse Mercator inverse to get true WGS84 lat/lon.
        GeographicCoordinate centroidGeo = coordinateTransformer.Unproject(
            centroidProjected,
            package.ReferenceContext.ProjectCrs);

        // Shift every vertex so geometry is expressed relative to the building centroid.
        foreach (Tiles3DMeshPrimitive mesh in package.Meshes)
        {
            List<Tiles3DTriangle> triangles = mesh.Triangles;
            for (int i = 0; i < triangles.Count; i++)
            {
                Tiles3DTriangle t = triangles[i];
                triangles[i] = new Tiles3DTriangle(
                    new Tiles3DPoint(t.A.X - cx, t.A.Y - cy, t.A.Z - cz),
                    new Tiles3DPoint(t.B.X - cx, t.B.Y - cy, t.B.Z - cz),
                    new Tiles3DPoint(t.C.X - cx, t.C.Y - cy, t.C.Z - cz),
                    t.MaterialColor);
            }

            // Only adjust the floor elevation for elements that have a real level assigned.
            // Unassigned elements carry LevelElevationMeters = 0 (a default, not a real elevation)
            // and shifting it would produce a meaningless negative value.
            if (!string.Equals(mesh.Metadata.LevelKey, Tiles3DLevelMetadata.UnassignedLevelKey, StringComparison.Ordinal))
            {
                mesh.Metadata.LevelElevationMeters -= cz;
            }

            mesh.Metadata.MinZMeters -= cz;
            mesh.Metadata.MaxZMeters -= cz;
        }

        // Move the ENU frame origin to the building centroid.
        package.ReferenceContext.AnchorLatitude = centroidGeo.Latitude;
        package.ReferenceContext.AnchorLongitude = centroidGeo.Longitude;
        package.ReferenceContext.AnchorElevationMeters += cz;
        package.ReferenceContext.AnchorProjectedCoordinate = centroidProjected;

        // Rebuild bounding box — centroid is now (0, 0, 0) so only half-extents remain.
        package.BoundingBox = BuildBoundingBox(package.Meshes);
        package.UsedPreciseCrsProjection = true;
    }

    private static double[] BuildBoundingBox(IReadOnlyList<Tiles3DMeshPrimitive> meshes)
    {
        IEnumerable<Tiles3DPoint> points = meshes.SelectMany(
            mesh => mesh.Triangles.SelectMany(t => new[] { t.A, t.B, t.C }));

        double minX = points.Min(p => p.X);
        double minY = points.Min(p => p.Y);
        double minZ = points.Min(p => p.Z);
        double maxX = points.Max(p => p.X);
        double maxY = points.Max(p => p.Y);
        double maxZ = points.Max(p => p.Z);

        return new[]
        {
            (minX + maxX) / 2d, (minY + maxY) / 2d, (minZ + maxZ) / 2d,
            Math.Max((maxX - minX) / 2d, 0.01d), 0d, 0d,
            0d, Math.Max((maxY - minY) / 2d, 0.01d), 0d,
            0d, 0d, Math.Max((maxZ - minZ) / 2d, 0.01d)
        };
    }
}
