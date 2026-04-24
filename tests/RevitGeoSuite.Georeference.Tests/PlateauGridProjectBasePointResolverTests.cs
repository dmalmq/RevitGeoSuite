using System;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Georeference;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class PlateauGridProjectBasePointResolverTests
{
    [Fact]
    public void Resolve_uses_southwest_corner_of_sparse_selection_extent()
    {
        JapanMeshCalculator meshCalculator = new JapanMeshCalculator();
        PlateauGridCandidateIndex candidateIndex = new PlateauGridCandidateIndex(meshCalculator);
        PlateauGridProjectBasePointResolver resolver = new PlateauGridProjectBasePointResolver(meshCalculator, CreateTransformer());

        PlateauGridCandidate[] sparseSelection = SelectSparsePair(candidateIndex.GetCandidateGrids(35.681236, 139.767125).ToArray(), meshCalculator, out double expectedSouthLatitude, out double expectedWestLongitude);
        MeshBounds[] selectedBounds = sparseSelection
            .Select(candidate => meshCalculator.GetBounds(new MeshCode { Value = candidate.TileId }))
            .ToArray();

        PlateauGridProjectBasePointSelection? result = resolver.Resolve(sparseSelection.Select(candidate => candidate.TileId).ToArray(), null);

        Assert.NotNull(result);
        Assert.Equal(expectedSouthLatitude, result!.AnchorLatitude, 10);
        Assert.Equal(expectedWestLongitude, result.AnchorLongitude, 10);
        Assert.Null(result.ProjectedCoordinate);
        Assert.DoesNotContain(
            selectedBounds,
            bounds => AreClose(bounds.SouthLatitude, expectedSouthLatitude) && AreClose(bounds.WestLongitude, expectedWestLongitude));
    }

    [Fact]
    public void Resolve_projects_southwest_corner_into_selected_crs()
    {
        JapanMeshCalculator meshCalculator = new JapanMeshCalculator();
        PlateauGridCandidateIndex candidateIndex = new PlateauGridCandidateIndex(meshCalculator);
        CoordinateTransformer transformer = CreateTransformer();
        PlateauGridProjectBasePointResolver resolver = new PlateauGridProjectBasePointResolver(meshCalculator, transformer);
        CrsRegistry registry = new CrsRegistry();
        Assert.True(registry.TryGetByEpsgCode(6677, out CrsDefinition? crsDefinition));

        PlateauGridCandidate[] sparseSelection = SelectSparsePair(candidateIndex.GetCandidateGrids(35.681236, 139.767125).ToArray(), meshCalculator, out double expectedSouthLatitude, out double expectedWestLongitude);

        PlateauGridProjectBasePointSelection? result = resolver.Resolve(
            sparseSelection.Select(candidate => candidate.TileId).ToArray(),
            crsDefinition!.ToReference());

        Assert.NotNull(result);
        Assert.True(result!.ProjectedCoordinate.HasValue);

        ProjectedCoordinate expectedProjected = transformer.Project(
            new GeographicCoordinate(expectedSouthLatitude, expectedWestLongitude),
            crsDefinition.ToReference());

        Assert.Equal(expectedProjected.Easting, result.ProjectedCoordinate!.Value.Easting, 6);
        Assert.Equal(expectedProjected.Northing, result.ProjectedCoordinate.Value.Northing, 6);
    }

    private static CoordinateTransformer CreateTransformer()
    {
        return new CoordinateTransformer(new CrsRegistry());
    }

    private static PlateauGridCandidate[] SelectSparsePair(
        PlateauGridCandidate[] candidates,
        JapanMeshCalculator meshCalculator,
        out double expectedSouthLatitude,
        out double expectedWestLongitude)
    {
        var withBounds = candidates
            .Select(candidate => (Candidate: candidate, Bounds: meshCalculator.GetBounds(new MeshCode { Value = candidate.TileId })))
            .ToArray();

        expectedWestLongitude = withBounds.Min(candidate => candidate.Bounds.WestLongitude);
        expectedSouthLatitude = withBounds.Min(candidate => candidate.Bounds.SouthLatitude);

        var westEdgeNorth = withBounds
            .Where(candidate => AreClose(candidate.Bounds.WestLongitude, expectedWestLongitude))
            .OrderByDescending(candidate => candidate.Bounds.SouthLatitude)
            .First();
        var southEdgeEast = withBounds
            .Where(candidate => AreClose(candidate.Bounds.SouthLatitude, expectedSouthLatitude))
            .OrderByDescending(candidate => candidate.Bounds.WestLongitude)
            .First();

        Assert.NotEqual(westEdgeNorth.Candidate.TileId, southEdgeEast.Candidate.TileId);
        return new[] { westEdgeNorth.Candidate, southEdgeEast.Candidate };
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) < 1e-9;
    }
}
