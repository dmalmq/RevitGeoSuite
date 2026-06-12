using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauRoadOutlineCleanerTests
{
    private const string RoadLayer = "PLATEAU_ROADS";
    private const string BuildingLayer = "PLATEAU_BUILDINGS";

    [Fact]
    public void DissolveRoads_removes_shared_edge_between_adjacent_road_polygons()
    {
        PlateauContextOutlinesDxfWriter.OutlineFeature building = BuildSquare(BuildingLayer, -20d, 0d, 5d, "building");
        PlateauContextOutlinesDxfWriter.OutlineFeature leftRoad = BuildRectangle(RoadLayer, 0d, 0d, 10d, 10d, "left-road");
        PlateauContextOutlinesDxfWriter.OutlineFeature rightRoad = BuildRectangle(RoadLayer, 10d, 0d, 20d, 10d, "right-road");
        List<string> warnings = new List<string>();

        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = PlateauRoadOutlineCleaner.DissolveRoads(
            new[] { building, leftRoad, rightRoad },
            warnings);

        PlateauContextOutlinesDxfWriter.AreaFeature roadArea = Assert.Single(roadAreas);
        Assert.Empty(warnings);
        Assert.Equal(RoadLayer, roadArea.Layer);
        Assert.Empty(roadArea.InteriorRingsMetres);
        Assert.Equal(200d, ComputeArea(roadArea.ExteriorRingMetres), 6);
        Assert.False(ContainsSegmentNearVertical(roadArea.ExteriorRingMetres, 10d, 0d, 10d, 0.01d));
    }

    [Fact]
    public void DissolveRoads_removes_shared_road_edges_when_one_side_is_split_into_shorter_segments()
    {
        PlateauContextOutlinesDxfWriter.OutlineFeature mainRoad = BuildRectangle(RoadLayer, 0d, 0d, 20d, 10d, "main-road");
        PlateauContextOutlinesDxfWriter.OutlineFeature lowerSideRoad = BuildRectangle(RoadLayer, 20d, 0d, 30d, 5d, "lower-side-road");
        PlateauContextOutlinesDxfWriter.OutlineFeature upperSideRoad = BuildRectangle(RoadLayer, 20d, 5d, 30d, 10d, "upper-side-road");
        List<string> warnings = new List<string>();

        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = PlateauRoadOutlineCleaner.DissolveRoads(
            new[] { mainRoad, lowerSideRoad, upperSideRoad },
            warnings);

        PlateauContextOutlinesDxfWriter.AreaFeature roadArea = Assert.Single(roadAreas);
        Assert.Empty(warnings);
        Assert.Equal(300d, ComputeArea(roadArea.ExteriorRingMetres), 6);
        Assert.False(ContainsSegmentNearVertical(roadArea.ExteriorRingMetres, 20d, 0d, 5d, 0.01d));
        Assert.False(ContainsSegmentNearVertical(roadArea.ExteriorRingMetres, 20d, 5d, 10d, 0.01d));
    }

    [Fact]
    public void DissolveRoads_keeps_disconnected_road_polygons_as_separate_fills_when_there_are_no_shared_edges()
    {
        PlateauContextOutlinesDxfWriter.OutlineFeature first = BuildSquare(RoadLayer, 0d, 0d, 10d, "first-road");
        PlateauContextOutlinesDxfWriter.OutlineFeature second = BuildSquare(RoadLayer, 30d, 0d, 10d, "second-road");
        List<string> warnings = new List<string>();

        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = PlateauRoadOutlineCleaner.DissolveRoads(
            new[] { first, second },
            warnings);

        Assert.Empty(warnings);
        Assert.Equal(2, roadAreas.Count);
        Assert.All(roadAreas, roadArea => Assert.Equal(100d, ComputeArea(roadArea.ExteriorRingMetres), 6));
    }

    [Fact]
    public void DissolveRoads_preserves_interior_ring_for_block_inside_connected_road_network()
    {
        PlateauContextOutlinesDxfWriter.OutlineFeature bottom = BuildRectangle(RoadLayer, 0d, 0d, 30d, 10d, "bottom-road");
        PlateauContextOutlinesDxfWriter.OutlineFeature top = BuildRectangle(RoadLayer, 0d, 20d, 30d, 30d, "top-road");
        PlateauContextOutlinesDxfWriter.OutlineFeature left = BuildRectangle(RoadLayer, 0d, 10d, 10d, 20d, "left-road");
        PlateauContextOutlinesDxfWriter.OutlineFeature right = BuildRectangle(RoadLayer, 20d, 10d, 30d, 20d, "right-road");
        List<string> warnings = new List<string>();

        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = PlateauRoadOutlineCleaner.DissolveRoads(
            new[] { bottom, top, left, right },
            warnings);

        PlateauContextOutlinesDxfWriter.AreaFeature roadArea = Assert.Single(roadAreas);
        IReadOnlyList<(double X, double Y)> blockHole = Assert.Single(roadArea.InteriorRingsMetres);
        Assert.Empty(warnings);
        Assert.Equal(900d, ComputeArea(roadArea.ExteriorRingMetres), 6);
        Assert.Equal(100d, ComputeArea(blockHole), 6);
    }

    [Fact]
    public void DissolveRoads_returns_no_fills_for_non_road_layers()
    {
        PlateauContextOutlinesDxfWriter.OutlineFeature firstBuilding = BuildSquare(BuildingLayer, 0d, 0d, 10d, "first-building");
        PlateauContextOutlinesDxfWriter.OutlineFeature secondBuilding = BuildSquare(BuildingLayer, 10d, 0d, 10d, "second-building");
        List<string> warnings = new List<string>();

        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = PlateauRoadOutlineCleaner.DissolveRoads(
            new[] { firstBuilding, secondBuilding },
            warnings);

        Assert.Empty(warnings);
        Assert.Empty(roadAreas);
    }

    [Fact]
    public void DissolveRoads_removes_shared_road_edges_when_endpoint_coordinates_are_within_snap_tolerance()
    {
        PlateauContextOutlinesDxfWriter.OutlineFeature leftRoad = new PlateauContextOutlinesDxfWriter.OutlineFeature(
            RoadLayer,
            new (double X, double Y)[]
            {
                (0d, 0d),
                (10d, 0d),
                (10d, 10d),
                (0d, 10d)
            },
            "left-road");
        PlateauContextOutlinesDxfWriter.OutlineFeature rightRoad = new PlateauContextOutlinesDxfWriter.OutlineFeature(
            RoadLayer,
            new (double X, double Y)[]
            {
                (10.004d, 0.003d),
                (20d, 0d),
                (20d, 10d),
                (10.003d, 9.996d)
            },
            "right-road");
        List<string> warnings = new List<string>();

        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = PlateauRoadOutlineCleaner.DissolveRoads(
            new[] { leftRoad, rightRoad },
            warnings);

        PlateauContextOutlinesDxfWriter.AreaFeature roadArea = Assert.Single(roadAreas);
        Assert.Empty(warnings);
        Assert.Equal(200d, ComputeArea(roadArea.ExteriorRingMetres), 6);
        Assert.False(ContainsSegmentNearVertical(roadArea.ExteriorRingMetres, 10d, 0d, 10d, 0.01d));
    }

    [Fact]
    public void DissolveRoads_skips_degenerate_road_outlines_and_warns()
    {
        PlateauContextOutlinesDxfWriter.OutlineFeature valid = BuildSquare(RoadLayer, 0d, 0d, 10d, "valid-road");
        PlateauContextOutlinesDxfWriter.OutlineFeature degenerate = new PlateauContextOutlinesDxfWriter.OutlineFeature(
            RoadLayer,
            new (double X, double Y)[] { (10d, 0d), (20d, 0d) },
            "degenerate-road");
        List<string> warnings = new List<string>();

        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = PlateauRoadOutlineCleaner.DissolveRoads(
            new[] { valid, degenerate },
            warnings);

        PlateauContextOutlinesDxfWriter.AreaFeature roadArea = Assert.Single(roadAreas);
        Assert.Equal(100d, ComputeArea(roadArea.ExteriorRingMetres), 6);
        Assert.Single(warnings);
        Assert.Contains("Skipped 1", warnings[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("road sub-faces", warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DissolveRoads_aggregates_multiple_skipped_sub_faces_into_a_single_summary_warning()
    {
        PlateauContextOutlinesDxfWriter.OutlineFeature valid = BuildSquare(RoadLayer, 0d, 0d, 10d, "valid-road");
        PlateauContextOutlinesDxfWriter.OutlineFeature tooFewVertices = new PlateauContextOutlinesDxfWriter.OutlineFeature(
            RoadLayer,
            new (double X, double Y)[] { (10d, 0d), (20d, 0d) },
            "too-few-vertices");
        PlateauContextOutlinesDxfWriter.OutlineFeature sliver = new PlateauContextOutlinesDxfWriter.OutlineFeature(
            RoadLayer,
            new (double X, double Y)[] { (0d, 0d), (10d, 0d), (20d, 0.0001d) },
            "collinear-sliver");
        PlateauContextOutlinesDxfWriter.OutlineFeature degenerateTriangle = new PlateauContextOutlinesDxfWriter.OutlineFeature(
            RoadLayer,
            new (double X, double Y)[] { (0d, 0d), (0.001d, 0d), (0.002d, 0d) },
            "zero-area-triangle");
        List<string> warnings = new List<string>();

        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = PlateauRoadOutlineCleaner.DissolveRoads(
            new[] { valid, tooFewVertices, sliver, degenerateTriangle },
            warnings);

        PlateauContextOutlinesDxfWriter.AreaFeature roadArea = Assert.Single(roadAreas);
        Assert.Equal(100d, ComputeArea(roadArea.ExteriorRingMetres), 6);
        string summary = Assert.Single(warnings);
        Assert.Contains("Skipped 3", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("road sub-faces", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DissolveRoads_bridges_centimeter_scale_gap_between_adjacent_roads()
    {
        PlateauContextOutlinesDxfWriter.OutlineFeature leftRoad = BuildRectangle(RoadLayer, 0d, 0d, 10d, 10d, "left-road");
        PlateauContextOutlinesDxfWriter.OutlineFeature rightRoad = BuildRectangle(RoadLayer, 10.07d, 0d, 20d, 10d, "right-road");
        List<string> warnings = new List<string>();

        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = PlateauRoadOutlineCleaner.DissolveRoads(
            new[] { leftRoad, rightRoad },
            warnings);

        PlateauContextOutlinesDxfWriter.AreaFeature roadArea = Assert.Single(roadAreas);
        Assert.Empty(warnings);
        Assert.Equal(RoadLayer, roadArea.Layer);
        Assert.Empty(roadArea.InteriorRingsMetres);
        Assert.False(ContainsSegmentNearVertical(roadArea.ExteriorRingMetres, 10d, 0d, 10d, 0.05d));
        Assert.False(ContainsSegmentNearVertical(roadArea.ExteriorRingMetres, 10.07d, 0d, 10d, 0.05d));
    }

    [Fact]
    public void DissolveRoads_does_not_bridge_gaps_larger_than_the_bridging_distance()
    {
        PlateauContextOutlinesDxfWriter.OutlineFeature leftRoad = BuildRectangle(RoadLayer, 0d, 0d, 10d, 10d, "left-road");
        PlateauContextOutlinesDxfWriter.OutlineFeature rightRoad = BuildRectangle(RoadLayer, 11d, 0d, 21d, 10d, "right-road");
        List<string> warnings = new List<string>();

        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = PlateauRoadOutlineCleaner.DissolveRoads(
            new[] { leftRoad, rightRoad },
            warnings);

        Assert.Empty(warnings);
        Assert.Equal(2, roadAreas.Count);
        Assert.All(roadAreas, roadArea => Assert.Equal(100d, ComputeArea(roadArea.ExteriorRingMetres), 6));
    }

    [Fact]
    public void Clean_still_leaves_non_road_layers_unchanged_for_legacy_callers()
    {
        PlateauContextOutlinesDxfWriter.OutlineFeature firstBuilding = BuildSquare(BuildingLayer, 0d, 0d, 10d, "first-building");
        PlateauContextOutlinesDxfWriter.OutlineFeature secondBuilding = BuildSquare(BuildingLayer, 10d, 0d, 10d, "second-building");
        List<string> warnings = new List<string>();

        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> cleaned = PlateauRoadOutlineCleaner.Clean(
            new[] { firstBuilding, secondBuilding },
            warnings);

        Assert.Empty(warnings);
        Assert.Equal(2, cleaned.Count);
        Assert.Same(firstBuilding, cleaned[0]);
        Assert.Same(secondBuilding, cleaned[1]);
    }

    private static PlateauContextOutlinesDxfWriter.OutlineFeature BuildSquare(string layer, double originX, double originY, double size, string sourceId)
    {
        return BuildRectangle(layer, originX, originY, originX + size, originY + size, sourceId);
    }

    private static PlateauContextOutlinesDxfWriter.OutlineFeature BuildRectangle(
        string layer,
        double minX,
        double minY,
        double maxX,
        double maxY,
        string sourceId)
    {
        return new PlateauContextOutlinesDxfWriter.OutlineFeature(
            layer,
            new (double X, double Y)[]
            {
                (minX, minY),
                (maxX, minY),
                (maxX, maxY),
                (minX, maxY)
            },
            sourceId);
    }

    private static double ComputeArea(IReadOnlyList<(double X, double Y)> points)
    {
        double areaTwice = 0d;
        for (int index = 0; index < points.Count; index++)
        {
            (double X, double Y) current = points[index];
            (double X, double Y) next = points[(index + 1) % points.Count];
            areaTwice += (current.X * next.Y) - (next.X * current.Y);
        }

        return Math.Abs(areaTwice) * 0.5d;
    }

    private static bool ContainsSegmentNearVertical(
        IReadOnlyList<(double X, double Y)> ring,
        double x,
        double minY,
        double maxY,
        double tolerance)
    {
        double low = Math.Min(minY, maxY);
        double high = Math.Max(minY, maxY);
        for (int index = 0; index < ring.Count; index++)
        {
            (double X, double Y) current = ring[index];
            (double X, double Y) next = ring[(index + 1) % ring.Count];
            bool bothNearX = Math.Abs(current.X - x) <= tolerance && Math.Abs(next.X - x) <= tolerance;
            bool spansY = Math.Abs(Math.Min(current.Y, next.Y) - low) <= tolerance
                && Math.Abs(Math.Max(current.Y, next.Y) - high) <= tolerance;
            if (bothNearX && spansY)
            {
                return true;
            }
        }

        return false;
    }
}
