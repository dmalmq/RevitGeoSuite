using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.Tiles3DExport;

/// <summary>
/// Moves every vertex from plane-grid metres into the anchor's true east-north-up frame.
/// </summary>
/// <remarks>
/// Revit's shared coordinates are plane-grid metres: their north is grid north and their
/// distances carry the projection's scale. The tileset's transform is east-north-up at the anchor,
/// so writing grid metres into it as they are twists the model by the meridian convergence
/// (about 0.04 degrees at Tokyo Station, 0.2 m at 300 m) and stretches it by the scale (3 cm at
/// 300 m), and leaves out the earth's curvature (7 mm at 300 m).
/// <para>
/// This takes each vertex the whole way instead: grid easting and northing through the
/// projection's own inverse to latitude and longitude, its height above the ellipsoid, to ECEF,
/// and then into east-north-up at the anchor. That is exact, needs no convergence or scale
/// formula, and uses the same inverse the anchor itself came from. Heights stay as they are: the
/// geoid offset is the anchor's, so level elevations and z extents keep their meaning.
/// </para>
/// Runs after <see cref="Tiles3DPreciseCrsAnchorRebaser"/>, whose anchor is the geometry's centroid.
/// </remarks>
public sealed class Tiles3DExactPlacement
{
    private readonly ICoordinateTransformer coordinateTransformer;

    public Tiles3DExactPlacement(ICoordinateTransformer coordinateTransformer)
    {
        this.coordinateTransformer = coordinateTransformer ?? throw new ArgumentNullException(nameof(coordinateTransformer));
    }

    public void Place(Tiles3DExportPackage package)
    {
        if (package is null)
        {
            throw new ArgumentNullException(nameof(package));
        }

        Tiles3DExportReferenceContext context = package.ReferenceContext;
        var frame = new EastNorthUpFrame(
            context.AnchorLatitude,
            context.AnchorLongitude,
            context.AnchorElevationMeters);
        double originEasting = context.AnchorProjectedCoordinate.Easting;
        double originNorthing = context.AnchorProjectedCoordinate.Northing;

        // A vertex is shared by several triangles; place each distinct one once.
        var placed = new Dictionary<(double X, double Y, double Z), Tiles3DPoint>();
        Tiles3DPoint PlacePoint(Tiles3DPoint point)
        {
            var key = (point.X, point.Y, point.Z);
            if (!placed.TryGetValue(key, out Tiles3DPoint result))
            {
                GeographicCoordinate geographic = coordinateTransformer.Unproject(
                    new ProjectedCoordinate(originEasting + point.X, originNorthing + point.Y),
                    context.ProjectCrs);
                result = frame.ToLocal(geographic.Latitude, geographic.Longitude, context.AnchorElevationMeters + point.Z);
                placed[key] = result;
            }

            return result;
        }

        foreach (Tiles3DMeshPrimitive mesh in package.Meshes)
        {
            List<Tiles3DTriangle> triangles = mesh.Triangles;
            for (int i = 0; i < triangles.Count; i++)
            {
                Tiles3DTriangle t = triangles[i];
                triangles[i] = new Tiles3DTriangle(PlacePoint(t.A), PlacePoint(t.B), PlacePoint(t.C), t.MaterialColor);
            }
        }

        package.BoundingBox = BuildBoundingBox(package.Meshes);
        package.UsedExactPlacement = true;
    }

    private static double[] BuildBoundingBox(IReadOnlyList<Tiles3DMeshPrimitive> meshes)
    {
        IEnumerable<Tiles3DPoint> points = meshes.SelectMany(
            mesh => mesh.Triangles.SelectMany(t => new[] { t.A, t.B, t.C }));

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (Tiles3DPoint p in points)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            minZ = Math.Min(minZ, p.Z);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
            maxZ = Math.Max(maxZ, p.Z);
        }

        if (minX > maxX)
        {
            return new double[12];
        }

        return new[]
        {
            (minX + maxX) / 2d, (minY + maxY) / 2d, (minZ + maxZ) / 2d,
            Math.Max((maxX - minX) / 2d, 0.01d), 0d, 0d,
            0d, Math.Max((maxY - minY) / 2d, 0.01d), 0d,
            0d, 0d, Math.Max((maxZ - minZ) / 2d, 0.01d)
        };
    }

    /// <summary>The anchor's east-north-up frame on the WGS84 ellipsoid.</summary>
    internal readonly struct EastNorthUpFrame
    {
        private readonly double originX;
        private readonly double originY;
        private readonly double originZ;
        private readonly double sinLat;
        private readonly double cosLat;
        private readonly double sinLon;
        private readonly double cosLon;

        public EastNorthUpFrame(double latitudeDegrees, double longitudeDegrees, double ellipsoidalHeightMeters)
        {
            double latitude = GeospatialTransformBuilder.DegreesToRadians(latitudeDegrees);
            double longitude = GeospatialTransformBuilder.DegreesToRadians(longitudeDegrees);
            (originX, originY, originZ) = GeospatialTransformBuilder.ToEcef(latitude, longitude, ellipsoidalHeightMeters);
            sinLat = Math.Sin(latitude);
            cosLat = Math.Cos(latitude);
            sinLon = Math.Sin(longitude);
            cosLon = Math.Cos(longitude);
        }

        public Tiles3DPoint ToLocal(double latitudeDegrees, double longitudeDegrees, double ellipsoidalHeightMeters)
        {
            (double x, double y, double z) = GeospatialTransformBuilder.ToEcef(
                GeospatialTransformBuilder.DegreesToRadians(latitudeDegrees),
                GeospatialTransformBuilder.DegreesToRadians(longitudeDegrees),
                ellipsoidalHeightMeters);
            double dx = x - originX;
            double dy = y - originY;
            double dz = z - originZ;
            return new Tiles3DPoint(
                (-sinLon * dx) + (cosLon * dy),
                (-sinLat * cosLon * dx) - (sinLat * sinLon * dy) + (cosLat * dz),
                (cosLat * cosLon * dx) + (cosLat * sinLon * dy) + (sinLat * dz));
        }
    }
}
