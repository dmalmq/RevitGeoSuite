using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RevitGeoSuite.Core.Plateau.Tiles3D;

namespace RevitGeoSuite.PlateauImport.Online;

/// <summary>
/// Writes a minimal AutoCAD R12 (AC1009) DXF containing per-feature convex-hull
/// footprint polylines and a marker at the project base point. R12 is the lowest
/// common denominator format every CAD tool reads. Coordinates are emitted in metres.
/// </summary>
public static class PlateauFootprintDxfWriter
{
    private const string DefaultLayer = "0";
    private const string BuildingLayer = "PLATEAU_BUILDINGS";
    private const string MarkerLayer = "PROJECT_BASE_POINT";
    private const string Continuous = "CONTINUOUS";
    private const string FloatFormat = "0.0##############";

    // Survey-control symbol: 1 m ring with a + cross whose legs extend 0.5 m past the
    // ring on all four sides (cross half-length 1.5 m). Sizes in metres.
    private const double MarkerRingRadiusMeters = 1.0;
    private const double MarkerCrossHalfLengthMeters = 1.5;

    public sealed class WriteResult
    {
        public WriteResult(int featureCount, int polylineCount, IReadOnlyList<string> warnings)
        {
            FeatureCount = featureCount;
            PolylineCount = polylineCount;
            Warnings = warnings;
        }

        public int FeatureCount { get; }
        public int PolylineCount { get; }
        public IReadOnlyList<string> Warnings { get; }
    }

    /// <summary>
    /// Writes the DXF with vertex and marker coordinates shifted so that the supplied
    /// <paramref name="originOffsetMeters"/> position in the input frame lands at DXF
    /// (0,0,0). Use the Revit Survey Point's local-metre position as the offset to
    /// follow the Civil 3D shared-coordinates convention.
    /// </summary>
    public static WriteResult Write(
        TextWriter writer,
        PlateauTilesetModel buildings,
        Vector3d markerMeters,
        Vector3d originOffsetMeters)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        if (buildings is null) throw new ArgumentNullException(nameof(buildings));

        List<string> warnings = new List<string>();

        WriteHeaderSection(writer);
        WriteTablesSection(writer);
        WriteEntitiesStart(writer);

        int polylines = 0;
        foreach (PlateauTilesetFeature feature in buildings.Features)
        {
            if (feature.Triangles.Count == 0)
            {
                warnings.Add($"Skipped {feature.Id}: feature contained no triangles.");
                continue;
            }

            List<(double X, double Y)> vertices = new List<(double X, double Y)>(feature.Triangles.Count * 3);
            foreach (PlateauTilesetTriangle tri in feature.Triangles)
            {
                vertices.Add((tri.A.X - originOffsetMeters.X, tri.A.Y - originOffsetMeters.Y));
                vertices.Add((tri.B.X - originOffsetMeters.X, tri.B.Y - originOffsetMeters.Y));
                vertices.Add((tri.C.X - originOffsetMeters.X, tri.C.Y - originOffsetMeters.Y));
            }

            List<(double X, double Y)> hull = ConvexHull.Compute(vertices);
            if (hull.Count < 3)
            {
                warnings.Add($"Skipped {feature.Id}: convex hull collapsed to <3 points.");
                continue;
            }

            WriteClosedPolyline(writer, hull, BuildingLayer);
            polylines++;
        }

        Vector3d markerInDxfFrame = new Vector3d(
            markerMeters.X - originOffsetMeters.X,
            markerMeters.Y - originOffsetMeters.Y,
            markerMeters.Z - originOffsetMeters.Z);
        WriteMarker(writer, markerInDxfFrame);
        WriteSectionEnd(writer);
        WriteEof(writer);

        return new WriteResult(buildings.Features.Count, polylines, warnings);
    }

    private static void WriteHeaderSection(TextWriter w)
    {
        WritePair(w, 0, "SECTION");
        WritePair(w, 2, "HEADER");
        WritePair(w, 9, "$ACADVER");
        WritePair(w, 1, "AC1009");
        WriteSectionEnd(w);
    }

    private static void WriteTablesSection(TextWriter w)
    {
        WritePair(w, 0, "SECTION");
        WritePair(w, 2, "TABLES");
        WritePair(w, 0, "TABLE");
        WritePair(w, 2, "LAYER");
        WritePair(w, 70, 3);
        WriteLayer(w, DefaultLayer, colorIndex: 7);
        WriteLayer(w, BuildingLayer, colorIndex: 3);
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
        WritePair(w, 66, 1); // vertices-follow flag
        WritePair(w, 10, 0.0);
        WritePair(w, 20, 0.0);
        WritePair(w, 30, 0.0);
        WritePair(w, 70, 1); // closed polyline
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

    private static void WriteMarker(TextWriter w, Vector3d marker)
    {
        // Standard survey-control symbol: circle with a plus-sign cross whose legs
        // extend past the ring. Centre of the cross sits at the project base point.
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
