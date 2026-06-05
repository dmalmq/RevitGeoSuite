using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Simplify;
using RevitGeoSuite.Core.Plateau.Tiles3D;

namespace RevitGeoSuite.PlateauImport.Online;

/// <summary>
/// Builds 2D building footprint outlines from already transformed online 3D-Tiles meshes. The
/// downloader has converted ECEF vertices into the model's internal metre frame, so these outlines
/// can be written directly into the lightweight basemap DXF.
/// </summary>
public sealed class PlateauOnlineFootprintBuilder
{
    private const string BuildingLayer = "PLATEAU_BUILDINGS";
    private const double DefaultMinimumTriangleAreaSquareMeters = 0.01d;
    private const double DefaultSimplifyToleranceMeters = 0.10d;

    private readonly GeometryFactory geometryFactory;
    private readonly double minimumTriangleAreaSquareMeters;
    private readonly double simplifyToleranceMeters;

    public PlateauOnlineFootprintBuilder()
        : this(DefaultMinimumTriangleAreaSquareMeters, DefaultSimplifyToleranceMeters)
    {
    }

    public PlateauOnlineFootprintBuilder(double minimumTriangleAreaSquareMeters, double simplifyToleranceMeters)
    {
        if (minimumTriangleAreaSquareMeters <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumTriangleAreaSquareMeters), minimumTriangleAreaSquareMeters, "Minimum triangle area must be positive.");
        }

        if (simplifyToleranceMeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(simplifyToleranceMeters), simplifyToleranceMeters, "Simplify tolerance cannot be negative.");
        }

        this.minimumTriangleAreaSquareMeters = minimumTriangleAreaSquareMeters;
        this.simplifyToleranceMeters = simplifyToleranceMeters;
        geometryFactory = new GeometryFactory(new PrecisionModel(1000d));
    }

    public IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> Build(
        PlateauTilesetModel buildings,
        ICollection<string> warnings)
    {
        if (buildings is null) throw new ArgumentNullException(nameof(buildings));
        if (warnings is null) throw new ArgumentNullException(nameof(warnings));

        List<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines = new List<PlateauContextOutlinesDxfWriter.OutlineFeature>();
        foreach (PlateauTilesetFeature feature in buildings.Features)
        {
            Geometry? footprint = BuildFeatureFootprint(feature, warnings);
            if (footprint is null || footprint.IsEmpty)
            {
                continue;
            }

            AddPolygonOutlines(footprint, feature.Id, outlines);
        }

        return outlines;
    }

    private Geometry? BuildFeatureFootprint(PlateauTilesetFeature feature, ICollection<string> warnings)
    {
        if (feature.Triangles.Count == 0)
        {
            warnings.Add($"Skipped {feature.Id}: feature contained no triangles.");
            return null;
        }

        List<Geometry> trianglePolygons = new List<Geometry>();
        foreach (PlateauTilesetTriangle triangle in feature.Triangles)
        {
            Polygon? polygon = CreateProjectedTrianglePolygon(triangle);
            if (polygon is not null)
            {
                trianglePolygons.Add(polygon);
            }
        }

        if (trianglePolygons.Count == 0)
        {
            warnings.Add($"Skipped {feature.Id}: projected mesh triangles collapsed to no usable footprint area.");
            return null;
        }

        Geometry unioned;
        try
        {
            unioned = UnaryUnionOp.Union(trianglePolygons);
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            warnings.Add($"Skipped {feature.Id}: footprint union failed ({ex.Message}).");
            return null;
        }

        Geometry repaired;
        try
        {
            repaired = unioned.IsValid ? unioned : unioned.Buffer(0d);
            if (simplifyToleranceMeters > 0d && !repaired.IsEmpty)
            {
                repaired = TopologyPreservingSimplifier.Simplify(repaired, simplifyToleranceMeters);
            }

            if (!repaired.IsValid)
            {
                repaired = repaired.Buffer(0d);
            }
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            warnings.Add($"Skipped {feature.Id}: footprint repair failed ({ex.Message}).");
            return null;
        }

        return repaired.IsEmpty ? null : repaired;
    }

    private Polygon? CreateProjectedTrianglePolygon(PlateauTilesetTriangle triangle)
    {
        Coordinate a = new Coordinate(triangle.A.X, triangle.A.Y);
        Coordinate b = new Coordinate(triangle.B.X, triangle.B.Y);
        Coordinate c = new Coordinate(triangle.C.X, triangle.C.Y);
        double area = Math.Abs(SignedArea(a, b, c));
        if (area <= minimumTriangleAreaSquareMeters)
        {
            return null;
        }

        if (SameCoordinate(a, b) || SameCoordinate(b, c) || SameCoordinate(c, a))
        {
            return null;
        }

        try
        {
            return geometryFactory.CreatePolygon(new[] { a, b, c, a });
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static double SignedArea(Coordinate a, Coordinate b, Coordinate c)
    {
        return ((a.X * (b.Y - c.Y)) + (b.X * (c.Y - a.Y)) + (c.X * (a.Y - b.Y))) / 2d;
    }

    private static void AddPolygonOutlines(
        Geometry geometry,
        string sourceId,
        ICollection<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines)
    {
        if (geometry is Polygon polygon)
        {
            AddPolygonOutlines(polygon, sourceId, outlines);
            return;
        }

        for (int index = 0; index < geometry.NumGeometries; index++)
        {
            Geometry child = geometry.GetGeometryN(index);
            if (!child.IsEmpty)
            {
                AddPolygonOutlines(child, sourceId, outlines);
            }
        }
    }

    private static void AddPolygonOutlines(
        Polygon polygon,
        string sourceId,
        ICollection<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines)
    {
        IReadOnlyList<(double X, double Y)> exterior = ToRing(polygon.ExteriorRing.Coordinates);
        if (exterior.Count >= 3)
        {
            outlines.Add(new PlateauContextOutlinesDxfWriter.OutlineFeature(BuildingLayer, exterior, sourceId));
        }

        for (int index = 0; index < polygon.NumInteriorRings; index++)
        {
            IReadOnlyList<(double X, double Y)> interior = ToRing(polygon.GetInteriorRingN(index).Coordinates);
            if (interior.Count >= 3)
            {
                outlines.Add(new PlateauContextOutlinesDxfWriter.OutlineFeature(
                    BuildingLayer,
                    interior,
                    string.Concat(sourceId, "-courtyard-", (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))));
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

    private static bool SameCoordinate(Coordinate left, Coordinate right)
    {
        return left.X == right.X && left.Y == right.Y;
    }

    private static bool SamePoint((double X, double Y) left, (double X, double Y) right)
    {
        return left.X == right.X && left.Y == right.Y;
    }
}
