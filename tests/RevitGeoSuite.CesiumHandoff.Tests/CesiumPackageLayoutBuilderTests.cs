using System;
using System.Collections.Generic;
using System.IO;
using RevitGeoSuite.CesiumHandoff;
using Xunit;

namespace RevitGeoSuite.CesiumHandoff.Tests;

public sealed class CesiumPackageLayoutBuilderTests : IDisposable
{
    private readonly string _root;

    public CesiumPackageLayoutBuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cesium-handoff-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static CesiumPackageBuildInputs CreateInputs()
    {
        return new CesiumPackageBuildInputs
        {
            BuildingId = "tower",
            BuildingName = "Shinjuku Tower",
            SourceModel = "Tower.rvt",
            DocumentKey = "doc-key",
            GeneratorVersion = "1.0.0",
            ProjectEpsg = 6677,
            CoordinateMode = "SharedCoordinates",
            GisEpsg = 6677,
            AnchorLat = 35.69,
            AnchorLon = 139.70,
            AnchorElevationMeters = 40.2,
            GeoidOffsetMeters = 36.7,
            LevelMap = new List<CesiumPackageLevelMapEntry>
            {
                new() { GisLevelId = "lvl-1", TilesLevelKey = "1f", Name = "1F" },
            },
        };
    }

    [Fact]
    public void CreateLayout_CreatesTilesAndGisDirectories()
    {
        var builder = new CesiumPackageLayoutBuilder();
        CesiumPackageLayout layout = builder.CreateLayout(_root);

        Assert.True(Directory.Exists(layout.TilesDirectory));
        Assert.True(Directory.Exists(layout.GisDirectory));
        Assert.Equal(Path.Combine(_root, "tiles"), layout.TilesDirectory);
        Assert.Equal(Path.Combine(_root, "gis"), layout.GisDirectory);
        Assert.Equal(Path.Combine(_root, "cesium-package.json"), layout.ManifestPath);
    }

    [Fact]
    public void WriteManifest_ScansArtifactsAndWritesManifest()
    {
        var builder = new CesiumPackageLayoutBuilder();
        CesiumPackageLayout layout = builder.CreateLayout(_root);

        File.WriteAllText(Path.Combine(layout.TilesDirectory, "tileset.json"), "{}");
        File.WriteAllText(Path.Combine(layout.TilesDirectory, "content.glb"), "glb");
        File.WriteAllText(Path.Combine(layout.TilesDirectory, "levels.json"), "{}");
        File.WriteAllText(Path.Combine(layout.GisDirectory, "tower.gpkg"), "gpkg");

        CesiumPackageManifest manifest = builder.WriteManifest(layout, CreateInputs());

        Assert.True(File.Exists(layout.ManifestPath));
        Assert.Equal("tiles/tileset.json", manifest.Tiles!.Tileset);
        Assert.Equal("tiles/levels.json", manifest.Tiles!.Levels);
        CesiumPackageGisArtifact artifact = Assert.Single(manifest.Gis!.Artifacts!);
        Assert.Equal("gis/tower.gpkg", artifact.Path);
        Assert.Equal("geopackage", manifest.Gis!.Format);

        CesiumPackageManifest reloaded = CesiumPackageManifestSerializer.Deserialize(
            File.ReadAllText(layout.ManifestPath));
        Assert.Equal(manifest.PackageId, reloaded.PackageId);
        Assert.Equal("tower", reloaded.Building!.Id);
    }

    [Fact]
    public void WriteManifest_OmitsTilesSectionWhenNoTileset()
    {
        var builder = new CesiumPackageLayoutBuilder();
        CesiumPackageLayout layout = builder.CreateLayout(_root);
        File.WriteAllText(Path.Combine(layout.GisDirectory, "tower.gpkg"), "gpkg");

        CesiumPackageManifest manifest = builder.WriteManifest(layout, CreateInputs());

        Assert.Null(manifest.Tiles);
        Assert.NotNull(manifest.Gis);
    }

    [Fact]
    public void WriteManifest_OmitsGisSectionWhenNoArtifacts()
    {
        var builder = new CesiumPackageLayoutBuilder();
        CesiumPackageLayout layout = builder.CreateLayout(_root);
        File.WriteAllText(Path.Combine(layout.TilesDirectory, "tileset.json"), "{}");

        CesiumPackageManifest manifest = builder.WriteManifest(layout, CreateInputs());

        Assert.NotNull(manifest.Tiles);
        Assert.Null(manifest.Gis);
    }

    [Fact]
    public void WriteManifest_ThrowsWhenPackageIsEmpty()
    {
        var builder = new CesiumPackageLayoutBuilder();
        CesiumPackageLayout layout = builder.CreateLayout(_root);

        Assert.Throws<InvalidOperationException>(() => builder.WriteManifest(layout, CreateInputs()));
    }

    [Fact]
    public void WriteManifest_ListsShapefileArtifactsWithShpFormat()
    {
        var builder = new CesiumPackageLayoutBuilder();
        CesiumPackageLayout layout = builder.CreateLayout(_root);
        File.WriteAllText(Path.Combine(layout.GisDirectory, "unit.shp"), "shp");
        File.WriteAllText(Path.Combine(layout.GisDirectory, "unit.dbf"), "dbf");
        File.WriteAllText(Path.Combine(layout.GisDirectory, "unit.prj"), "prj");

        CesiumPackageManifest manifest = builder.WriteManifest(layout, CreateInputs());

        Assert.Equal("shapefile", manifest.Gis!.Format);
        CesiumPackageGisArtifact artifact = Assert.Single(manifest.Gis!.Artifacts!);
        Assert.Equal("gis/unit.shp", artifact.Path);
    }
}
