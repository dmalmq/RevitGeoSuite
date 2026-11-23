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
        PlateauTilesetModel gridAModel = BuildModel("13101", "bldg", "2", "53394536", "bldg-grid-a");
        PlateauTilesetModel gridBModel = BuildModel("13101", "bldg", "2", "53394537", "bldg-grid-b");

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
        PlateauTilesetModel gridAModel = BuildModel("13101", "bldg", "2", "53394536", "bldg-grid-a");
        PlateauTilesetModel reimportGridA = BuildModel("13101", "bldg", "2", "53394536", "bldg-grid-a-new");

        string storedJson = PlateauTilesImporter.SerializeMetadata(
            gridAModel,
            gridAModel.Features[0],
            PlateauOnlineGeometryMode.Lod2Untextured);

        Assert.True(PlateauTilesImporter.TryReadMetadata(storedJson, out PlateauTilesImporter.PlateauOnlineImportMetadata? existing));
        HashSet<string> incomingGrids = PlateauTilesImporter.CollectIncomingGridIds(reimportGridA);

        Assert.True(PlateauTilesImporter.MatchesReplacementScope(existing!, reimportGridA, incomingGrids));
    }

    [Theory]
    [InlineData("13102", "bldg", "2", "53394536", "bldg-1")] // different area
    [InlineData("13101", "brid", "2", "53394536", "bldg-1")] // different dataset
    [InlineData("13101", "bldg", "1", "53394536", "bldg-1")] // different LOD
    [InlineData("13101", "bldg", "2", "53394538", "bldg-2")] // different grid & building
    public void MatchesReplacementScope_does_not_match_when_any_scope_dimension_differs(
        string incomingArea,
        string incomingDataset,
        string incomingLod,
        string incomingGrid,
        string incomingFeatureId)
    {
        PlateauTilesetModel existingModel = BuildModel("13101", "bldg", "2", "53394536", "bldg-1");
        PlateauTilesetModel incomingModel = BuildModel(incomingArea, incomingDataset, incomingLod, incomingGrid, incomingFeatureId);

        string storedJson = PlateauTilesImporter.SerializeMetadata(
            existingModel,
            existingModel.Features[0],
            PlateauOnlineGeometryMode.Lod2Untextured);

        Assert.True(PlateauTilesImporter.TryReadMetadata(storedJson, out PlateauTilesImporter.PlateauOnlineImportMetadata? existing));
        HashSet<string> incomingGrids = PlateauTilesImporter.CollectIncomingGridIds(incomingModel);

        Assert.False(PlateauTilesImporter.MatchesReplacementScope(existing!, incomingModel, incomingGrids));
    }

    [Fact]
    public void MatchesReplacementScope_replaces_legacy_shapes_without_grid_id_in_matching_scope()
    {
        // Legacy DirectShape JSON written by main (no "dataset" and no "grid_id").
        const string legacyJson =
            "{\"gml_id\":\"bldg-old\",\"feature_type\":\"bldg:Building\",\"source_url\":\"https://example.test/tileset.json\",\"area_code\":\"13101\",\"lod\":\"2\",\"mode\":\"Lod2Untextured\"}";

        Assert.True(PlateauTilesImporter.TryReadMetadata(legacyJson, out PlateauTilesImporter.PlateauOnlineImportMetadata? legacy));

        PlateauTilesetModel sameScopeGridImport = BuildModel("13101", "bldg", "2", "53394536", "bldg-new");
        Assert.True(PlateauTilesImporter.MatchesReplacementScope(
            legacy!,
            sameScopeGridImport,
            PlateauTilesImporter.CollectIncomingGridIds(sameScopeGridImport)));

        // Legacy shapes from a different area, LOD, or source URL are still preserved.
        PlateauTilesetModel differentArea = BuildModel("13102", "bldg", "2", "53394536", "bldg-new");
        Assert.False(PlateauTilesImporter.MatchesReplacementScope(
            legacy!,
            differentArea,
            PlateauTilesImporter.CollectIncomingGridIds(differentArea)));

        PlateauTilesetModel differentSource = new PlateauTilesetModel(
            "https://example.test/other-dataset/tileset.json",
            "brid",
            "2",
            false,
            "13101",
            new[] { BuildFeature("brid-1", "53394536") });
        Assert.False(PlateauTilesImporter.MatchesReplacementScope(
            legacy!,
            differentSource,
            PlateauTilesImporter.CollectIncomingGridIds(differentSource)));
    }

    [Fact]
    public void MatchesReplacementScope_deduplicates_straddling_building_across_grids_without_deleting_unrelated_grid_a_buildings()
    {
        PlateauTilesetModel gridAModel = new PlateauTilesetModel(
            "https://example.test/tileset.json",
            "bldg",
            "2",
            false,
            "13101",
            new[]
            {
                BuildFeature("bldg-a-only", "53394536"),
                BuildFeature("bldg-straddle", "53394536")
            });

        PlateauTilesetModel gridBModel = new PlateauTilesetModel(
            "https://example.test/tileset.json",
            "bldg",
            "2",
            false,
            "13101",
            new[]
            {
                BuildFeature("bldg-b-only", "53394537"),
                BuildFeature("bldg-straddle", "53394537")
            });

        string storedAOnlyJson = PlateauTilesImporter.SerializeMetadata(gridAModel, gridAModel.Features[0], PlateauOnlineGeometryMode.Lod2Untextured);
        string storedStraddleJson = PlateauTilesImporter.SerializeMetadata(gridAModel, gridAModel.Features[1], PlateauOnlineGeometryMode.Lod2Untextured);

        Assert.True(PlateauTilesImporter.TryReadMetadata(storedAOnlyJson, out PlateauTilesImporter.PlateauOnlineImportMetadata? existingAOnly));
        Assert.True(PlateauTilesImporter.TryReadMetadata(storedStraddleJson, out PlateauTilesImporter.PlateauOnlineImportMetadata? existingStraddle));

        HashSet<string> gridBGrids = PlateauTilesImporter.CollectIncomingGridIds(gridBModel);
        HashSet<string> gridBFeatures = PlateauTilesImporter.CollectIncomingFeatureIds(gridBModel);

        // Straddling building stored under grid A must be replaced when grid B re-imports it.
        Assert.True(PlateauTilesImporter.MatchesReplacementScope(existingStraddle!, gridBModel, gridBGrids, gridBFeatures));

        // Unrelated buildings in grid A must remain untouched.
        Assert.False(PlateauTilesImporter.MatchesReplacementScope(existingAOnly!, gridBModel, gridBGrids, gridBFeatures));
    }

    [Fact]
    public void MatchesReplacementScope_handles_null_area_code_for_osm_buildings()
    {
        PlateauTilesetModel osmTile1 = BuildModel(areaCode: null, dataset: "OSM Buildings", lod: null, gridId: "tile-abc", featureId: "osm-1");
        PlateauTilesetModel osmTile2 = BuildModel(areaCode: null, dataset: "OSM Buildings", lod: null, gridId: "tile-def", featureId: "osm-2");

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
    public void MatchesReplacementScope_does_not_mass_delete_legacy_osm_buildings_outside_smaller_reimport()
    {
        const string legacyOsmFarJson =
            "{\"gml_id\":\"osm-far\",\"feature_type\":\"bldg:Building\",\"source_url\":\"https://example.test/tileset.json\",\"area_code\":null,\"lod\":null,\"mode\":\"Lod2Untextured\"}";
        const string legacyOsmNearJson =
            "{\"gml_id\":\"osm-near\",\"feature_type\":\"bldg:Building\",\"source_url\":\"https://example.test/tileset.json\",\"area_code\":null,\"lod\":null,\"mode\":\"Lod2Untextured\"}";

        Assert.True(PlateauTilesImporter.TryReadMetadata(legacyOsmFarJson, out PlateauTilesImporter.PlateauOnlineImportMetadata? legacyFar));
        Assert.True(PlateauTilesImporter.TryReadMetadata(legacyOsmNearJson, out PlateauTilesImporter.PlateauOnlineImportMetadata? legacyNear));

        PlateauTilesetModel smallReimport = BuildModel(areaCode: null, dataset: "OSM Buildings", lod: null, gridId: "53394536", featureId: "osm-near");
        HashSet<string> grids = PlateauTilesImporter.CollectIncomingGridIds(smallReimport);
        HashSet<string> features = PlateauTilesImporter.CollectIncomingFeatureIds(smallReimport);

        // Legacy OSM building outside the smaller bounding box is preserved.
        Assert.False(PlateauTilesImporter.MatchesReplacementScope(legacyFar!, smallReimport, grids, features));

        // Legacy OSM building whose feature ID matches the incoming re-import IS replaced.
        Assert.True(PlateauTilesImporter.MatchesReplacementScope(legacyNear!, smallReimport, grids, features));
    }

    [Fact]
    public void MatchesReplacementScope_does_not_delete_other_osm_buildings_in_same_1km_mesh_on_partial_bbox_reimport()
    {
        PlateauTilesetModel existingMeshModel = new PlateauTilesetModel(
            "https://example.test/tileset.json",
            "OSM Buildings",
            null,
            false,
            null,
            new[]
            {
                BuildFeature("osm-outside-bbox", "53394536"),
                BuildFeature("osm-inside-bbox", "53394536")
            });

        string storedOutsideJson = PlateauTilesImporter.SerializeMetadata(
            existingMeshModel,
            existingMeshModel.Features[0],
            PlateauOnlineGeometryMode.Lod2Untextured);
        string storedInsideJson = PlateauTilesImporter.SerializeMetadata(
            existingMeshModel,
            existingMeshModel.Features[1],
            PlateauOnlineGeometryMode.Lod2Untextured);

        Assert.True(PlateauTilesImporter.TryReadMetadata(storedOutsideJson, out PlateauTilesImporter.PlateauOnlineImportMetadata? existingOutside));
        Assert.True(PlateauTilesImporter.TryReadMetadata(storedInsideJson, out PlateauTilesImporter.PlateauOnlineImportMetadata? existingInside));

        // Partial-mesh OSM re-import covering only "osm-inside-bbox" within the same 1 km mesh "53394536".
        PlateauTilesetModel partialReimport = BuildModel(
            areaCode: null,
            dataset: "OSM Buildings",
            lod: null,
            gridId: "53394536",
            featureId: "osm-inside-bbox");
        HashSet<string> grids = PlateauTilesImporter.CollectIncomingGridIds(partialReimport);
        HashSet<string> features = PlateauTilesImporter.CollectIncomingFeatureIds(partialReimport);

        // Other OSM building in the same 1 km mesh must NOT be deleted.
        Assert.False(PlateauTilesImporter.MatchesReplacementScope(existingOutside!, partialReimport, grids, features));

        // Exact matching OSM building IS replaced.
        Assert.True(PlateauTilesImporter.MatchesReplacementScope(existingInside!, partialReimport, grids, features));
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

    private static PlateauTilesetModel BuildModel(
        string? areaCode,
        string dataset,
        string? lod,
        string? gridId,
        string featureId = "bldg-1")
    {
        return new PlateauTilesetModel(
            "https://example.test/tileset.json",
            dataset,
            lod,
            false,
            areaCode,
            new[] { BuildFeature(featureId, gridId) });
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
