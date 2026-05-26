using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;

namespace RevitGeoSuite.PlateauImport;

internal static class PlateauRoadOutlineCleaner
{
    internal const double DefaultSnapToleranceMeters = 0.01d;
    internal const double DefaultBridgingDistanceMeters = 0.10d;
    private const string RoadLayer = "PLATEAU_ROADS";

    public static IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> DissolveRoads(
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> features,
        ICollection<string> warnings,
        double snapToleranceMeters = DefaultSnapToleranceMeters,
        double bridgingDistanceMeters = DefaultBridgingDistanceMeters)
    {
        if (features is null)
        {
            throw new ArgumentNullException(nameof(features));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        if (snapToleranceMeters <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(snapToleranceMeters), snapToleranceMeters, "Snap tolerance must be positive.");
        }

        if (bridgingDistanceMeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(bridgingDistanceMeters), bridgingDistanceMeters, "Bridging distance must be non-negative.");
        }

        List<PlateauContextOutlinesDxfWriter.OutlineFeature> roadFeatures = new List<PlateauContextOutlinesDxfWriter.OutlineFeature>();
        foreach (PlateauContextOutlinesDxfWriter.OutlineFeature feature in features)
        {
            if (string.Equals(feature.Layer, RoadLayer, StringComparison.Ordinal))
            {
                roadFeatures.Add(feature);
            }
        }

        if (roadFeatures.Count == 0)
        {
            return Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>();
        }

        GeometryFactory geometryFactory = new GeometryFactory(new PrecisionModel(1d / snapToleranceMeters));
        List<Geometry> roadPolygons = new List<Geometry>(roadFeatures.Count);
        foreach (PlateauContextOutlinesDxfWriter.OutlineFeature road in roadFeatures)
        {
            Geometry? geometry = CreatePolygonGeometry(road, geometryFactory, snapToleranceMeters, warnings);
            if (geometry is null || geometry.IsEmpty)
            {
                continue;
            }

            AddPolygonalGeometries(geometry, roadPolygons);
        }

        if (roadPolygons.Count == 0)
        {
            warnings.Add("No valid road polygons were available for filled DXF export.");
            return Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>();
        }

        Geometry dissolved;
        try
        {
            dissolved = DissolveWithMorphologicalClosure(roadPolygons, geometryFactory, bridgingDistanceMeters);
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            warnings.Add($"Road polygon dissolve failed ({ex.Message}); exported filled road polygons without dissolving overlaps.");
            return CreateAreaFeatures(geometryFactory.CreateGeometryCollection(roadPolygons.ToArray()), "roads-undissolved");
        }

        return CreateAreaFeatures(dissolved, "roads-dissolved");
    }

    private static Geometry DissolveWithMorphologicalClosure(
        IReadOnlyList<Geometry> roadPolygons,
        GeometryFactory geometryFactory,
        double bridgingDistanceMeters)
    {
        if (bridgingDistanceMeters <= 0d)
        {
            return geometryFactory.CreateGeometryCollection(roadPolygons.ToArray()).Union();
        }

        BufferParameters bufferParameters = new BufferParameters
        {
            QuadrantSegments = 8,
            JoinStyle = JoinStyle.Mitre,
            MitreLimit = 10.0d,
            EndCapStyle = EndCapStyle.Square
        };

        Geometry[] buffered = new Geometry[roadPolygons.Count];
        for (int index = 0; index < roadPolygons.Count; index++)
        {
            buffered[index] = BufferOp.Buffer(roadPolygons[index], bridgingDistanceMeters, bufferParameters);
        }

        Geometry unioned = geometryFactory.CreateGeometryCollection(buffered).Union();
        Geometry shrunk = BufferOp.Buffer(unioned, -bridgingDistanceMeters, bufferParameters);

        // The inward buffer can collapse very thin geometry; fall back to the un-shrunk union in that case.
        return shrunk.IsEmpty ? unioned : shrunk;
    }

    public static IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> Clean(
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> features,
        ICollection<string> warnings,
        double snapToleranceMeters = DefaultSnapToleranceMeters,
        double bridgingDistanceMeters = DefaultBridgingDistanceMeters)
    {
        if (features is null)
        {
            throw new ArgumentNullException(nameof(features));
        }

        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = DissolveRoads(features, warnings, snapToleranceMeters, bridgingDistanceMeters);
        if (roadAreas.Count == 0)
        {
            return features;
        }

        List<PlateauContextOutlinesDxfWriter.OutlineFeature> result = new List<PlateauContextOutlinesDxfWriter.OutlineFeature>(
            features.Count + roadAreas.Count);
        bool insertedRoads = false;
        foreach (PlateauContextOutlinesDxfWriter.OutlineFeature feature in features)
        {
            if (!string.Equals(feature.Layer, RoadLayer, StringComparison.Ordinal))
            {
                result.Add(feature);
                continue;
            }

            if (insertedRoads)
            {
                continue;
            }

            foreach (PlateauContextOutlinesDxfWriter.AreaFeature roadArea in roadAreas)
            {
                result.Add(new PlateauContextOutlinesDxfWriter.OutlineFeature(
                    RoadLayer,
                    roadArea.ExteriorRingMetres,
                    roadArea.SourceId));
            }

            insertedRoads = true;
        }

        return result;
    }

    private static Geometry? CreatePolygonGeometry(
        PlateauContextOutlinesDxfWriter.OutlineFeature road,
        GeometryFactory geometryFactory,
        double snapToleranceMeters,
        ICollection<string> warnings)
    {
        if (road.VerticesMetres.Count < 3)
        {
            warnings.Add($"Skipped road fill for '{road.SourceId ?? "unknown"}' because it has fewer than three vertices.");
            return null;
        }

        Coordinate[] ring = BuildClosedRing(road.VerticesMetres, snapToleranceMeters);
        if (ring.Length < 4 || Math.Abs(ComputeSignedArea(ring)) <= (snapToleranceMeters * snapToleranceMeters))
        {
            warnings.Add($"Skipped road fill for '{road.SourceId ?? "unknown"}' because its outline collapsed after snapping.");
            return null;
        }

        Geometry geometry;
        try
        {
            geometry = geometryFactory.CreatePolygon(geometryFactory.CreateLinearRing(ring));
        }
        catch (ArgumentException ex)
        {
            warnings.Add($"Skipped road fill for '{road.SourceId ?? "unknown"}' because its outline is invalid ({ex.Message}).");
            return null;
        }

        if (!geometry.IsValid)
        {
            try
            {
                geometry = geometry.Buffer(0d);
            }
            catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
            {
                warnings.Add($"Skipped road fill for '{road.SourceId ?? "unknown"}' because its outline could not be repaired ({ex.Message}).");
                return null;
            }
        }

        if (geometry.IsEmpty || !geometry.IsValid || geometry.Area <= (snapToleranceMeters * snapToleranceMeters))
        {
            warnings.Add($"Skipped road fill for '{road.SourceId ?? "unknown"}' because its outline is empty or invalid after repair.");
            return null;
        }

        return geometry;
    }

    private static Coordinate[] BuildClosedRing(IReadOnlyList<(double X, double Y)> vertices, double snapToleranceMeters)
    {
        List<Coordinate> coordinates = new List<Coordinate>(vertices.Count + 1);
        foreach ((double x, double y) in vertices)
        {
            Coordinate coordinate = Snap(x, y, snapToleranceMeters);
            if (coordinates.Count == 0 || !SameCoordinate(coordinates[coordinates.Count - 1], coordinate))
            {
                coordinates.Add(coordinate);
            }
        }

        while (coordinates.Count > 1 && SameCoordinate(coordinates[0], coordinates[coordinates.Count - 1]))
        {
            coordinates.RemoveAt(coordinates.Count - 1);
        }

        if (coordinates.Count < 3)
        {
            return Array.Empty<Coordinate>();
        }

        coordinates.Add(new Coordinate(coordinates[0]));
        return coordinates.ToArray();
    }

    private static Coordinate Snap(double x, double y, double snapToleranceMeters)
    {
        return new Coordinate(
            Math.Round(x / snapToleranceMeters, MidpointRounding.AwayFromZero) * snapToleranceMeters,
            Math.Round(y / snapToleranceMeters, MidpointRounding.AwayFromZero) * snapToleranceMeters);
    }

    private static void AddPolygonalGeometries(Geometry geometry, ICollection<Geometry> polygons)
    {
        if (geometry is Polygon polygon)
        {
            polygons.Add(polygon);
            return;
        }

        for (int index = 0; index < geometry.NumGeometries; index++)
        {
            Geometry child = geometry.GetGeometryN(index);
            if (!child.IsEmpty)
            {
                AddPolygonalGeometries(child, polygons);
            }
        }
    }

    private static IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> CreateAreaFeatures(Geometry geometry, string sourceIdPrefix)
    {
        List<PlateauContextOutlinesDxfWriter.AreaFeature> result = new List<PlateauContextOutlinesDxfWriter.AreaFeature>();
        AddAreaFeatures(geometry, sourceIdPrefix, result);
        return result;
    }

    private static void AddAreaFeatures(
        Geometry geometry,
        string sourceIdPrefix,
        ICollection<PlateauContextOutlinesDxfWriter.AreaFeature> result)
    {
        if (geometry is Polygon polygon)
        {
            IReadOnlyList<(double X, double Y)> exterior = ToRing(polygon.ExteriorRing.Coordinates);
            if (exterior.Count < 3)
            {
                return;
            }

            List<IReadOnlyList<(double X, double Y)>> interiors = new List<IReadOnlyList<(double X, double Y)>>(polygon.NumInteriorRings);
            for (int index = 0; index < polygon.NumInteriorRings; index++)
            {
                IReadOnlyList<(double X, double Y)> interior = ToRing(polygon.GetInteriorRingN(index).Coordinates);
                if (interior.Count >= 3)
                {
                    interiors.Add(interior);
                }
            }

            result.Add(new PlateauContextOutlinesDxfWriter.AreaFeature(
                RoadLayer,
                exterior,
                interiors,
                string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}-{1}", sourceIdPrefix, result.Count + 1)));
            return;
        }

        for (int index = 0; index < geometry.NumGeometries; index++)
        {
            Geometry child = geometry.GetGeometryN(index);
            if (!child.IsEmpty)
            {
                AddAreaFeatures(child, sourceIdPrefix, result);
            }
        }
    }

    private static IReadOnlyList<(double X, double Y)> ToRing(IReadOnlyList<Coordinate> coordinates)
    {
        List<(double X, double Y)> ring = new List<(double X, double Y)>(coordinates.Count);
        foreach (Coordinate coordinate in coordinates)
        {
            (double X, double Y) point = (coordinate.X, coordinate.Y);
            if (ring.Count == 0 || !SamePoint(ring[ring.Count - 1], point))
            {
                ring.Add(point);
            }
        }

        while (ring.Count > 1 && SamePoint(ring[0], ring[ring.Count - 1]))
        {
            ring.RemoveAt(ring.Count - 1);
        }

        return ring.ToArray();
    }

    private static double ComputeSignedArea(IReadOnlyList<Coordinate> coordinates)
    {
        double areaTwice = 0d;
        for (int index = 0; index < coordinates.Count - 1; index++)
        {
            Coordinate current = coordinates[index];
            Coordinate next = coordinates[index + 1];
            areaTwice += (current.X * next.Y) - (next.X * current.Y);
        }

        return areaTwice / 2d;
    }

    private static bool SameCoordinate(Coordinate left, Coordinate right)
    {
        return left.X == right.X && left.Y == right.Y;
    }

    private static bool SamePoint((double X, double Y) left, (double X, double Y) right)
    {
        return left.X == right.X && left.Y == right.Y;
    }
}
