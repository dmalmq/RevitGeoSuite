using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;

namespace RevitGeoSuite.PlateauImport;

public static class KibanGeometryConverter
{
    private const double DefaultSidewalkWidthMeters = 2.0;
    private const double DefaultRailwayWidthMeters = 3.0;
    private const double CoordinateEpsilon = 1e-12d;

    public static IReadOnlyList<KibanLineExportFeature> ConvertToLines(
        IReadOnlyList<KibanParsedFeature> kibanFeatures,
        IReadOnlyCollection<string> selectedTileIds,
        CrsReference targetCrs,
        ICoordinateTransformer coordinateTransformer,
        ICollection<string>? warnings = null)
    {
        if (kibanFeatures is null) throw new ArgumentNullException(nameof(kibanFeatures));
        if (selectedTileIds is null) throw new ArgumentNullException(nameof(selectedTileIds));
        if (targetCrs is null) throw new ArgumentNullException(nameof(targetCrs));
        if (coordinateTransformer is null) throw new ArgumentNullException(nameof(coordinateTransformer));

        IReadOnlyList<GeographicBounds> selectedBounds = BuildSelectedBounds(selectedTileIds);
        if (selectedBounds.Count == 0)
        {
            return Array.Empty<KibanLineExportFeature>();
        }

        List<KibanLineExportFeature> exportFeatures = new List<KibanLineExportFeature>();
        int featureIndex = 0;
        foreach (KibanParsedFeature kibanFeature in kibanFeatures)
        {
            featureIndex++;
            if (kibanFeature.Vertices.Count < 2)
            {
                continue;
            }

            GeographicBounds featureBounds = GetFeatureBounds(kibanFeature.Vertices);
            string baseSourceId = BuildSourceId(kibanFeature, featureIndex);
            int partIndex = 0;
            HashSet<string> emittedPartKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (GeographicBounds bounds in selectedBounds)
            {
                if (!bounds.Intersects(featureBounds))
                {
                    continue;
                }

                IReadOnlyList<IReadOnlyList<(double Latitude, double Longitude)>> clippedParts = ClipPolylineToBounds(kibanFeature.Vertices, bounds);
                foreach (IReadOnlyList<(double Latitude, double Longitude)> clippedPart in clippedParts)
                {
                    if (clippedPart.Count < 2)
                    {
                        continue;
                    }

                    string partKey = BuildPartKey(clippedPart);
                    if (!emittedPartKeys.Add(partKey))
                    {
                        continue;
                    }

                    IReadOnlyList<(double X, double Y)>? projected = ProjectVertices(clippedPart, targetCrs, coordinateTransformer);
                    if (projected is null || projected.Count < 2)
                    {
                        continue;
                    }

                    partIndex++;
                    string sourceId = partIndex == 1
                        ? baseSourceId
                        : string.Format(CultureInfo.InvariantCulture, "{0}:{1}:part{2}", baseSourceId, bounds.TileId, partIndex);

                    exportFeatures.Add(new KibanLineExportFeature(
                        kibanFeature.Layer,
                        projected,
                        sourceId,
                        kibanFeature.MeshCode,
                        kibanFeature.SourcePath,
                        kibanFeature.FeatureType,
                        kibanFeature.Visibility));
                }
            }
        }

        return exportFeatures;
    }

    public static IReadOnlyList<KibanPolygonExportFeature> ConvertToPolygons(
        IReadOnlyList<KibanParsedPolygonFeature> polygonFeatures,
        IReadOnlyCollection<string> selectedTileIds,
        CrsReference targetCrs,
        ICoordinateTransformer coordinateTransformer,
        ICollection<string>? warnings = null)
    {
        if (polygonFeatures is null) throw new ArgumentNullException(nameof(polygonFeatures));
        if (selectedTileIds is null) throw new ArgumentNullException(nameof(selectedTileIds));
        if (targetCrs is null) throw new ArgumentNullException(nameof(targetCrs));
        if (coordinateTransformer is null) throw new ArgumentNullException(nameof(coordinateTransformer));

        IReadOnlyList<GeographicBounds> selectedBounds = BuildSelectedBounds(selectedTileIds);
        if (selectedBounds.Count == 0)
        {
            return Array.Empty<KibanPolygonExportFeature>();
        }

        GeometryFactory geometryFactory = new GeometryFactory();
        List<KibanPolygonExportFeature> exportFeatures = new List<KibanPolygonExportFeature>();
        int featureIndex = 0;
        foreach (KibanParsedPolygonFeature kibanFeature in polygonFeatures)
        {
            featureIndex++;
            if (kibanFeature.ExteriorRings.Count == 0)
            {
                continue;
            }

            GeographicBounds featureBounds = GetPolygonFeatureBounds(kibanFeature);
            string baseSourceId = BuildPolygonSourceId(kibanFeature, featureIndex);
            int partIndex = 0;

            foreach (GeographicBounds bounds in selectedBounds)
            {
                if (!bounds.Intersects(featureBounds))
                {
                    continue;
                }

                Polygon? projectedPolygon = ProjectPolygon(
                    geometryFactory,
                    kibanFeature.ExteriorRings,
                    kibanFeature.InteriorRings,
                    targetCrs,
                    coordinateTransformer);
                if (projectedPolygon is null || projectedPolygon.IsEmpty)
                {
                    continue;
                }

                Polygon clipBox = CreateBoundsPolygon(geometryFactory, bounds, targetCrs, coordinateTransformer);
                if (clipBox is null || clipBox.IsEmpty)
                {
                    continue;
                }

                Geometry intersection;
                try
                {
                    intersection = projectedPolygon.Intersection(clipBox);
                }
                catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
                {
                    warnings?.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "GSI Kiban {0} polygon '{1}' could not be clipped to tile '{2}': {3}",
                        GetPolygonLayerLabel(kibanFeature.Layer),
                        baseSourceId,
                        bounds.TileId,
                        ex.Message));
                    continue;
                }

                AppendPolygonExportFeatures(
                    intersection,
                    kibanFeature,
                    bounds,
                    baseSourceId,
                    ref partIndex,
                    exportFeatures);
            }
        }

        return exportFeatures;
    }

    private static GeographicBounds GetPolygonFeatureBounds(KibanParsedPolygonFeature feature)
    {
        double south = double.PositiveInfinity;
        double west = double.PositiveInfinity;
        double north = double.NegativeInfinity;
        double east = double.NegativeInfinity;

        foreach (List<(double Latitude, double Longitude)> ring in feature.ExteriorRings)
        {
            foreach ((double latitude, double longitude) in ring)
            {
                south = Math.Min(south, latitude);
                west = Math.Min(west, longitude);
                north = Math.Max(north, latitude);
                east = Math.Max(east, longitude);
            }
        }

        return new GeographicBounds(string.Empty, south, west, north, east);
    }

    private static string BuildPolygonSourceId(KibanParsedPolygonFeature kibanFeature, int featureIndex)
    {
        if (!string.IsNullOrWhiteSpace(kibanFeature.Fid))
        {
            return kibanFeature.Fid;
        }

        if (!string.IsNullOrWhiteSpace(kibanFeature.SourceId))
        {
            return kibanFeature.SourceId;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1}:{2}",
            kibanFeature.MeshCode,
            kibanFeature.Layer,
            featureIndex);
    }

    private static string GetPolygonLayerLabel(string layer)
    {
        if (string.Equals(layer, KibanGmlParser.WaterLayer, StringComparison.Ordinal))
        {
            return "water";
        }

        if (string.Equals(layer, KibanGmlParser.LandUseLayer, StringComparison.Ordinal))
        {
            return "land-use";
        }

        return "polygon";
    }

    private static Polygon? ProjectPolygon(
        GeometryFactory geometryFactory,
        IReadOnlyList<List<(double Latitude, double Longitude)>> exteriorRings,
        IReadOnlyList<List<(double Latitude, double Longitude)>> interiorRings,
        CrsReference targetCrs,
        ICoordinateTransformer coordinateTransformer)
    {
        LinearRing? shell = ProjectRing(geometryFactory, exteriorRings[0], targetCrs, coordinateTransformer);
        if (shell is null)
        {
            return null;
        }

        List<LinearRing> holes = new List<LinearRing>(interiorRings.Count);
        foreach (List<(double Latitude, double Longitude)> interior in interiorRings)
        {
            LinearRing? hole = ProjectRing(geometryFactory, interior, targetCrs, coordinateTransformer);
            if (hole is not null)
            {
                holes.Add(hole);
            }
        }

        try
        {
            Polygon polygon = geometryFactory.CreatePolygon(shell, holes.ToArray());
            if (polygon.IsEmpty || polygon.Area <= CoordinateEpsilon)
            {
                return null;
            }

            return polygon.IsValid ? polygon : polygon.Buffer(0d) as Polygon;
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return null;
        }
    }

    private static LinearRing? ProjectRing(
        GeometryFactory geometryFactory,
        IReadOnlyList<(double Latitude, double Longitude)> ring,
        CrsReference targetCrs,
        ICoordinateTransformer coordinateTransformer)
    {
        if (ring.Count < 3)
        {
            return null;
        }

        List<Coordinate> coordinates = new List<Coordinate>(ring.Count + 1);
        foreach ((double latitude, double longitude) in ring)
        {
            ProjectedCoordinate projected;
            try
            {
                projected = coordinateTransformer.Project(new GeographicCoordinate(latitude, longitude), targetCrs);
            }
            catch (Exception)
            {
                return null;
            }

            if (!projected.IsFinite)
            {
                return null;
            }

            Coordinate coordinate = new Coordinate(projected.Easting, projected.Northing);
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
            return null;
        }

        coordinates.Add(new Coordinate(coordinates[0]));
        try
        {
            return geometryFactory.CreateLinearRing(coordinates.ToArray());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static Polygon CreateBoundsPolygon(
        GeometryFactory geometryFactory,
        GeographicBounds bounds,
        CrsReference targetCrs,
        ICoordinateTransformer coordinateTransformer)
    {
        const int samplesPerEdge = 12;
        List<(double Latitude, double Longitude)> ring = new List<(double Latitude, double Longitude)>((samplesPerEdge * 4) + 1);

        for (int i = 0; i < samplesPerEdge; i++)
        {
            double t = (double)i / samplesPerEdge;
            ring.Add((bounds.SouthLatitude, bounds.WestLongitude + (t * (bounds.EastLongitude - bounds.WestLongitude))));
        }

        for (int i = 0; i < samplesPerEdge; i++)
        {
            double t = (double)i / samplesPerEdge;
            ring.Add((bounds.SouthLatitude + (t * (bounds.NorthLatitude - bounds.SouthLatitude)), bounds.EastLongitude));
        }

        for (int i = 0; i < samplesPerEdge; i++)
        {
            double t = (double)i / samplesPerEdge;
            ring.Add((bounds.NorthLatitude, bounds.EastLongitude - (t * (bounds.EastLongitude - bounds.WestLongitude))));
        }

        for (int i = 0; i < samplesPerEdge; i++)
        {
            double t = (double)i / samplesPerEdge;
            ring.Add((bounds.NorthLatitude - (t * (bounds.NorthLatitude - bounds.SouthLatitude)), bounds.WestLongitude));
        }

        LinearRing? shell = ProjectRing(geometryFactory, ring, targetCrs, coordinateTransformer);
        if (shell is null)
        {
            return geometryFactory.CreatePolygon();
        }

        return geometryFactory.CreatePolygon(shell);
    }

    private static void AppendPolygonExportFeatures(
        Geometry geometry,
        KibanParsedPolygonFeature kibanFeature,
        GeographicBounds bounds,
        string baseSourceId,
        ref int partIndex,
        List<KibanPolygonExportFeature> exportFeatures)
    {
        if (geometry.IsEmpty)
        {
            return;
        }

        if (geometry is Polygon polygon)
        {
            if (polygon.Area <= CoordinateEpsilon || polygon.ExteriorRing is null)
            {
                return;
            }

            List<(double X, double Y)> exterior = new List<(double X, double Y)>(polygon.ExteriorRing.NumPoints);
            foreach (Coordinate coordinate in polygon.ExteriorRing.Coordinates)
            {
                exterior.Add((coordinate.X, coordinate.Y));
            }

            List<IReadOnlyList<(double X, double Y)>> interiors = new List<IReadOnlyList<(double X, double Y)>>(polygon.NumInteriorRings);
            for (int i = 0; i < polygon.NumInteriorRings; i++)
            {
                LineString interiorRing = polygon.GetInteriorRingN(i);
                List<(double X, double Y)> interior = new List<(double X, double Y)>(interiorRing.NumPoints);
                foreach (Coordinate coordinate in interiorRing.Coordinates)
                {
                    interior.Add((coordinate.X, coordinate.Y));
                }

                if (interior.Count >= 3)
                {
                    interiors.Add(interior);
                }
            }

            partIndex++;
            string sourceId = partIndex == 1
                ? baseSourceId
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1}:part{2}", baseSourceId, bounds.TileId, partIndex);

            exportFeatures.Add(new KibanPolygonExportFeature(
                kibanFeature.Layer,
                exterior,
                interiors,
                sourceId,
                kibanFeature.MeshCode,
                kibanFeature.SourcePath,
                kibanFeature.FeatureType,
                kibanFeature.Visibility));
            return;
        }

        for (int index = 0; index < geometry.NumGeometries; index++)
        {
            AppendPolygonExportFeatures(geometry.GetGeometryN(index), kibanFeature, bounds, baseSourceId, ref partIndex, exportFeatures);
        }
    }

    public static IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> ConvertToOutlines(
        IReadOnlyList<KibanParsedFeature> kibanFeatures,
        CrsReference targetCrs,
        ICoordinateTransformer coordinateTransformer)
    {
        if (kibanFeatures is null) throw new ArgumentNullException(nameof(kibanFeatures));
        if (targetCrs is null) throw new ArgumentNullException(nameof(targetCrs));
        if (coordinateTransformer is null) throw new ArgumentNullException(nameof(coordinateTransformer));

        List<PlateauContextOutlinesDxfWriter.OutlineFeature> outlineFeatures = new List<PlateauContextOutlinesDxfWriter.OutlineFeature>(kibanFeatures.Count);
        int featureIndex = 0;
        foreach (KibanParsedFeature kibanFeature in kibanFeatures)
        {
            featureIndex++;
            if (kibanFeature.Vertices.Count < 2)
            {
                continue;
            }

            double bufferWidthMeters = GetBufferWidth(kibanFeature.Layer);
            IReadOnlyList<(double X, double Y)>? footprint = ProjectAndBuffer(
                kibanFeature.Vertices,
                targetCrs,
                coordinateTransformer,
                bufferWidthMeters);

            if (footprint is null || footprint.Count < 3)
            {
                continue;
            }

            string sourceId = string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1}:{2}",
                kibanFeature.MeshCode,
                kibanFeature.Layer,
                featureIndex);

            outlineFeatures.Add(new PlateauContextOutlinesDxfWriter.OutlineFeature(
                kibanFeature.Layer,
                footprint,
                sourceId));
        }

        return outlineFeatures;
    }

    internal static IReadOnlyList<(string TileId, Polygon ClipBox)> BuildSelectedTileClipPolygons(
        IReadOnlyCollection<string> selectedTileIds,
        CrsReference targetCrs,
        ICoordinateTransformer coordinateTransformer)
    {
        if (selectedTileIds is null) throw new ArgumentNullException(nameof(selectedTileIds));
        if (targetCrs is null) throw new ArgumentNullException(nameof(targetCrs));
        if (coordinateTransformer is null) throw new ArgumentNullException(nameof(coordinateTransformer));

        GeometryFactory geometryFactory = new GeometryFactory();
        IReadOnlyList<GeographicBounds> selectedBounds = BuildSelectedBounds(selectedTileIds);
        List<(string, Polygon)> result = new List<(string, Polygon)>(selectedBounds.Count);
        foreach (GeographicBounds bounds in selectedBounds)
        {
            Polygon clipBox = CreateBoundsPolygon(geometryFactory, bounds, targetCrs, coordinateTransformer);
            if (clipBox is null || clipBox.IsEmpty)
            {
                continue;
            }

            result.Add((bounds.TileId, clipBox));
        }

        return result;
    }

    private static IReadOnlyList<GeographicBounds> BuildSelectedBounds(IReadOnlyCollection<string> selectedTileIds)
    {
        JapanMeshCalculator meshCalculator = new JapanMeshCalculator();
        List<GeographicBounds> selectedBounds = new List<GeographicBounds>();
        foreach (string tileId in selectedTileIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            string trimmed = tileId.Trim();
            if ((trimmed.Length != 6 && trimmed.Length != 8) || trimmed.Any(ch => !char.IsDigit(ch)))
            {
                continue;
            }

            MeshBounds meshBounds;
            try
            {
                meshBounds = meshCalculator.GetBounds(new MeshCode { Value = trimmed });
            }
            catch (ArgumentException)
            {
                continue;
            }

            selectedBounds.Add(new GeographicBounds(trimmed, meshBounds));
        }

        return selectedBounds;
    }

    private static GeographicBounds GetFeatureBounds(IReadOnlyList<(double Latitude, double Longitude)> vertices)
    {
        double south = double.PositiveInfinity;
        double west = double.PositiveInfinity;
        double north = double.NegativeInfinity;
        double east = double.NegativeInfinity;
        foreach ((double latitude, double longitude) in vertices)
        {
            south = Math.Min(south, latitude);
            west = Math.Min(west, longitude);
            north = Math.Max(north, latitude);
            east = Math.Max(east, longitude);
        }

        return new GeographicBounds(string.Empty, south, west, north, east);
    }

    private static IReadOnlyList<IReadOnlyList<(double Latitude, double Longitude)>> ClipPolylineToBounds(
        IReadOnlyList<(double Latitude, double Longitude)> vertices,
        GeographicBounds bounds)
    {
        List<IReadOnlyList<(double Latitude, double Longitude)>> parts = new List<IReadOnlyList<(double, double)>>();
        List<(double Latitude, double Longitude)>? currentPart = null;

        for (int index = 0; index < vertices.Count - 1; index++)
        {
            if (!TryClipSegment(vertices[index], vertices[index + 1], bounds, out (double Latitude, double Longitude) clippedStart, out (double Latitude, double Longitude) clippedEnd)
                || SamePoint(clippedStart, clippedEnd))
            {
                AddCurrentPart(parts, ref currentPart);
                continue;
            }

            if (currentPart is null || currentPart.Count == 0 || !SamePoint(currentPart[currentPart.Count - 1], clippedStart))
            {
                AddCurrentPart(parts, ref currentPart);
                currentPart = new List<(double Latitude, double Longitude)> { clippedStart };
            }

            currentPart.Add(clippedEnd);
        }

        AddCurrentPart(parts, ref currentPart);
        return parts;
    }

    private static void AddCurrentPart(
        ICollection<IReadOnlyList<(double Latitude, double Longitude)>> parts,
        ref List<(double Latitude, double Longitude)>? currentPart)
    {
        if (currentPart is not null && currentPart.Count >= 2)
        {
            parts.Add(currentPart);
        }

        currentPart = null;
    }

    private static bool TryClipSegment(
        (double Latitude, double Longitude) start,
        (double Latitude, double Longitude) end,
        GeographicBounds bounds,
        out (double Latitude, double Longitude) clippedStart,
        out (double Latitude, double Longitude) clippedEnd)
    {
        clippedStart = default;
        clippedEnd = default;

        double x0 = start.Longitude;
        double y0 = start.Latitude;
        double x1 = end.Longitude;
        double y1 = end.Latitude;
        double dx = x1 - x0;
        double dy = y1 - y0;
        double t0 = 0d;
        double t1 = 1d;

        if (!ClipBoundary(-dx, x0 - bounds.WestLongitude, ref t0, ref t1)
            || !ClipBoundary(dx, bounds.EastLongitude - x0, ref t0, ref t1)
            || !ClipBoundary(-dy, y0 - bounds.SouthLatitude, ref t0, ref t1)
            || !ClipBoundary(dy, bounds.NorthLatitude - y0, ref t0, ref t1))
        {
            return false;
        }

        clippedStart = (y0 + (t0 * dy), x0 + (t0 * dx));
        clippedEnd = (y0 + (t1 * dy), x0 + (t1 * dx));
        return true;
    }

    private static bool ClipBoundary(double p, double q, ref double t0, ref double t1)
    {
        if (Math.Abs(p) <= CoordinateEpsilon)
        {
            return q >= -CoordinateEpsilon;
        }

        double r = q / p;
        if (p < 0d)
        {
            if (r > t1)
            {
                return false;
            }

            if (r > t0)
            {
                t0 = r;
            }

            return true;
        }

        if (r < t0)
        {
            return false;
        }

        if (r < t1)
        {
            t1 = r;
        }

        return true;
    }

    private static IReadOnlyList<(double X, double Y)>? ProjectVertices(
        IReadOnlyList<(double Latitude, double Longitude)> vertices,
        CrsReference targetCrs,
        ICoordinateTransformer coordinateTransformer)
    {
        List<(double X, double Y)> projectedVertices = new List<(double, double)>(vertices.Count);
        foreach ((double latitude, double longitude) in vertices)
        {
            ProjectedCoordinate projected;
            try
            {
                projected = coordinateTransformer.Project(new GeographicCoordinate(latitude, longitude), targetCrs);
            }
            catch (Exception)
            {
                return null;
            }

            if (!projected.IsFinite)
            {
                return null;
            }

            (double X, double Y) projectedVertex = (projected.Easting, projected.Northing);
            if (projectedVertices.Count == 0
                || projectedVertices[projectedVertices.Count - 1].X != projectedVertex.X
                || projectedVertices[projectedVertices.Count - 1].Y != projectedVertex.Y)
            {
                projectedVertices.Add(projectedVertex);
            }
        }

        return projectedVertices.Count < 2 ? null : projectedVertices;
    }

    private static string BuildPartKey(IReadOnlyList<(double Latitude, double Longitude)> vertices)
    {
        (double Latitude, double Longitude) first = vertices[0];
        (double Latitude, double Longitude) last = vertices[vertices.Count - 1];
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1:0.#########},{2:0.#########}:{3:0.#########},{4:0.#########}",
            vertices.Count,
            first.Latitude,
            first.Longitude,
            last.Latitude,
            last.Longitude);
    }

    private static string BuildSourceId(KibanParsedFeature kibanFeature, int featureIndex)
    {
        if (!string.IsNullOrWhiteSpace(kibanFeature.Fid))
        {
            return kibanFeature.Fid;
        }

        if (!string.IsNullOrWhiteSpace(kibanFeature.SourceId))
        {
            return kibanFeature.SourceId;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1}:{2}",
            kibanFeature.MeshCode,
            kibanFeature.Layer,
            featureIndex);
    }

    private static bool SameCoordinate(Coordinate left, Coordinate right)
    {
        return left.X == right.X && left.Y == right.Y;
    }

    private static bool SamePoint((double Latitude, double Longitude) left, (double Latitude, double Longitude) right)
    {
        return Math.Abs(left.Latitude - right.Latitude) <= CoordinateEpsilon
            && Math.Abs(left.Longitude - right.Longitude) <= CoordinateEpsilon;
    }

    private readonly struct GeographicBounds
    {
        public GeographicBounds(string tileId, MeshBounds bounds)
            : this(tileId, bounds.SouthLatitude, bounds.WestLongitude, bounds.NorthLatitude, bounds.EastLongitude)
        {
        }

        public GeographicBounds(string tileId, double southLatitude, double westLongitude, double northLatitude, double eastLongitude)
        {
            TileId = tileId;
            SouthLatitude = southLatitude;
            WestLongitude = westLongitude;
            NorthLatitude = northLatitude;
            EastLongitude = eastLongitude;
        }

        public string TileId { get; }
        public double SouthLatitude { get; }
        public double WestLongitude { get; }
        public double NorthLatitude { get; }
        public double EastLongitude { get; }

        public bool Intersects(GeographicBounds other)
        {
            return WestLongitude <= other.EastLongitude
                && EastLongitude >= other.WestLongitude
                && SouthLatitude <= other.NorthLatitude
                && NorthLatitude >= other.SouthLatitude;
        }
    }

    private static double GetBufferWidth(string layer)
    {
        if (string.Equals(layer, "GSI_SIDEWALKS", StringComparison.Ordinal))
        {
            return DefaultSidewalkWidthMeters;
        }

        if (string.Equals(layer, "GSI_RAILWAYS", StringComparison.Ordinal))
        {
            return DefaultRailwayWidthMeters;
        }

        return DefaultSidewalkWidthMeters;
    }

    private static IReadOnlyList<(double X, double Y)>? ProjectAndBuffer(
        IReadOnlyList<(double Latitude, double Longitude)> vertices,
        CrsReference targetCrs,
        ICoordinateTransformer coordinateTransformer,
        double bufferWidthMeters)
    {
        List<Coordinate> projectedCoords = new List<Coordinate>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
        {
            GeographicCoordinate geographic = new GeographicCoordinate(vertices[i].Latitude, vertices[i].Longitude);
            ProjectedCoordinate projected = coordinateTransformer.Project(geographic, targetCrs);
            projectedCoords.Add(new Coordinate(projected.Easting, projected.Northing));
        }

        if (projectedCoords.Count < 2)
        {
            return null;
        }

        GeometryFactory geometryFactory = new GeometryFactory();
        LineString lineString;
        try
        {
            lineString = geometryFactory.CreateLineString(projectedCoords.ToArray());
        }
        catch (Exception)
        {
            return null;
        }

        if (lineString.IsEmpty || lineString.Length <= 0)
        {
            return null;
        }

        Geometry buffered;
        try
        {
            BufferParameters bufferParameters = new BufferParameters
            {
                EndCapStyle = EndCapStyle.Flat,
                JoinStyle = JoinStyle.Mitre,
                QuadrantSegments = 4
            };
            buffered = BufferOp.Buffer(lineString, bufferWidthMeters, bufferParameters);
        }
        catch (Exception)
        {
            return null;
        }

        if (buffered is null || buffered.IsEmpty)
        {
            return null;
        }

        Polygon? polygon = buffered as Polygon;
        if (polygon is null && buffered is GeometryCollection collection)
        {
            double maxArea = 0;
            for (int i = 0; i < collection.NumGeometries; i++)
            {
                Geometry child = collection.GetGeometryN(i);
                if (child is Polygon childPolygon && childPolygon.Area > maxArea)
                {
                    polygon = childPolygon;
                    maxArea = childPolygon.Area;
                }
            }
        }

        if (polygon is null || polygon.IsEmpty || polygon.ExteriorRing is null)
        {
            return null;
        }

        List<(double X, double Y)> ringVertices = new List<(double, double)>(polygon.ExteriorRing.NumPoints);
        foreach (Coordinate coordinate in polygon.ExteriorRing.Coordinates)
        {
            ringVertices.Add((coordinate.X, coordinate.Y));
        }

        return ringVertices;
    }
}
