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

    [Theory]
    [InlineData("Files", "RelativePath")]
    [InlineData("files", "relativePath")]
    public void ComposeFromFolder_HashIncludesPackageManifestReferences(
        string filesProperty,
        string relativePathProperty)
    {
        File.WriteAllText(Path.Combine(_root, "unit.gpkg"), "gpkg");
        File.WriteAllText(Path.Combine(_root, "preview.png"), "before");
        File.WriteAllText(
            Path.Combine(_root, "package-manifest.json"),
            $"{{\"{filesProperty}\":[{{\"{relativePathProperty}\":\"preview.png\"}}]}}");

        string? before = CesiumPackageFolderComposer.ComposeFromFolder(_root, Inputs()).ContentHash;
        File.WriteAllText(Path.Combine(_root, "preview.png"), "after");
        string? after = CesiumPackageFolderComposer.ComposeFromFolder(_root, Inputs()).ContentHash;

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ComposeFromFolder_HashIncludesManifestReferencedShapefileSidecars()
    {
        File.WriteAllText(Path.Combine(_root, "unit.gpkg"), "gpkg");
        File.WriteAllText(Path.Combine(_root, "legacy.shp"), "shp");
        File.WriteAllText(Path.Combine(_root, "legacy.dbf"), "before");
        File.WriteAllText(Path.Combine(_root, "legacy.shx"), "shx");
        File.WriteAllText(
            Path.Combine(_root, "package-manifest.json"),
            "{\"Files\":[{\"RelativePath\":\"legacy.shp\"}]}");

        string? before = CesiumPackageFolderComposer.ComposeFromFolder(_root, Inputs()).ContentHash;
        File.WriteAllText(Path.Combine(_root, "legacy.dbf"), "after");
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

    [Theory]
    [InlineData("20260102T030405Z-abcdef12")]
    [InlineData("20260102T030405Z-abcdef12-2")]
    [InlineData("20260102T030405Z")]
    [InlineData("20260102T030405Z-ABCDEF12")]
    [InlineData("20260102t030405z-abcdef12")]
    public void ComposeFromFolder_LeavesStationVersionManifestUntouched(string versionName)
    {
        string version = Path.Combine(_root, "station", "revit", "tower-12345678", versionName);
        Directory.CreateDirectory(Path.Combine(version, "gis"));
        File.WriteAllText(Path.Combine(version, "gis", "tower.gpkg"), "gpkg");
        string manifestPath = Path.Combine(version, "cesium-package.json");
        string original = CesiumPackageManifestSerializer.Serialize(new CesiumPackageManifest
        {
            PackageId = "published",
            Source = new CesiumPackageSource { ModelId = "model-1" },
            Gis = new CesiumPackageGis
            {
                Format = "geopackage",
                Artifacts = new() { new CesiumPackageGisArtifact { Path = "gis/tower.gpkg" } },
            },
        });
        File.WriteAllText(manifestPath, original);

        CesiumPackageManifest manifest = CesiumPackageFolderComposer.ComposeFromFolder(version, Inputs());

        Assert.Equal("published", manifest.PackageId);
        Assert.Equal("model-1", manifest.Source!.ModelId);
        Assert.Equal(original, File.ReadAllText(manifestPath));
    }

    [Fact]
    public void ComposeFromFolder_RefusesStationVersionWithoutManifest()
    {
        string version = Path.Combine(_root, "station", "revit", "tower-12345678", "20260102T030405Z-abcdef12");
        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "tower.gpkg"), "gpkg");

        Assert.Throws<InvalidOperationException>(() => CesiumPackageFolderComposer.ComposeFromFolder(version, Inputs()));
        Assert.False(File.Exists(Path.Combine(version, "cesium-package.json")));
    }

    [Theory]
    [InlineData("gis")]
    [InlineData("tiles")]
    public void ComposeFromFolder_RefusesSubfolderOfStationVersion(string subfolder)
    {
        string version = Path.Combine(_root, "station", "revit", "tower-12345678", "20260102T030405Z-abcdef12");
        string inside = Path.Combine(version, subfolder);
        Directory.CreateDirectory(inside);
        File.WriteAllText(Path.Combine(inside, "tower.gpkg"), "gpkg");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CesiumPackageFolderComposer.ComposeFromFolder(inside, Inputs()));

        Assert.Contains(version, exception.Message);
        Assert.False(File.Exists(Path.Combine(inside, "cesium-package.json")));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData("/")]
    public void VersionFolderChecks_TreatFilesystemRootsAsOrdinaryFolders(string root)
    {
        Assert.False(CesiumStationFolder.IsVersionFolder(root));
        Assert.Null(CesiumStationFolder.FindEnclosingVersionFolder(root));
    }

    [Fact]
    public void ComposeFromFolder_RewritesTimestampNamedFolderOutsideStation()
    {
        string folder = Path.Combine(_root, "exports", "20260102T030405Z-abcdef12");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "tower.gpkg"), "gpkg");
        File.WriteAllText(Path.Combine(folder, "cesium-package.json"), "stale");

        CesiumPackageManifest manifest = CesiumPackageFolderComposer.ComposeFromFolder(folder, Inputs());

        Assert.Equal("tower.gpkg", Assert.Single(manifest.Gis!.Artifacts!).Path);
        Assert.NotEqual("stale", File.ReadAllText(Path.Combine(folder, "cesium-package.json")));
    }

    [Fact]
    public void ComposeFromFolder_EmptyFolderThrows()
    {
        Assert.Throws<InvalidOperationException>(() => CesiumPackageFolderComposer.ComposeFromFolder(_root, Inputs()));
    }


    [Fact]
    public void ComposeFromFolder_DescribesTheElementAndRoomIndexesBesideTheTileset()
    {
        File.WriteAllText(Path.Combine(_root, "tileset.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "elements.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "rooms.json"), "{}");

        CesiumPackageManifest manifest = CesiumPackageFolderComposer.ComposeFromFolder(_root, Inputs());
        File.WriteAllText(Path.Combine(_root, "rooms.json"), "{\"rooms\":[]}");
        string? after = CesiumPackageFolderComposer.ComposeFromFolder(_root, Inputs()).ContentHash;

        Assert.Equal("elements.json", manifest.Tiles!.Elements);
        Assert.Equal("rooms.json", manifest.Tiles!.Rooms);
        Assert.Null(manifest.Tiles!.Levels);
        Assert.NotEqual(manifest.ContentHash, after);
    }

    [Fact]
    public void ComposeFromFolder_HashIncludesTheFieldListBesideTheGeoPackage()
    {
        Directory.CreateDirectory(Path.Combine(_root, "gis"));
        File.WriteAllText(Path.Combine(_root, "gis", "tower.gpkg"), "gpkg");
        File.WriteAllText(Path.Combine(_root, "gis", "gis-fields.json"), "{}");

        string? before = CesiumPackageFolderComposer.ComposeFromFolder(_root, Inputs()).ContentHash;
        File.WriteAllText(Path.Combine(_root, "gis", "gis-fields.json"), "{\"fields\":[]}");
        string? after = CesiumPackageFolderComposer.ComposeFromFolder(_root, Inputs()).ContentHash;

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ComposeFromFolder_WritesTheModelIdItIsGiven()
    {
        File.WriteAllText(Path.Combine(_root, "tower.gpkg"), "gpkg");
        CesiumPackageBuildInputs inputs = Inputs();
        inputs.ModelId = "8d7f3c1e-2b4a-4f1e-9a51-0d3c2e1f4b6a";

        CesiumPackageFolderComposer.ComposeFromFolder(_root, inputs);

        CesiumPackageManifest written = CesiumPackageManifestSerializer.Deserialize(
            File.ReadAllText(Path.Combine(_root, "cesium-package.json")));
        Assert.Equal("8d7f3c1e-2b4a-4f1e-9a51-0d3c2e1f4b6a", written.Source!.ModelId);
    }

    [Fact]
    public void ComposeFromFolder_KeepsTheModelIdAndExportOfTheSameModelsPackage()
    {
        WriteExportedPackage(documentKey: "doc-key");
        CesiumPackageBuildInputs inputs = Inputs();
        inputs.DocumentKey = "DOC-KEY";

        CesiumPackageFolderComposer.ComposeFromFolder(_root, inputs);

        CesiumPackageManifest written = CesiumPackageManifestSerializer.Deserialize(
            File.ReadAllText(Path.Combine(_root, "cesium-package.json")));
        Assert.Equal("8d7f3c1e-2b4a-4f1e-9a51-0d3c2e1f4b6a", written.Source!.ModelId);
        Assert.Equal("fine", written.Export!.LevelOfDetail);
        Assert.Equal("link-1", Assert.Single(written.Export!.Links).UniqueId);
        Assert.Equal("tiles/elements.json", written.Tiles!.Elements);
        Assert.Equal("tiles/rooms.json", written.Tiles!.Rooms);
    }

    [Fact]
    public void ComposeFromFolder_TakesNothingFromAnotherModelsPackage()
    {
        WriteExportedPackage(documentKey: "another-model");
        CesiumPackageBuildInputs inputs = Inputs();
        inputs.DocumentKey = "doc-key";

        CesiumPackageManifest manifest = CesiumPackageFolderComposer.ComposeFromFolder(_root, inputs);

        Assert.Null(manifest.Source!.ModelId);
        Assert.Null(manifest.Export);
    }

    [Fact]
    public void ComposeFromFolder_PreservesExistingManifestModelIdOverIncomingInput()
    {
        WriteExportedPackage(documentKey: "doc-key");
        CesiumPackageBuildInputs inputs = Inputs();
        inputs.DocumentKey = "doc-key";
        inputs.ModelId = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";

        CesiumPackageManifest manifest = CesiumPackageFolderComposer.ComposeFromFolder(_root, inputs);

        Assert.Equal("8d7f3c1e-2b4a-4f1e-9a51-0d3c2e1f4b6a", manifest.Source!.ModelId);
    }

    /// <summary>A package as the combined export leaves it in an ordinary folder: tiles/, gis/ and a full manifest.</summary>
    private void WriteExportedPackage(string documentKey)
    {
        var layout = new CesiumPackageLayout(_root);
        Directory.CreateDirectory(layout.TilesDirectory);
        Directory.CreateDirectory(layout.GisDirectory);
        File.WriteAllText(Path.Combine(layout.TilesDirectory, "tileset.json"), "{}");
        File.WriteAllText(Path.Combine(layout.TilesDirectory, "elements.json"), "{}");
        File.WriteAllText(Path.Combine(layout.TilesDirectory, "rooms.json"), "{}");
        File.WriteAllText(Path.Combine(layout.GisDirectory, "tower.gpkg"), "gpkg");
        new CesiumPackageLayoutBuilder().WriteManifest(layout, new CesiumPackageBuildInputs
        {
            BuildingId = "tower-1234",
            BuildingName = "Tower",
            GeneratorVersion = "1.0.0",
            DocumentKey = documentKey,
            ModelId = "8d7f3c1e-2b4a-4f1e-9a51-0d3c2e1f4b6a",
            Export = new CesiumPackageExport
            {
                Scope = CesiumExportScopes.Whole,
                LevelOfDetail = "fine",
                ExactPlacement = true,
                Links = new() { new CesiumPackageExportItem { UniqueId = "link-1", Name = "Link.rvt : 1" } },
            },
        });
    }
}
