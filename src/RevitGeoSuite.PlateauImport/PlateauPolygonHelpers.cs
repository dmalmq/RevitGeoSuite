using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;

namespace RevitGeoSuite.PlateauImport;

internal static class PlateauPolygonHelpers
{
    public static LinearRing? CreateLinearRing(GeometryFactory geometryFactory, IReadOnlyList<(double X, double Y)> ring)
    {
        if (geometryFactory is null) throw new ArgumentNullException(nameof(geometryFactory));
        if (ring is null || ring.Count < 3)
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

    public static bool SameCoordinate(Coordinate a, Coordinate b)
    {
        return a.X == b.X && a.Y == b.Y;
    }
}
