using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;

namespace RevitGeoSuite.PlateauImport.Online;

public static class PlateauOnlineBasemapFeatureBuilder
{
    public const string BuildingsLayer = "PLATEAU_BUILDINGS";
    public const string RoadsLayer = "PLATEAU_ROADS";
    public const string LandUseLayer = "PLATEAU_LANDUSE";

    public static void AddMvtFeatures(
        MvtProjectedFeatures features,
        string layer,
        string sourcePrefix,
        ICollection<PlateauContextOutlinesDxfWriter.AreaFeature> areas,
        ICollection<PlateauContextOutlinesDxfWriter.LineFeature> lines,
        ICollection<string> warnings)
    {
        if (features is null) throw new ArgumentNullException(nameof(features));
        if (areas is null) throw new ArgumentNullException(nameof(areas));
        if (lines is null) throw new ArgumentNullException(nameof(lines));
        if (warnings is null) throw new ArgumentNullException(nameof(warnings));

        if (features.PolygonArea is not null && !features.PolygonArea.IsEmpty)
        {
            AddPolygonAreaFeatures(features.PolygonArea, layer, sourcePrefix, areas);
        }

        int lineIndex = 1;
        foreach (LineString line in features.Lines)
        {
            IReadOnlyList<(double X, double Y)> vertices = ToLine(line.Coordinates);
            if (vertices.Count < 2)
            {
                warnings.Add($"{sourcePrefix}: skipped an MVT line that collapsed to fewer than two vertices.");
                continue;
            }

            lines.Add(new PlateauContextOutlinesDxfWriter.LineFeature(
                layer,
                vertices,
                string.Concat(sourcePrefix, "-line-", lineIndex.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            lineIndex++;
        }
    }

    private static void AddPolygonAreaFeatures(
        Geometry geometry,
        string layer,
        string sourcePrefix,
        ICollection<PlateauContextOutlinesDxfWriter.AreaFeature> areas)
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

            areas.Add(new PlateauContextOutlinesDxfWriter.AreaFeature(
                layer,
                exterior,
                interiors,
                string.Concat(sourcePrefix, "-area-", (areas.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))));
            return;
        }

        for (int index = 0; index < geometry.NumGeometries; index++)
        {
            Geometry child = geometry.GetGeometryN(index);
            if (!child.IsEmpty)
            {
                AddPolygonAreaFeatures(child, layer, sourcePrefix, areas);
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

    private static IReadOnlyList<(double X, double Y)> ToLine(IReadOnlyList<Coordinate> coordinates)
    {
        List<(double X, double Y)> vertices = new List<(double X, double Y)>(coordinates.Count);
        foreach (Coordinate coordinate in coordinates)
        {
            (double X, double Y) point = (coordinate.X, coordinate.Y);
            if (vertices.Count == 0 || !SamePoint(vertices[vertices.Count - 1], point))
            {
                vertices.Add(point);
            }
        }

        return vertices.ToArray();
    }

    private static bool SamePoint((double X, double Y) left, (double X, double Y) right)
    {
        return left.X == right.X && left.Y == right.Y;
    }
}
