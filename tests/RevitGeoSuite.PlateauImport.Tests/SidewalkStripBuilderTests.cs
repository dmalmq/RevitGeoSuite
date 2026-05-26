using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class SidewalkStripBuilderTests
{
    [Fact]
    public void Build_buffers_curved_line_to_side_it_curves_towards()
    {
        TileContext tile = TileContext.Centered("53394536");
        double cx = tile.Center.Easting;
        double cy = tile.Center.Northing;

        IReadOnlyList<KibanPolygonExportFeature> output = SidewalkStripBuilder.Build(
            new[] { MakeSidewalk("curve-left", (cx - 30d, cy), (cx, cy), (cx, cy + 30d)) },
            Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
            new[] { tile.TileId },
            tile.ProjectCrs,
            tile.Transformer,
            SidewalkStripOptions.Default);

        Polygon polygon = ToPolygon(Assert.Single(output));
        Assert.True(polygon.Covers(PointAt(cx - 2d, cy + 2d)));
        Assert.False(polygon.Covers(PointAt(cx + 2d, cy - 2d)));
    }

    [Fact]
    public void Build_reversed_curved_line_buffers_to_same_physical_side()
    {
        TileContext tile = TileContext.Centered("53394536");
        double cx = tile.Center.Easting;
        double cy = tile.Center.Northing;

        IReadOnlyList<KibanPolygonExportFeature> output = SidewalkStripBuilder.Build(
            new[] { MakeSidewalk("curve-reversed", (cx, cy + 30d), (cx, cy), (cx - 30d, cy)) },
            Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
            new[] { tile.TileId },
            tile.ProjectCrs,
            tile.Transformer,
            SidewalkStripOptions.Default);

        Polygon polygon = ToPolygon(Assert.Single(output));
        Assert.True(polygon.Covers(PointAt(cx - 2d, cy + 2d)));
        Assert.False(polygon.Covers(PointAt(cx + 2d, cy - 2d)));
    }

    [Fact]
    public void Build_buffers_straight_line_away_from_nearby_road()
    {
        TileContext tile = TileContext.Centered("53394536");
        double cx = tile.Center.Easting;
        double cy = tile.Center.Northing;

        IReadOnlyList<KibanPolygonExportFeature> output = SidewalkStripBuilder.Build(
            new[] { MakeSidewalk("straight", (cx - 50d, cy), (cx + 50d, cy)) },
            new[] { MakeRoad("road-below", (cx - 70d, cy - 6d), (cx + 70d, cy - 6d), (cx + 70d, cy - 1d), (cx - 70d, cy - 1d)) },
            new[] { tile.TileId },
            tile.ProjectCrs,
            tile.Transformer,
            SidewalkStripOptions.Default);

        Polygon polygon = ToPolygon(Assert.Single(output));
        Assert.True(polygon.Covers(PointAt(cx, cy + 2d)));
        Assert.False(polygon.Covers(PointAt(cx, cy - 2d)));
    }

    [Fact]
    public void Build_skips_straight_line_without_road_context()
    {
        TileContext tile = TileContext.Centered("53394536");
        double cx = tile.Center.Easting;
        double cy = tile.Center.Northing;
        List<string> warnings = new List<string>();

        IReadOnlyList<KibanPolygonExportFeature> output = SidewalkStripBuilder.Build(
            new[] { MakeSidewalk("ambiguous", (cx - 50d, cy), (cx + 50d, cy)) },
            Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
            new[] { tile.TileId },
            tile.ProjectCrs,
            tile.Transformer,
            SidewalkStripOptions.Default,
            warnings);

        Assert.Empty(output);
        Assert.Contains(warnings, warning => warning.IndexOf("inside side could not be inferred", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Build_uses_four_metres_on_one_side_not_two_each_side()
    {
        TileContext tile = TileContext.Centered("53394536");
        double cx = tile.Center.Easting;
        double cy = tile.Center.Northing;

        KibanPolygonExportFeature feature = Assert.Single(SidewalkStripBuilder.Build(
            new[] { MakeSidewalk("width", (cx - 50d, cy), (cx + 50d, cy)) },
            new[] { MakeRoad("road-below", (cx - 70d, cy - 6d), (cx + 70d, cy - 6d), (cx + 70d, cy - 1d), (cx - 70d, cy - 1d)) },
            new[] { tile.TileId },
            tile.ProjectCrs,
            tile.Transformer,
            SidewalkStripOptions.Default));

        Assert.InRange(Math.Abs(SignedArea(feature.ExteriorRingMetres)), 390d, 410d);
        Polygon polygon = ToPolygon(feature);
        Assert.True(polygon.Covers(PointAt(cx, cy + 3.8d)));
        Assert.False(polygon.Covers(PointAt(cx, cy + 4.2d)));
        Assert.False(polygon.Covers(PointAt(cx, cy - 0.1d)));
    }

    [Fact]
    public void Build_clips_strips_to_selected_tile_bounds()
    {
        TileContext tile = TileContext.Centered("53394536");
        Envelope envelope = tile.ClipBox.EnvelopeInternal;
        double y = (envelope.MinY + envelope.MaxY) / 2d;
        double startX = envelope.MaxX - 50d;
        double endX = envelope.MaxX + 50d;

        IReadOnlyList<KibanPolygonExportFeature> output = SidewalkStripBuilder.Build(
            new[] { MakeSidewalk("clip", (startX, y), (endX, y)) },
            new[] { MakeRoad("road-below", (startX - 20d, y - 6d), (endX + 20d, y - 6d), (endX + 20d, y - 1d), (startX - 20d, y - 1d)) },
            new[] { tile.TileId },
            tile.ProjectCrs,
            tile.Transformer,
            SidewalkStripOptions.Default);

        KibanPolygonExportFeature feature = Assert.Single(output);
        Assert.All(feature.ExteriorRingMetres, vertex => Assert.True(vertex.X <= envelope.MaxX + 1d));
        Assert.InRange(Math.Abs(SignedArea(feature.ExteriorRingMetres)), 150d, 250d);
    }

    [Fact]
    public void Build_keeps_multiple_source_lines_as_separate_features()
    {
        TileContext tile = TileContext.Centered("53394536");
        double cx = tile.Center.Easting;
        double cy = tile.Center.Northing;

        IReadOnlyList<KibanPolygonExportFeature> output = SidewalkStripBuilder.Build(
            new[]
            {
                MakeSidewalk("one", (cx - 60d, cy), (cx - 20d, cy)),
                MakeSidewalk("two", (cx + 20d, cy), (cx + 60d, cy)),
            },
            new[]
            {
                MakeRoad("road-one", (cx - 70d, cy - 6d), (cx - 10d, cy - 6d), (cx - 10d, cy - 1d), (cx - 70d, cy - 1d)),
                MakeRoad("road-two", (cx + 10d, cy - 6d), (cx + 70d, cy - 6d), (cx + 70d, cy - 1d), (cx + 10d, cy - 1d)),
            },
            new[] { tile.TileId },
            tile.ProjectCrs,
            tile.Transformer,
            SidewalkStripOptions.Default);

        Assert.Equal(2, output.Count);
        Assert.Contains(output, feature => feature.SourceId is not null && feature.SourceId.StartsWith("one:", StringComparison.Ordinal));
        Assert.Contains(output, feature => feature.SourceId is not null && feature.SourceId.StartsWith("two:", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_ignores_non_sidewalk_lines()
    {
        TileContext tile = TileContext.Centered("53394536");
        double cx = tile.Center.Easting;
        double cy = tile.Center.Northing;

        IReadOnlyList<KibanPolygonExportFeature> output = SidewalkStripBuilder.Build(
            new[]
            {
                MakeSidewalk("sidewalk", (cx - 30d, cy), (cx, cy), (cx, cy + 30d)),
                MakeLine(PlateauContextOutlinesDxfWriter.GsiRailwaysLayer, "rail", (cx + 10d, cy), (cx + 30d, cy)),
            },
            Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
            new[] { tile.TileId },
            tile.ProjectCrs,
            tile.Transformer,
            SidewalkStripOptions.Default);

        KibanPolygonExportFeature feature = Assert.Single(output);
        Assert.Equal(PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, feature.Layer);
        Assert.StartsWith("sidewalk:", feature.SourceId, StringComparison.Ordinal);
    }

    private static KibanLineExportFeature MakeSidewalk(
        string sourceId,
        params (double X, double Y)[] vertices)
    {
        return MakeLine(PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, sourceId, vertices);
    }

    private static KibanLineExportFeature MakeLine(
        string layer,
        string sourceId,
        params (double X, double Y)[] vertices)
    {
        return new KibanLineExportFeature(
            layer,
            vertices,
            sourceId,
            meshCode: string.Empty,
            sourcePath: string.Empty,
            featureType: layer == PlateauContextOutlinesDxfWriter.GsiSidewalksLayer ? "歩道" : "普通鉄道",
            visibility: "表示");
    }

    private static PlateauContextOutlinesDxfWriter.AreaFeature MakeRoad(
        string sourceId,
        params (double X, double Y)[] vertices)
    {
        return new PlateauContextOutlinesDxfWriter.AreaFeature(
            "PLATEAU_ROADS",
            vertices,
            sourceId: sourceId);
    }

    private static Polygon ToPolygon(KibanPolygonExportFeature feature)
    {
        GeometryFactory factory = new GeometryFactory();
        LinearRing shell = factory.CreateLinearRing(feature.ExteriorRingMetres.Select(vertex => new Coordinate(vertex.X, vertex.Y)).ToArray());
        LinearRing[] holes = feature.InteriorRingsMetres
            .Select(ring => factory.CreateLinearRing(ring.Select(vertex => new Coordinate(vertex.X, vertex.Y)).ToArray()))
            .ToArray();
        return factory.CreatePolygon(shell, holes);
    }

    private static Point PointAt(double x, double y)
    {
        return new GeometryFactory().CreatePoint(new Coordinate(x, y));
    }

    private static double SignedArea(IReadOnlyList<(double X, double Y)> ring)
    {
        double sum = 0d;
        for (int i = 0; i < ring.Count - 1; i++)
        {
            (double x0, double y0) = ring[i];
            (double x1, double y1) = ring[i + 1];
            sum += (x0 * y1) - (x1 * y0);
        }

        return sum / 2d;
    }

    private sealed class TileContext
    {
        private TileContext(
            string tileId,
            CrsReference projectCrs,
            CoordinateTransformer transformer,
            ProjectedCoordinate center,
            Polygon clipBox)
        {
            TileId = tileId;
            ProjectCrs = projectCrs;
            Transformer = transformer;
            Center = center;
            ClipBox = clipBox;
        }

        public string TileId { get; }
        public CrsReference ProjectCrs { get; }
        public CoordinateTransformer Transformer { get; }
        public ProjectedCoordinate Center { get; }
        public Polygon ClipBox { get; }

        public static TileContext Centered(string tileId)
        {
            JapanMeshCalculator calc = new JapanMeshCalculator();
            MeshBounds bounds = calc.GetBounds(new MeshCode { Value = tileId });
            double midLat = (bounds.SouthLatitude + bounds.NorthLatitude) / 2d;
            double midLon = (bounds.WestLongitude + bounds.EastLongitude) / 2d;
            CrsReference crs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" };
            CoordinateTransformer transformer = new CoordinateTransformer(new CrsRegistry());
            ProjectedCoordinate center = transformer.Project(new GeographicCoordinate(midLat, midLon), crs);
            Polygon clipBox = KibanGeometryConverter.BuildSelectedTileClipPolygons(new[] { tileId }, crs, transformer).Single().ClipBox;
            return new TileContext(tileId, crs, transformer, center, clipBox);
        }
    }
}
