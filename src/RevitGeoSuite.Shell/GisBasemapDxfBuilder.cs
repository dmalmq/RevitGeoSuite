using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NetTopologySuite.Geometries;
using RevitGeoSuite.FloorPlanExport.Core.Gis;
using RevitGeoSuite.PlateauImport;

namespace RevitGeoSuite.Shell;

/// <summary>
/// Turns reprojected GIS layers into the 2D DXF feature lists consumed by
/// <see cref="PlateauContextDxfImporter.WriteOutlineDxf"/>. Each input coordinate is run through the
/// supplied projector (file CRS → project CRS metres), then <see cref="PlateauReferenceFrame.ToLocalFeet"/>
/// and converted to the model-internal metre frame the DXF importer expects, so the basemap lands over
/// the model on import. Polygons become closed outline polylines (exterior + holes), lines become
/// polylines, and points become small "+" markers on a companion layer (the DXF writer has no point
/// entity).
/// </summary>
public static class GisBasemapDxfBuilder
{
    private const double FeetToMetres = 0.3048d;
    private const double PointMarkerHalfLengthMetres = 0.5d;
    private const int MaxDxfLayerNameLength = 31;
    private const double CoincidentEpsilon = 1e-9d;

    public sealed class BuildResult
    {
        public BuildResult(
            IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines,
            IReadOnlyList<PlateauContextOutlinesDxfWriter.LineFeature> lines,
            int polygonCount,
            int lineCount,
            int pointCount)
        {
            Outlines = outlines;
            Lines = lines;
            PolygonCount = polygonCount;
            LineCount = lineCount;
            PointCount = pointCount;
        }

        public IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> Outlines { get; }

        public IReadOnlyList<PlateauContextOutlinesDxfWriter.LineFeature> Lines { get; }

        public int PolygonCount { get; }

        public int LineCount { get; }

        public int PointCount { get; }

        public bool IsEmpty => Outlines.Count == 0 && Lines.Count == 0;
    }

    public static BuildResult Build(
        IReadOnlyList<GisLayerGeometry> layers,
        Func<double, double, (double X, double Y)> projectToProjectCrsMetres,
        PlateauImportReferenceContext referenceContext,
        ISet<string>? usedLayerNames = null,
        DxfLayerColor? layerColor = null)
    {
        if (layers is null) throw new ArgumentNullException(nameof(layers));
        if (projectToProjectCrsMetres is null) throw new ArgumentNullException(nameof(projectToProjectCrsMetres));
        if (referenceContext is null) throw new ArgumentNullException(nameof(referenceContext));

        (double X, double Y) ToModelMetres(double x, double y)
        {
            (double easting, double northing) = projectToProjectCrsMetres(x, y);
            (double xFeet, double yFeet) = PlateauReferenceFrame.ToLocalFeet(easting, northing, referenceContext);
            return (xFeet * FeetToMetres, yFeet * FeetToMetres);
        }

        List<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines = new();
        List<PlateauContextOutlinesDxfWriter.LineFeature> lines = new();
        Counters counters = new();
        ISet<string> layerNameScope = usedLayerNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (GisLayerGeometry layer in layers)
        {
            string layerName = ReserveUniqueLayerName(SanitizeLayerName(layer.Name), layerNameScope);
            string pointLayerName = ReserveUniqueLayerName(SanitizeLayerName(layer.Name + "_PT"), layerNameScope);
            foreach (Geometry geometry in layer.Geometries)
            {
                AddGeometry(geometry, layerName, pointLayerName, ToModelMetres, layerColor, outlines, lines, counters);
            }
        }

        return new BuildResult(outlines, lines, counters.Polygons, counters.Lines, counters.Points);
    }

    private static void AddGeometry(
        Geometry geometry,
        string layerName,
        string pointLayerName,
        Func<double, double, (double X, double Y)> toModel,
        DxfLayerColor? layerColor,
        ICollection<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines,
        ICollection<PlateauContextOutlinesDxfWriter.LineFeature> lines,
        Counters counters)
    {
        if (geometry is null || geometry.IsEmpty)
        {
            return;
        }

        switch (geometry)
        {
            case Point point:
                AddPoint(point, pointLayerName, toModel, layerColor, lines, counters);
                break;

            case Polygon polygon:
                AddPolygon(polygon, layerName, toModel, layerColor, outlines, counters);
                break;

            case LineString lineString:
                AddLine(lineString, layerName, toModel, layerColor, lines, counters);
                break;

            // MultiPolygon / MultiPoint / MultiLineString all derive from GeometryCollection; recurse
            // into their parts (which are the singular types matched above).
            case GeometryCollection collection:
                for (int index = 0; index < collection.NumGeometries; index++)
                {
                    AddGeometry(collection.GetGeometryN(index), layerName, pointLayerName, toModel, layerColor, outlines, lines, counters);
                }

                break;
        }
    }

    private static void AddPolygon(
        Polygon polygon,
        string layerName,
        Func<double, double, (double X, double Y)> toModel,
        DxfLayerColor? layerColor,
        ICollection<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines,
        Counters counters)
    {
        List<(double X, double Y)> exterior = ToVertices(polygon.ExteriorRing.Coordinates, toModel, isRing: true);
        if (exterior.Count < 3)
        {
            return;
        }

        outlines.Add(new PlateauContextOutlinesDxfWriter.OutlineFeature(layerName, exterior, layerColor: layerColor));
        counters.Polygons++;

        for (int index = 0; index < polygon.NumInteriorRings; index++)
        {
            List<(double X, double Y)> hole = ToVertices(polygon.GetInteriorRingN(index).Coordinates, toModel, isRing: true);
            if (hole.Count >= 3)
            {
                outlines.Add(new PlateauContextOutlinesDxfWriter.OutlineFeature(layerName, hole, layerColor: layerColor));
            }
        }
    }

    private static void AddLine(
        LineString lineString,
        string layerName,
        Func<double, double, (double X, double Y)> toModel,
        DxfLayerColor? layerColor,
        ICollection<PlateauContextOutlinesDxfWriter.LineFeature> lines,
        Counters counters)
    {
        List<(double X, double Y)> vertices = ToVertices(lineString.Coordinates, toModel, isRing: false);
        if (vertices.Count < 2)
        {
            return;
        }

        lines.Add(new PlateauContextOutlinesDxfWriter.LineFeature(layerName, vertices, layerColor: layerColor));
        counters.Lines++;
    }

    private static void AddPoint(
        Point point,
        string pointLayerName,
        Func<double, double, (double X, double Y)> toModel,
        DxfLayerColor? layerColor,
        ICollection<PlateauContextOutlinesDxfWriter.LineFeature> lines,
        Counters counters)
    {
        (double cx, double cy) = toModel(point.X, point.Y);
        const double h = PointMarkerHalfLengthMetres;

        lines.Add(new PlateauContextOutlinesDxfWriter.LineFeature(
            pointLayerName,
            new[] { (cx - h, cy), (cx + h, cy) },
            layerColor: layerColor));
        lines.Add(new PlateauContextOutlinesDxfWriter.LineFeature(
            pointLayerName,
            new[] { (cx, cy - h), (cx, cy + h) },
            layerColor: layerColor));
        counters.Points++;
    }

    private static List<(double X, double Y)> ToVertices(
        Coordinate[] coordinates,
        Func<double, double, (double X, double Y)> toModel,
        bool isRing)
    {
        List<(double X, double Y)> result = new(coordinates.Length);
        foreach (Coordinate coordinate in coordinates)
        {
            (double X, double Y) point = toModel(coordinate.X, coordinate.Y);
            if (result.Count == 0 || !Coincident(result[result.Count - 1], point))
            {
                result.Add(point);
            }
        }

        // The DXF writer closes rings itself (POLYLINE flag 70=1), so drop the duplicate closing
        // vertex NetTopologySuite stores to avoid a zero-length closing segment.
        if (isRing)
        {
            while (result.Count > 1 && Coincident(result[0], result[result.Count - 1]))
            {
                result.RemoveAt(result.Count - 1);
            }
        }

        return result;
    }

    private static bool Coincident((double X, double Y) a, (double X, double Y) b)
    {
        return Math.Abs(a.X - b.X) < CoincidentEpsilon && Math.Abs(a.Y - b.Y) < CoincidentEpsilon;
    }

    /// <summary>
    /// Coerces a source layer/table name into a DXF (AutoCAD R12) layer name: uppercase, only
    /// letters/digits/<c>_ - $</c>, trimmed and capped at 31 characters. Falls back to "GIS".
    /// </summary>
    private static string SanitizeLayerName(string name)
    {
        string source = string.IsNullOrWhiteSpace(name) ? "GIS" : name;
        StringBuilder builder = new(source.Length);
        foreach (char ch in source.ToUpperInvariant())
        {
            builder.Append(IsValidLayerChar(ch) ? ch : '_');
        }

        string sanitized = builder.ToString().Trim('_');
        if (sanitized.Length == 0)
        {
            sanitized = "GIS";
        }

        return sanitized.Length > MaxDxfLayerNameLength
            ? sanitized.Substring(0, MaxDxfLayerNameLength)
            : sanitized;
    }

    private static string ReserveUniqueLayerName(string baseName, ISet<string> usedLayerNames)
    {
        if (usedLayerNames.Add(baseName))
        {
            return baseName;
        }

        for (int index = 2; index < 1000; index++)
        {
            string suffix = "_" + index.ToString(CultureInfo.InvariantCulture);
            int prefixLength = Math.Min(baseName.Length, MaxDxfLayerNameLength - suffix.Length);
            if (prefixLength <= 0)
            {
                continue;
            }

            string candidate = baseName.Substring(0, prefixLength) + suffix;
            if (usedLayerNames.Add(candidate))
            {
                return candidate;
            }
        }

        for (int index = 0; index < 1000; index++)
        {
            string candidate = "GIS_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
            if (usedLayerNames.Add(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to allocate a unique DXF layer name.");
    }

    private static bool IsValidLayerChar(char ch)
    {
        return (ch >= 'A' && ch <= 'Z')
            || (ch >= '0' && ch <= '9')
            || ch == '_'
            || ch == '-'
            || ch == '$';
    }

    private sealed class Counters
    {
        public int Polygons;
        public int Lines;
        public int Points;
    }
}
