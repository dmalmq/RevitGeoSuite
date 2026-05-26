using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Precision;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Converts GSI sidewalk outer-edge linework into one-sided strip polygons.
/// The source line is treated as the road-facing edge; the strip is emitted on
/// the inferred block/interior side.
/// </summary>
public static class SidewalkStripBuilder
{
    private const string SidewalkLayer = "GSI_SIDEWALKS";
    private const double PrecisionGridMetres = 0.01d;
    private const double CoordinateEpsilon = 1e-9d;

    public static IReadOnlyList<KibanPolygonExportFeature> Build(
        IReadOnlyList<KibanLineExportFeature> sidewalkLines,
        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas,
        IReadOnlyCollection<string> selectedTileIds,
        CrsReference targetCrs,
        ICoordinateTransformer coordinateTransformer,
        SidewalkStripOptions options,
        ICollection<string>? warnings = null)
    {
        if (sidewalkLines is null) throw new ArgumentNullException(nameof(sidewalkLines));
        if (roadAreas is null) throw new ArgumentNullException(nameof(roadAreas));
        if (selectedTileIds is null) throw new ArgumentNullException(nameof(selectedTileIds));
        if (targetCrs is null) throw new ArgumentNullException(nameof(targetCrs));
        if (coordinateTransformer is null) throw new ArgumentNullException(nameof(coordinateTransformer));
        if (options is null) throw new ArgumentNullException(nameof(options));

        if (sidewalkLines.Count == 0 || options.WidthMetres <= 0d)
        {
            return Array.Empty<KibanPolygonExportFeature>();
        }

        PrecisionModel precisionModel = new PrecisionModel(1d / PrecisionGridMetres);
        GeometryFactory factory = new GeometryFactory(precisionModel);
        GeometryPrecisionReducer reducer = new GeometryPrecisionReducer(precisionModel) { Pointwise = true };

        List<Polygon> roadPolygons = BuildRoadPolygons(roadAreas, factory, reducer);
        STRtree<int> roadTree = BuildRoadTree(roadPolygons);
        IReadOnlyList<(string TileId, Polygon ClipBox)> clipBoxes = KibanGeometryConverter.BuildSelectedTileClipPolygons(
            selectedTileIds,
            targetCrs,
            coordinateTransformer);
        if (clipBoxes.Count == 0)
        {
            return Array.Empty<KibanPolygonExportFeature>();
        }

        List<KibanPolygonExportFeature> output = new List<KibanPolygonExportFeature>();
        int sourceIndex = 0;
        foreach (KibanLineExportFeature sidewalk in sidewalkLines)
        {
            sourceIndex++;
            if (!string.Equals(sidewalk.Layer, SidewalkLayer, StringComparison.Ordinal))
            {
                continue;
            }

            LineString? line = ToLineString(sidewalk.VerticesMetres, factory, reducer);
            if (line is null)
            {
                warnings?.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Skipped GSI sidewalk '{0}': line geometry could not be buffered.",
                    sidewalk.SourceId ?? "sidewalk-line"));
                continue;
            }

            if (!TryChooseBufferDistance(line, roadPolygons, roadTree, options, out double bufferDistance, out string sideReason))
            {
                warnings?.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Skipped GSI sidewalk '{0}': inside side could not be inferred from curvature or nearby roads.",
                    sidewalk.SourceId ?? "sidewalk-line"));
                continue;
            }

            Geometry strip;
            try
            {
                strip = BufferLine(line, bufferDistance, options);
            }
            catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
            {
                warnings?.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Skipped GSI sidewalk '{0}': one-sided buffer failed: {1}",
                    sidewalk.SourceId ?? "sidewalk-line",
                    ex.Message));
                continue;
            }

            if (strip.IsEmpty)
            {
                warnings?.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Skipped GSI sidewalk '{0}': one-sided buffer produced no polygon.",
                    sidewalk.SourceId ?? "sidewalk-line"));
                continue;
            }

            int partIndex = 0;
            string baseSourceId = string.IsNullOrWhiteSpace(sidewalk.SourceId)
                ? string.Format(CultureInfo.InvariantCulture, "sidewalk-strip:{0}", sourceIndex)
                : sidewalk.SourceId!;
            foreach ((string tileId, Polygon clipBox) in clipBoxes)
            {
                Geometry clipped;
                try
                {
                    clipped = strip.Intersection(clipBox);
                }
                catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
                {
                    warnings?.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "GSI sidewalk strip '{0}' could not be clipped to tile '{1}': {2}",
                        sidewalk.SourceId ?? "sidewalk-line",
                        tileId,
                        ex.Message));
                    continue;
                }

                if (clipped.IsEmpty)
                {
                    continue;
                }

                EmitPolygons(clipped, sidewalk, baseSourceId, tileId, sideReason, options.MinimumPolygonArea, ref partIndex, output);
            }
        }

        return output;
    }

    private static Geometry BufferLine(LineString line, double bufferDistance, SidewalkStripOptions options)
    {
        BufferParameters bufferParameters = new BufferParameters
        {
            IsSingleSided = true,
            JoinStyle = options.JoinStyle,
            EndCapStyle = options.EndCapStyle,
            MitreLimit = options.MitreLimit,
            QuadrantSegments = Math.Max(1, options.QuadrantSegments)
        };

        return BufferOp.Buffer(line, bufferDistance, bufferParameters);
    }

    private static bool TryChooseBufferDistance(
        LineString line,
        List<Polygon> roadPolygons,
        STRtree<int> roadTree,
        SidewalkStripOptions options,
        out double bufferDistance,
        out string reason)
    {
        double turn = ComputeSignedTurn(line);
        if (Math.Abs(turn) >= Math.Max(options.CurvatureTurnThresholdRadians, 0d))
        {
            bufferDistance = turn > 0d ? options.WidthMetres : -options.WidthMetres;
            reason = turn > 0d ? "curvature-left" : "curvature-right";
            return true;
        }

        if (TryChooseSideAwayFromRoad(line, roadPolygons, roadTree, options, out int side))
        {
            bufferDistance = side > 0 ? options.WidthMetres : -options.WidthMetres;
            reason = side > 0 ? "road-away-left" : "road-away-right";
            return true;
        }

        bufferDistance = 0d;
        reason = string.Empty;
        return false;
    }

    private static double ComputeSignedTurn(LineString line)
    {
        double turn = 0d;
        Coordinate[] coordinates = line.Coordinates;
        for (int i = 1; i < coordinates.Length - 1; i++)
        {
            double ax = coordinates[i].X - coordinates[i - 1].X;
            double ay = coordinates[i].Y - coordinates[i - 1].Y;
            double bx = coordinates[i + 1].X - coordinates[i].X;
            double by = coordinates[i + 1].Y - coordinates[i].Y;
            double aLen = Math.Sqrt((ax * ax) + (ay * ay));
            double bLen = Math.Sqrt((bx * bx) + (by * by));
            if (aLen <= CoordinateEpsilon || bLen <= CoordinateEpsilon)
            {
                continue;
            }

            double cross = (ax * by) - (ay * bx);
            double dot = (ax * bx) + (ay * by);
            turn += Math.Atan2(cross, dot);
        }

        return turn;
    }

    private static bool TryChooseSideAwayFromRoad(
        LineString line,
        List<Polygon> roadPolygons,
        STRtree<int> roadTree,
        SidewalkStripOptions options,
        out int side)
    {
        side = 0;
        if (roadPolygons.Count == 0 || options.RoadSearchDistance <= 0d)
        {
            return false;
        }

        double roadLeftScore = 0d;
        double roadRightScore = 0d;
        double probeDistance = Math.Min(options.RoadSearchDistance, Math.Max(1.0d, options.WidthMetres * 0.5d));
        for (int i = 0; i < line.NumPoints - 1; i++)
        {
            Coordinate start = line.GetCoordinateN(i);
            Coordinate end = line.GetCoordinateN(i + 1);
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length <= CoordinateEpsilon)
            {
                continue;
            }

            double midX = (start.X + end.X) * 0.5d;
            double midY = (start.Y + end.Y) * 0.5d;
            double leftNormalX = -dy / length;
            double leftNormalY = dx / length;
            Coordinate leftProbe = new Coordinate(midX + (leftNormalX * probeDistance), midY + (leftNormalY * probeDistance));
            Coordinate rightProbe = new Coordinate(midX - (leftNormalX * probeDistance), midY - (leftNormalY * probeDistance));
            Envelope searchEnvelope = new Envelope(start, end);
            searchEnvelope.ExpandBy(options.RoadSearchDistance + probeDistance);
            IList<int> candidates = roadTree.Query(searchEnvelope);
            if (candidates.Count == 0)
            {
                continue;
            }

            double leftDistance = DistanceToNearestRoad(leftProbe, candidates, roadPolygons, line.Factory);
            double rightDistance = DistanceToNearestRoad(rightProbe, candidates, roadPolygons, line.Factory);
            if (leftDistance <= options.RoadSearchDistance && rightDistance > options.RoadSearchDistance)
            {
                roadLeftScore += length;
            }
            else if (rightDistance <= options.RoadSearchDistance && leftDistance > options.RoadSearchDistance)
            {
                roadRightScore += length;
            }
            else if (leftDistance <= options.RoadSearchDistance && rightDistance <= options.RoadSearchDistance)
            {
                double distanceDelta = Math.Abs(leftDistance - rightDistance);
                if (distanceDelta > 0.25d)
                {
                    if (leftDistance < rightDistance)
                    {
                        roadLeftScore += length;
                    }
                    else
                    {
                        roadRightScore += length;
                    }
                }
            }
        }

        if (roadLeftScore <= CoordinateEpsilon && roadRightScore <= CoordinateEpsilon)
        {
            return false;
        }

        if (roadLeftScore > roadRightScore * 1.25d)
        {
            side = -1;
            return true;
        }

        if (roadRightScore > roadLeftScore * 1.25d)
        {
            side = 1;
            return true;
        }

        return false;
    }

    private static double DistanceToNearestRoad(
        Coordinate coordinate,
        IList<int> candidateIndices,
        List<Polygon> roadPolygons,
        GeometryFactory factory)
    {
        Point point = factory.CreatePoint(coordinate);
        double best = double.PositiveInfinity;
        foreach (int index in candidateIndices)
        {
            Polygon road = roadPolygons[index];
            double distance = road.Distance(point);
            if (distance < best)
            {
                best = distance;
            }
        }

        return best;
    }

    private static List<Polygon> BuildRoadPolygons(
        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas,
        GeometryFactory factory,
        GeometryPrecisionReducer reducer)
    {
        List<Polygon> roadPolygons = new List<Polygon>();
        foreach (PlateauContextOutlinesDxfWriter.AreaFeature road in roadAreas)
        {
            Polygon? roadPolygon = ToPolygon(road, factory, reducer);
            if (roadPolygon is not null && !roadPolygon.IsEmpty)
            {
                roadPolygons.Add(roadPolygon);
            }
        }

        return roadPolygons;
    }

    private static STRtree<int> BuildRoadTree(List<Polygon> roadPolygons)
    {
        STRtree<int> tree = new STRtree<int>();
        for (int i = 0; i < roadPolygons.Count; i++)
        {
            Polygon polygon = roadPolygons[i];
            if (!polygon.IsEmpty)
            {
                tree.Insert(polygon.EnvelopeInternal, i);
            }
        }

        if (roadPolygons.Count > 0)
        {
            tree.Build();
        }

        return tree;
    }

    private static LineString? ToLineString(
        IReadOnlyList<(double X, double Y)> vertices,
        GeometryFactory factory,
        GeometryPrecisionReducer reducer)
    {
        if (vertices.Count < 2)
        {
            return null;
        }

        List<Coordinate> coords = new List<Coordinate>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
        {
            Coordinate coord = new Coordinate(vertices[i].X, vertices[i].Y);
            if (coords.Count == 0 || !coords[coords.Count - 1].Equals2D(coord))
            {
                coords.Add(coord);
            }
        }

        if (coords.Count < 2)
        {
            return null;
        }

        try
        {
            LineString line = factory.CreateLineString(coords.ToArray());
            Geometry reduced = reducer.Reduce(line);
            return reduced is LineString reducedLine ? CleanLineString(reducedLine, factory) : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static LineString? CleanLineString(LineString line, GeometryFactory factory)
    {
        List<Coordinate> coords = new List<Coordinate>(line.NumPoints);
        foreach (Coordinate coord in line.Coordinates)
        {
            if (coords.Count == 0 || !coords[coords.Count - 1].Equals2D(coord))
            {
                coords.Add(new Coordinate(coord));
            }
        }

        if (coords.Count < 2)
        {
            return null;
        }

        try
        {
            return factory.CreateLineString(coords.ToArray());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static Polygon? ToPolygon(
        PlateauContextOutlinesDxfWriter.AreaFeature area,
        GeometryFactory factory,
        GeometryPrecisionReducer reducer)
    {
        LinearRing? shell = ToLinearRing(area.ExteriorRingMetres, factory);
        if (shell is null)
        {
            return null;
        }

        List<LinearRing> holes = new List<LinearRing>(area.InteriorRingsMetres.Count);
        foreach (IReadOnlyList<(double X, double Y)> interior in area.InteriorRingsMetres)
        {
            LinearRing? hole = ToLinearRing(interior, factory);
            if (hole is not null)
            {
                holes.Add(hole);
            }
        }

        try
        {
            Polygon polygon = factory.CreatePolygon(shell, holes.ToArray());
            Geometry reduced = reducer.Reduce(polygon);
            if (reduced is Polygon reducedPolygon && reducedPolygon.IsValid && !reducedPolygon.IsEmpty)
            {
                return reducedPolygon;
            }

            if (polygon.Buffer(0d) is Polygon buffered && !buffered.IsEmpty)
            {
                return buffered;
            }
        }
        catch (ArgumentException)
        {
        }

        return null;
    }

    private static LinearRing? ToLinearRing(IReadOnlyList<(double X, double Y)> vertices, GeometryFactory factory)
    {
        if (vertices.Count < 3)
        {
            return null;
        }

        List<Coordinate> coords = new List<Coordinate>(vertices.Count + 1);
        for (int i = 0; i < vertices.Count; i++)
        {
            Coordinate coord = new Coordinate(vertices[i].X, vertices[i].Y);
            if (coords.Count == 0 || !coords[coords.Count - 1].Equals2D(coord))
            {
                coords.Add(coord);
            }
        }

        if (coords.Count < 3)
        {
            return null;
        }

        if (!coords[0].Equals2D(coords[coords.Count - 1]))
        {
            coords.Add(new Coordinate(coords[0].X, coords[0].Y));
        }

        try
        {
            return factory.CreateLinearRing(coords.ToArray());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void EmitPolygons(
        Geometry geometry,
        KibanLineExportFeature sourceLine,
        string baseSourceId,
        string tileId,
        string sideReason,
        double minimumArea,
        ref int partIndex,
        List<KibanPolygonExportFeature> output)
    {
        if (geometry.IsEmpty)
        {
            return;
        }

        if (geometry is Polygon polygon)
        {
            if (polygon.ExteriorRing is null || polygon.Area <= minimumArea)
            {
                return;
            }

            List<(double X, double Y)> exterior = new List<(double X, double Y)>(polygon.ExteriorRing.NumPoints);
            foreach (Coordinate coord in polygon.ExteriorRing.Coordinates)
            {
                exterior.Add((coord.X, coord.Y));
            }

            List<IReadOnlyList<(double X, double Y)>> interiors = new List<IReadOnlyList<(double X, double Y)>>(polygon.NumInteriorRings);
            for (int i = 0; i < polygon.NumInteriorRings; i++)
            {
                LineString interiorRing = polygon.GetInteriorRingN(i);
                List<(double X, double Y)> interior = new List<(double X, double Y)>(interiorRing.NumPoints);
                foreach (Coordinate coord in interiorRing.Coordinates)
                {
                    interior.Add((coord.X, coord.Y));
                }

                if (interior.Count >= 3)
                {
                    interiors.Add(interior);
                }
            }

            partIndex++;
            string sourceId = string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1}:strip{2}:{3}",
                baseSourceId,
                tileId,
                partIndex,
                sideReason);

            output.Add(new KibanPolygonExportFeature(
                SidewalkLayer,
                exterior,
                interiors,
                sourceId,
                sourceLine.MeshCode,
                sourceLine.SourcePath,
                "sidewalk-strip",
                sourceLine.Visibility));
            return;
        }

        for (int i = 0; i < geometry.NumGeometries; i++)
        {
            EmitPolygons(geometry.GetGeometryN(i), sourceLine, baseSourceId, tileId, sideReason, minimumArea, ref partIndex, output);
        }
    }
}
