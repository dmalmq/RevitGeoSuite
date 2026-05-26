using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate.Polygon;
using RevitGeoSuite.Core.Plateau.Tiles3D;

namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Writes a DXF containing closed outline polylines and optional filled road areas,
/// on layers chosen by the caller, plus a survey-control marker at the project base
/// point. The default fill mode stays in AutoCAD R12 (AC1009) using SOLID triangles;
/// callers can opt into AutoCAD 2000 (AC1015) HATCH fills for more editable modern
/// CAD output. Input and emitted coordinates are shared/projected metres.
/// </summary>
public static class PlateauContextOutlinesDxfWriter
{
    private const string DefaultLayer = "0";
    private const string MarkerLayer = "PROJECT_BASE_POINT";
    private const string Continuous = "CONTINUOUS";
    private const string FloatFormat = "0.0##############";
    private const double AreaEpsilon = 1e-9d;

    private const double MarkerRingRadiusMeters = 1.0;
    private const double MarkerCrossHalfLengthMeters = 1.5;

    public static IReadOnlyDictionary<PlateauFeatureType, string> LayerByFeatureType { get; } = new Dictionary<PlateauFeatureType, string>
    {
        { PlateauFeatureType.Building, "PLATEAU_BUILDINGS" },
        { PlateauFeatureType.Bridge, "PLATEAU_BRIDGES" },
        { PlateauFeatureType.Road, "PLATEAU_ROADS" },
        { PlateauFeatureType.Vegetation, "PLATEAU_VEGETATION" },
        { PlateauFeatureType.Relief, "PLATEAU_RELIEF" },
        { PlateauFeatureType.LandUse, "PLATEAU_LANDUSE" },
    };

    public const string PlateauLandUseLayer = "PLATEAU_LANDUSE";

    public const string GsiSidewalksLayer = "GSI_SIDEWALKS";
    public const string GsiRailwaysLayer = "GSI_RAILWAYS";

    private static readonly IReadOnlyDictionary<string, int> LayerColors = new Dictionary<string, int>
    {
        { "PLATEAU_BUILDINGS", 3 },
        { "PLATEAU_BRIDGES", 4 },
        { "PLATEAU_ROADS", 6 },
        { "PLATEAU_VEGETATION", 2 },
        { "PLATEAU_RELIEF", 8 },
        { GsiSidewalksLayer, 30 },
        { GsiRailwaysLayer, 5 },
    };

    public sealed class OutlineFeature
    {
        public OutlineFeature(
            string layer,
            IReadOnlyList<(double X, double Y)> verticesMetres,
            string? sourceId = null,
            string? classCode = null,
            string? className = null)
        {
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
            VerticesMetres = verticesMetres ?? throw new ArgumentNullException(nameof(verticesMetres));
            SourceId = sourceId;
            ClassCode = classCode;
            ClassName = className;
        }

        public string Layer { get; }
        public IReadOnlyList<(double X, double Y)> VerticesMetres { get; }
        public string? SourceId { get; }
        public string? ClassCode { get; }
        public string? ClassName { get; }
    }

    public sealed class AreaFeature
    {
        public AreaFeature(
            string layer,
            IReadOnlyList<(double X, double Y)> exteriorRingMetres,
            IReadOnlyList<IReadOnlyList<(double X, double Y)>>? interiorRingsMetres = null,
            string? sourceId = null)
        {
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
            ExteriorRingMetres = exteriorRingMetres ?? throw new ArgumentNullException(nameof(exteriorRingMetres));
            InteriorRingsMetres = interiorRingsMetres ?? Array.Empty<IReadOnlyList<(double X, double Y)>>();
            SourceId = sourceId;
        }

        public string Layer { get; }

        public IReadOnlyList<(double X, double Y)> ExteriorRingMetres { get; }

        public IReadOnlyList<IReadOnlyList<(double X, double Y)>> InteriorRingsMetres { get; }

        public string? SourceId { get; }
    }

    public sealed class WriteResult
    {
        public WriteResult(int featureCount, int polylineCount, int fillCount, int solidCount, IReadOnlyList<string> warnings)
            : this(featureCount, polylineCount, fillCount, solidCount, 0, warnings)
        {
        }

        public WriteResult(int featureCount, int polylineCount, int fillCount, int solidCount, int hatchCount, IReadOnlyList<string> warnings)
        {
            FeatureCount = featureCount;
            PolylineCount = polylineCount;
            FillCount = fillCount;
            SolidCount = solidCount;
            HatchCount = hatchCount;
            Warnings = warnings;
        }

        public int FeatureCount { get; }
        public int PolylineCount { get; }
        public int FillCount { get; }
        public int SolidCount { get; }
        public int HatchCount { get; }
        public IReadOnlyList<string> Warnings { get; }
    }

    /// <summary>
    /// Writes the DXF with vertex and marker coordinates shifted so that the supplied
    /// <paramref name="originOffsetMetres"/> shared/projected position lands at DXF
    /// (0,0,0). Use the Revit Survey Point's shared/projected metre position as the
    /// offset to follow the Civil 3D shared-coordinates convention.
    /// </summary>
    public static WriteResult Write(
        TextWriter writer,
        IReadOnlyCollection<OutlineFeature> features,
        Vector3d markerMetres,
        Vector3d originOffsetMetres)
    {
        return Write(writer, features, Array.Empty<AreaFeature>(), markerMetres, originOffsetMetres);
    }

    /// <summary>
    /// Writes outline polylines plus filled area SOLID triangles. Fills are emitted after
    /// polylines so filled roads visually cover their dissolved footprint while other
    /// context outlines remain inspectable on their own layers.
    /// </summary>
    public static WriteResult Write(
        TextWriter writer,
        IReadOnlyCollection<OutlineFeature> features,
        IReadOnlyCollection<AreaFeature> areaFeatures,
        Vector3d markerMetres,
        Vector3d originOffsetMetres)
    {
        return Write(
            writer,
            features,
            areaFeatures,
            markerMetres,
            originOffsetMetres,
            PlateauDxfRoadFillMode.R12SolidTriangles);
    }

    /// <summary>
    /// Writes outline polylines plus filled areas using the requested road fill mode.
    /// R12 SOLID triangles are the most broadly compatible. Modern HATCH emits one
    /// hatch per valid dissolved road polygon and requires an AC1015 DXF.
    /// </summary>
    public static WriteResult Write(
        TextWriter writer,
        IReadOnlyCollection<OutlineFeature> features,
        IReadOnlyCollection<AreaFeature> areaFeatures,
        Vector3d markerMetres,
        Vector3d originOffsetMetres,
        PlateauDxfRoadFillMode roadFillMode)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        if (features is null) throw new ArgumentNullException(nameof(features));
        if (areaFeatures is null) throw new ArgumentNullException(nameof(areaFeatures));
        if (!Enum.IsDefined(typeof(PlateauDxfRoadFillMode), roadFillMode))
        {
            throw new ArgumentOutOfRangeException(nameof(roadFillMode), roadFillMode, "Unsupported road fill mode.");
        }

        List<string> warnings = new List<string>();
        HashSet<string> usedLayers = new HashSet<string>(StringComparer.Ordinal);
        foreach (OutlineFeature feature in features)
        {
            if (feature.VerticesMetres.Count >= 3)
            {
                usedLayers.Add(feature.Layer);
            }
        }

        foreach (AreaFeature areaFeature in areaFeatures)
        {
            if (areaFeature.ExteriorRingMetres.Count >= 3)
            {
                usedLayers.Add(areaFeature.Layer);
            }
        }

        bool requiresModernDxf = roadFillMode == PlateauDxfRoadFillMode.ModernHatch && areaFeatures.Count > 0;
        WriteHeaderSection(writer, requiresModernDxf);
        WriteTablesSection(writer, usedLayers);
        WriteEntitiesStart(writer);

        int polylines = 0;
        foreach (OutlineFeature feature in features)
        {
            if (feature.VerticesMetres.Count < 3)
            {
                warnings.Add($"Skipped {feature.SourceId ?? "feature"}: outline collapsed to <3 points.");
                continue;
            }

            List<(double X, double Y)> shifted = new List<(double X, double Y)>(feature.VerticesMetres.Count);
            foreach ((double x, double y) in feature.VerticesMetres)
            {
                shifted.Add((x - originOffsetMetres.X, y - originOffsetMetres.Y));
            }

            WriteClosedPolyline(writer, shifted, feature.Layer);
            polylines++;
        }

        int fills = 0;
        int solids = 0;
        int hatches = 0;
        foreach (AreaFeature areaFeature in areaFeatures)
        {
            if (areaFeature.ExteriorRingMetres.Count < 3)
            {
                warnings.Add($"Skipped {areaFeature.SourceId ?? "area"}: fill boundary collapsed to <3 points.");
                continue;
            }

            List<(double X, double Y)> shiftedExterior = Shift(areaFeature.ExteriorRingMetres, originOffsetMetres);
            List<IReadOnlyList<(double X, double Y)>> shiftedInteriors = new List<IReadOnlyList<(double X, double Y)>>(areaFeature.InteriorRingsMetres.Count);
            foreach (IReadOnlyList<(double X, double Y)> interior in areaFeature.InteriorRingsMetres)
            {
                if (interior.Count < 3)
                {
                    continue;
                }

                shiftedInteriors.Add(Shift(interior, originOffsetMetres));
            }

            int writtenFills;
            switch (roadFillMode)
            {
                case PlateauDxfRoadFillMode.R12SolidTriangles:
                    writtenFills = WriteSolidTriangles(writer, shiftedExterior, shiftedInteriors, areaFeature.Layer);
                    solids += writtenFills;
                    break;

                case PlateauDxfRoadFillMode.ModernHatch:
                    writtenFills = WriteSolidHatches(writer, shiftedExterior, shiftedInteriors, areaFeature.Layer);
                    hatches += writtenFills;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(roadFillMode), roadFillMode, "Unsupported road fill mode.");
            }

            if (writtenFills == 0)
            {
                warnings.Add($"Skipped {areaFeature.SourceId ?? "area"}: fill boundary could not be written.");
                continue;
            }

            fills++;
        }

        Vector3d markerInDxfFrame = new Vector3d(
            markerMetres.X - originOffsetMetres.X,
            markerMetres.Y - originOffsetMetres.Y,
            markerMetres.Z - originOffsetMetres.Z);
        WriteMarker(writer, markerInDxfFrame);
        WriteSectionEnd(writer);
        WriteEof(writer);

        return new WriteResult(features.Count + areaFeatures.Count, polylines, fills, solids, hatches, warnings);
    }

    private static List<(double X, double Y)> Shift(IReadOnlyList<(double X, double Y)> vertices, Vector3d originOffsetMetres)
    {
        List<(double X, double Y)> shifted = new List<(double X, double Y)>(vertices.Count);
        foreach ((double x, double y) in vertices)
        {
            shifted.Add((x - originOffsetMetres.X, y - originOffsetMetres.Y));
        }

        return shifted;
    }

    private static void WriteHeaderSection(TextWriter w, bool modernDxf)
    {
        WritePair(w, 0, "SECTION");
        WritePair(w, 2, "HEADER");
        WritePair(w, 9, "$ACADVER");
        WritePair(w, 1, modernDxf ? "AC1015" : "AC1009");
        WriteSectionEnd(w);
    }

    private static void WriteTablesSection(TextWriter w, ICollection<string> usedLayers)
    {
        WritePair(w, 0, "SECTION");
        WritePair(w, 2, "TABLES");
        WritePair(w, 0, "TABLE");
        WritePair(w, 2, "LAYER");
        // Layer count = default '0' + marker layer + every layer referenced by at least one polyline.
        WritePair(w, 70, 2 + usedLayers.Count);
        WriteLayer(w, DefaultLayer, colorIndex: 7);
        foreach (string layer in usedLayers)
        {
            int color = LayerColors.TryGetValue(layer, out int c) ? c : 7;
            WriteLayer(w, layer, color);
        }
        WriteLayer(w, MarkerLayer, colorIndex: 1);
        WritePair(w, 0, "ENDTAB");
        WriteSectionEnd(w);
    }

    private static void WriteLayer(TextWriter w, string name, int colorIndex)
    {
        WritePair(w, 0, "LAYER");
        WritePair(w, 2, name);
        WritePair(w, 70, 0);
        WritePair(w, 62, colorIndex);
        WritePair(w, 6, Continuous);
    }

    private static void WriteEntitiesStart(TextWriter w)
    {
        WritePair(w, 0, "SECTION");
        WritePair(w, 2, "ENTITIES");
    }

    private static void WriteSectionEnd(TextWriter w)
    {
        WritePair(w, 0, "ENDSEC");
    }

    private static void WriteEof(TextWriter w)
    {
        WritePair(w, 0, "EOF");
    }

    private static void WriteClosedPolyline(TextWriter w, IReadOnlyList<(double X, double Y)> vertices, string layer)
    {
        WritePair(w, 0, "POLYLINE");
        WritePair(w, 8, layer);
        WritePair(w, 66, 1);
        WritePair(w, 10, 0.0);
        WritePair(w, 20, 0.0);
        WritePair(w, 30, 0.0);
        WritePair(w, 70, 1);
        for (int i = 0; i < vertices.Count; i++)
        {
            WritePair(w, 0, "VERTEX");
            WritePair(w, 8, layer);
            WritePair(w, 10, vertices[i].X);
            WritePair(w, 20, vertices[i].Y);
            WritePair(w, 30, 0.0);
        }
        WritePair(w, 0, "SEQEND");
        WritePair(w, 8, layer);
    }

    private static int WriteSolidTriangles(
        TextWriter w,
        IReadOnlyList<(double X, double Y)> exterior,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> interiors,
        string layer)
    {
        Geometry? polygon = CreatePolygonGeometry(exterior, interiors);
        if (polygon is null || polygon.IsEmpty)
        {
            return 0;
        }

        Geometry triangles;
        try
        {
            triangles = PolygonTriangulator.Triangulate(polygon);
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return 0;
        }

        return WriteTriangleGeometry(w, layer, triangles);
    }

    private static int WriteSolidHatches(
        TextWriter w,
        IReadOnlyList<(double X, double Y)> exterior,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> interiors,
        string layer)
    {
        Geometry? polygon = CreatePolygonGeometry(exterior, interiors);
        if (polygon is null || polygon.IsEmpty)
        {
            return 0;
        }

        return WriteHatchGeometry(w, layer, polygon);
    }

    private static Geometry? CreatePolygonGeometry(
        IReadOnlyList<(double X, double Y)> exterior,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> interiors)
    {
        GeometryFactory geometryFactory = new GeometryFactory();
        LinearRing? shell = CreateLinearRing(geometryFactory, exterior);
        if (shell is null)
        {
            return null;
        }

        List<LinearRing> holes = new List<LinearRing>(interiors.Count);
        foreach (IReadOnlyList<(double X, double Y)> interior in interiors)
        {
            LinearRing? hole = CreateLinearRing(geometryFactory, interior);
            if (hole is not null)
            {
                holes.Add(hole);
            }
        }

        try
        {
            Polygon polygon = geometryFactory.CreatePolygon(shell, holes.ToArray());
            if (polygon.IsEmpty || polygon.Area <= AreaEpsilon)
            {
                return null;
            }

            return polygon.IsValid ? polygon : polygon.Buffer(0d);
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return null;
        }
    }

    private static LinearRing? CreateLinearRing(GeometryFactory geometryFactory, IReadOnlyList<(double X, double Y)> ring)
    {
        if (ring.Count < 3)
        {
            return null;
        }

        List<Coordinate> coordinates = new List<Coordinate>(ring.Count + 1);
        foreach ((double x, double y) in ring)
        {
            Coordinate coordinate = new Coordinate(x, y);
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
        return geometryFactory.CreateLinearRing(coordinates.ToArray());
    }

    private static int WriteTriangleGeometry(TextWriter w, string layer, Geometry geometry)
    {
        if (geometry is Polygon polygon)
        {
            return TryGetTriangleVertices(
                polygon,
                out (double X, double Y) a,
                out (double X, double Y) b,
                out (double X, double Y) c)
                ? WriteSolidTriangle(w, layer, a, b, c)
                : 0;
        }

        int solidCount = 0;
        for (int index = 0; index < geometry.NumGeometries; index++)
        {
            Geometry child = geometry.GetGeometryN(index);
            if (!child.IsEmpty)
            {
                solidCount += WriteTriangleGeometry(w, layer, child);
            }
        }

        return solidCount;
    }

    private static int WriteHatchGeometry(TextWriter w, string layer, Geometry geometry)
    {
        if (geometry is Polygon polygon)
        {
            if (polygon.Area <= AreaEpsilon || polygon.IsEmpty)
            {
                return 0;
            }

            List<(double X, double Y)> exterior = ToDistinctRingPoints(polygon.ExteriorRing.Coordinates);
            if (exterior.Count < 3)
            {
                return 0;
            }

            List<IReadOnlyList<(double X, double Y)>> interiors = new List<IReadOnlyList<(double X, double Y)>>(polygon.NumInteriorRings);
            for (int index = 0; index < polygon.NumInteriorRings; index++)
            {
                List<(double X, double Y)> interior = ToDistinctRingPoints(polygon.GetInteriorRingN(index).Coordinates);
                if (interior.Count >= 3)
                {
                    interiors.Add(interior);
                }
            }

            Coordinate seed = polygon.InteriorPoint.Coordinate;
            WriteSolidHatch(w, layer, exterior, interiors, (seed.X, seed.Y));
            return 1;
        }

        int hatchCount = 0;
        for (int index = 0; index < geometry.NumGeometries; index++)
        {
            Geometry child = geometry.GetGeometryN(index);
            if (!child.IsEmpty)
            {
                hatchCount += WriteHatchGeometry(w, layer, child);
            }
        }

        return hatchCount;
    }

    private static List<(double X, double Y)> ToDistinctRingPoints(IReadOnlyList<Coordinate> coordinates)
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

        return ring;
    }

    private static void WriteSolidHatch(
        TextWriter w,
        string layer,
        IReadOnlyList<(double X, double Y)> exterior,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> interiors,
        (double X, double Y) seed)
    {
        WritePair(w, 0, "HATCH");
        WritePair(w, 100, "AcDbEntity");
        WritePair(w, 8, layer);
        WritePair(w, 100, "AcDbHatch");
        WritePair(w, 10, 0.0);
        WritePair(w, 20, 0.0);
        WritePair(w, 30, 0.0);
        WritePair(w, 210, 0.0);
        WritePair(w, 220, 0.0);
        WritePair(w, 230, 1.0);
        WritePair(w, 2, "SOLID");
        WritePair(w, 70, 1);
        WritePair(w, 71, 0);
        WritePair(w, 91, 1 + interiors.Count);
        WriteHatchPolylineBoundary(w, exterior, boundaryPathType: 3);
        foreach (IReadOnlyList<(double X, double Y)> interior in interiors)
        {
            WriteHatchPolylineBoundary(w, interior, boundaryPathType: 2);
        }

        WritePair(w, 75, 0);
        WritePair(w, 76, 1);
        WritePair(w, 98, 1);
        WritePair(w, 10, seed.X);
        WritePair(w, 20, seed.Y);
    }

    private static void WriteHatchPolylineBoundary(
        TextWriter w,
        IReadOnlyList<(double X, double Y)> vertices,
        int boundaryPathType)
    {
        WritePair(w, 92, boundaryPathType);
        WritePair(w, 72, 0);
        WritePair(w, 73, 1);
        WritePair(w, 93, vertices.Count);
        for (int index = 0; index < vertices.Count; index++)
        {
            WritePair(w, 10, vertices[index].X);
            WritePair(w, 20, vertices[index].Y);
        }

        WritePair(w, 97, 0);
    }

    private static bool TryGetTriangleVertices(
        Polygon polygon,
        out (double X, double Y) a,
        out (double X, double Y) b,
        out (double X, double Y) c)
    {
        a = default;
        b = default;
        c = default;
        if (polygon.Area <= AreaEpsilon)
        {
            return false;
        }

        List<(double X, double Y)> vertices = new List<(double X, double Y)>(3);
        foreach (Coordinate coordinate in polygon.ExteriorRing.Coordinates)
        {
            (double X, double Y) point = (coordinate.X, coordinate.Y);
            if (vertices.Count == 0 || !SamePoint(vertices[vertices.Count - 1], point))
            {
                vertices.Add(point);
            }
        }

        while (vertices.Count > 1 && SamePoint(vertices[0], vertices[vertices.Count - 1]))
        {
            vertices.RemoveAt(vertices.Count - 1);
        }

        if (vertices.Count != 3)
        {
            return false;
        }

        a = vertices[0];
        b = vertices[1];
        c = vertices[2];
        return true;
    }

    private static int WriteSolidTriangle(
        TextWriter w,
        string layer,
        (double X, double Y) a,
        (double X, double Y) b,
        (double X, double Y) c)
    {
        WritePair(w, 0, "SOLID");
        WritePair(w, 8, layer);
        WritePair(w, 10, a.X);
        WritePair(w, 20, a.Y);
        WritePair(w, 30, 0.0);
        WritePair(w, 11, b.X);
        WritePair(w, 21, b.Y);
        WritePair(w, 31, 0.0);
        WritePair(w, 12, c.X);
        WritePair(w, 22, c.Y);
        WritePair(w, 32, 0.0);
        WritePair(w, 13, c.X);
        WritePair(w, 23, c.Y);
        WritePair(w, 33, 0.0);
        return 1;
    }

    private static bool SameCoordinate(Coordinate left, Coordinate right)
    {
        return left.X == right.X && left.Y == right.Y;
    }

    private static bool SamePoint((double X, double Y) left, (double X, double Y) right)
    {
        return left.X == right.X && left.Y == right.Y;
    }

    private static void WriteMarker(TextWriter w, Vector3d marker)
    {
        WritePair(w, 0, "CIRCLE");
        WritePair(w, 8, MarkerLayer);
        WritePair(w, 10, marker.X);
        WritePair(w, 20, marker.Y);
        WritePair(w, 30, marker.Z);
        WritePair(w, 40, MarkerRingRadiusMeters);

        WriteLine(w, MarkerLayer,
            marker.X - MarkerCrossHalfLengthMeters, marker.Y, marker.Z,
            marker.X + MarkerCrossHalfLengthMeters, marker.Y, marker.Z);
        WriteLine(w, MarkerLayer,
            marker.X, marker.Y - MarkerCrossHalfLengthMeters, marker.Z,
            marker.X, marker.Y + MarkerCrossHalfLengthMeters, marker.Z);
    }

    private static void WriteLine(TextWriter w, string layer,
        double x1, double y1, double z1, double x2, double y2, double z2)
    {
        WritePair(w, 0, "LINE");
        WritePair(w, 8, layer);
        WritePair(w, 10, x1);
        WritePair(w, 20, y1);
        WritePair(w, 30, z1);
        WritePair(w, 11, x2);
        WritePair(w, 21, y2);
        WritePair(w, 31, z2);
    }

    private static void WritePair(TextWriter w, int groupCode, string value)
    {
        w.Write(groupCode.ToString(CultureInfo.InvariantCulture));
        w.Write('\n');
        w.Write(value);
        w.Write('\n');
    }

    private static void WritePair(TextWriter w, int groupCode, int value)
    {
        WritePair(w, groupCode, value.ToString(CultureInfo.InvariantCulture));
    }

    private static void WritePair(TextWriter w, int groupCode, double value)
    {
        WritePair(w, groupCode, value.ToString(FloatFormat, CultureInfo.InvariantCulture));
    }
}
