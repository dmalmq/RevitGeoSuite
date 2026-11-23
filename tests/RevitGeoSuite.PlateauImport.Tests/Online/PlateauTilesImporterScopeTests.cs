using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using RevitGeoSuite.PlateauImport.Online;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests.Online;

public sealed class PlateauTilesImporterScopeTests
{
    [Fact]
    public void MatchesReplacementScope_keeps_first_grid_when_importing_second_grid_in_same_area()
    {
        PlateauTilesetModel gridAModel = BuildModel("13101", "bldg", "2", "53394536");
        PlateauTilesetModel gridBModel = BuildModel("13101", "bldg", "2", "53394537");

        string storedJson = PlateauTilesImporter.SerializeMetadata(
            gridAModel,
            gridAModel.Features[0],
            PlateauOnlineGeometryMode.Lod2Untextured);

        Assert.True(PlateauTilesImporter.TryReadMetadata(storedJson, out PlateauTilesImporter.PlateauOnlineImportMetadata? existing));
        HashSet<string> incomingGrids = PlateauTilesImporter.CollectIncomingGridIds(gridBModel);

        Assert.False(PlateauTilesImporter.MatchesReplacementScope(existing!, gridBModel, incomingGrids));
    }

    [Fact]
    public void MatchesReplacementScope_replaces_exact_matching_area_dataset_lod_grid()
    {
        PlateauTilesetModel gridAModel = BuildModel("13101", "bldg", "2", "53394536");
        PlateauTilesetModel reimportGridA = BuildModel("13101", "bldg", "2", "53394536");

        string storedJson = PlateauTilesImporter.SerializeMetadata(
            gridAModel,
            gridAModel.Features[0],
            PlateauOnlineGeometryMode.Lod2Untextured);

        Assert.True(PlateauTilesImporter.TryReadMetadata(storedJson, out PlateauTilesImporter.PlateauOnlineImportMetadata? existing));
        HashSet<string> incomingGrids = PlateauTilesImporter.CollectIncomingGridIds(reimportGridA);

        Assert.True(PlateauTilesImporter.MatchesReplacementScope(existing!, reimportGridA, incomingGrids));
    }

    [Theory]
    [InlineData("13102", "bldg", "2", "53394536")] // different area
    [InlineData("13101", "brid", "2", "53394536")] // different dataset
    [InlineData("13101", "bldg", "1", "53394536")] // different LOD
    [InlineData("13101", "bldg", "2", "53394538")] // different grid
    public void MatchesReplacementScope_does_not_match_when_any_scope_dimension_differs(
        string incomingArea,
        string incomingDataset,
        string incomingLod,
        string incomingGrid)
    {
        PlateauTilesetModel existingModel = BuildModel("13101", "bldg", "2", "53394536");
        PlateauTilesetModel incomingModel = BuildModel(incomingArea, incomingDataset, incomingLod, incomingGrid);

        string storedJson = PlateauTilesImporter.SerializeMetadata(
            existingModel,
            existingModel.Features[0],
            PlateauOnlineGeometryMode.Lod2Untextured);

        Assert.True(PlateauTilesImporter.TryReadMetadata(storedJson, out PlateauTilesImporter.PlateauOnlineImportMetadata? existing));
        HashSet<string> incomingGrids = PlateauTilesImporter.CollectIncomingGridIds(incomingModel);

        Assert.False(PlateauTilesImporter.MatchesReplacementScope(existing!, incomingModel, incomingGrids));
    }

    [Fact]
    public void MatchesReplacementScope_handles_null_area_code_for_osm_buildings()
    {
        PlateauTilesetModel osmTile1 = BuildModel(areaCode: null, dataset: "OSM Buildings", lod: null, gridId: "tile-abc");
        PlateauTilesetModel osmTile2 = BuildModel(areaCode: null, dataset: "OSM Buildings", lod: null, gridId: "tile-def");

        string storedJson = PlateauTilesImporter.SerializeMetadata(
            osmTile1,
            osmTile1.Features[0],
            PlateauOnlineGeometryMode.Lod2Untextured);

        Assert.Contains("\"area_code\":null", storedJson, StringComparison.Ordinal);
        Assert.True(PlateauTilesImporter.TryReadMetadata(storedJson, out PlateauTilesImporter.PlateauOnlineImportMetadata? existing));

        // Re-importing the same OSM tile replaces it (fixing the old "\"area_code\":\"\"" mismatch).
        Assert.True(PlateauTilesImporter.MatchesReplacementScope(
            existing!,
            osmTile1,
            PlateauTilesImporter.CollectIncomingGridIds(osmTile1)));

        // Importing an adjacent OSM tile keeps the first tile.
        Assert.False(PlateauTilesImporter.MatchesReplacementScope(
            existing!,
            osmTile2,
            PlateauTilesImporter.CollectIncomingGridIds(osmTile2)));
    }

    [Fact]
    public void PartitionIntoBatches_groups_by_grid_and_chunks_large_grids()
    {
        List<PlateauTilesetFeature> features = new List<PlateauTilesetFeature>();
        for (int i = 0; i < 5; i++)
        {
            features.Add(BuildFeature($"b-g2-{i}", "53394537"));
        }
        for (int i = 0; i < 3; i++)
        {
            features.Add(BuildFeature($"b-g1-{i}", "53394536"));
        }

        PlateauTilesetModel model = new PlateauTilesetModel(
            "https://example.test/tileset.json",
            "bldg",
            "2",
            false,
            "13101",
            features);

        IReadOnlyList<PlateauTilesImporter.OnlineImportBatch> batches =
            PlateauTilesImporter.PartitionIntoBatches(model, maxFeaturesPerBatch: 2);

        // Grid 53394536 (3 features -> 2 chunks) then Grid 53394537 (5 features -> 3 chunks).
        Assert.Equal(5, batches.Count);
        Assert.Equal("53394536", batches[0].GridId);
        Assert.Equal(2, batches[0].Features.Count);
        Assert.Equal(1, batches[0].ChunkNumber);
        Assert.Equal(2, batches[0].TotalChunks);

        Assert.Equal("53394536", batches[1].GridId);
        Assert.Single(batches[1].Features);
        Assert.Equal(2, batches[1].ChunkNumber);

        Assert.Equal("53394537", batches[2].GridId);
        Assert.Equal(2, batches[2].Features.Count);
        Assert.Equal(3, batches[2].TotalChunks);
        Assert.Equal(8, batches.Sum(b => b.Features.Count));
    }

    private static PlateauTilesetModel BuildModel(string? areaCode, string dataset, string? lod, string? gridId)
    {
        return new PlateauTilesetModel(
            "https://example.test/tileset.json",
            dataset,
            lod,
            false,
            areaCode,
            new[] { BuildFeature("bldg-1", gridId) });
    }

    private static PlateauTilesetFeature BuildFeature(string id, string? gridId)
    {
        return new PlateauTilesetFeature(
            id,
            new Dictionary<string, object?> { ["feature_type"] = "bldg:Building" },
            new List<PlateauTilesetTriangle>
            {
                new PlateauTilesetTriangle(
                    new Vector3d(0d, 0d, 0d),
                    new Vector3d(1d, 0d, 0d),
                    new Vector3d(0d, 1d, 0d))
            },
            gridId);
    }
}
