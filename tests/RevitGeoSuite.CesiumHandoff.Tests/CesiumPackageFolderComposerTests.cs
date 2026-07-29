using System;
using System.IO;
using RevitGeoSuite.CesiumHandoff;
using Xunit;

namespace RevitGeoSuite.CesiumHandoff.Tests;

public sealed class CesiumPackageFolderComposerTests : IDisposable
{
    private readonly string _root;

    public CesiumPackageFolderComposerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cesium-composer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static CesiumPackageBuildInputs Inputs() => new()
    {
        BuildingId = "tower-1234",
        BuildingName = "Tower",
        GeneratorVersion = "1.0.0",
    };

    [Fact]
    public void ComposeFromFolder_TilesOnlyExportAtRoot()
    {
        File.WriteAllText(Path.Combine(_root, "tileset.json"), "{\"root\":{\"content\":{\"uri\":\"content.glb\"}}}");
        File.WriteAllText(Path.Combine(_root, "content.glb"), "glb");
        File.WriteAllText(Path.Combine(_root, "levels.json"), "{}");

        CesiumPackageManifest manifest = CesiumPackageFolderComposer.ComposeFromFolder(_root, Inputs());

        Assert.Equal("tileset.json", manifest.Tiles!.Tileset);
        Assert.Equal("levels.json", manifest.Tiles!.Levels);
        Assert.Null(manifest.Gis);
        Assert.Null(manifest.Crs);
        Assert.Null(manifest.Anchor);
        Assert.True(File.Exists(Path.Combine(_root, "cesium-package.json")));
    }

    [Fact]
    public void ComposeFromFolder_GisOnlyExportWithNestedArtifacts()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "sub", "tower.gpkg"), "gpkg");

        CesiumPackageManifest manifest = CesiumPackageFolderComposer.ComposeFromFolder(_root, Inputs());

        Assert.Null(manifest.Tiles);
        Assert.Equal("geopackage", manifest.Gis!.Format);
        Assert.Equal("sub/tower.gpkg", Assert.Single(manifest.Gis!.Artifacts!).Path);
    }

    [Fact]
    public void ComposeFromFolder_HashIncludesShapefileSidecars()
    {
        File.WriteAllText(Path.Combine(_root, "unit.shp"), "shp");
        File.WriteAllText(Path.Combine(_root, "unit.dbf"), "before");
        File.WriteAllText(Path.Combine(_root, "unit.shx"), "shx");

        string? before = CesiumPackageFolderComposer.ComposeFromFolder(_root, Inputs()).ContentHash;
        File.WriteAllText(Path.Combine(_root, "unit.dbf"), "after");
        string? after = CesiumPackageFolderComposer.ComposeFromFolder(_root, Inputs()).ContentHash;

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ComposeFromFolder_PrefersPackageLayoutSubdirsWhenPresent()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tiles"));
        File.WriteAllText(Path.Combine(_root, "tiles", "tileset.json"), "{}");

        CesiumPackageManifest manifest = CesiumPackageFolderComposer.ComposeFromFolder(_root, Inputs());
        Assert.Equal("tiles/tileset.json", manifest.Tiles!.Tileset);
    }

    [Fact]
    public void ComposeFromFolder_EmptyFolderThrows()
    {
        Assert.Throws<InvalidOperationException>(() => CesiumPackageFolderComposer.ComposeFromFolder(_root, Inputs()));
    }
}
