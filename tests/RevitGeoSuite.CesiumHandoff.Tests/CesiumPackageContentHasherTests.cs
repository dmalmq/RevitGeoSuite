using System;
using System.IO;
using RevitGeoSuite.CesiumHandoff;
using Xunit;

namespace RevitGeoSuite.CesiumHandoff.Tests;

public sealed class CesiumPackageContentHasherTests : IDisposable
{
    private readonly string _root;

    public CesiumPackageContentHasherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cesium-hash-tests", Guid.NewGuid().ToString("N"));
        SeedPackageContent();
    }

    /// <summary>
    /// Writes the payload a package manifest can describe. Call this again after
    /// <see cref="CesiumPackageLayoutBuilder.CreateLayout"/>, which clears tiles/ and gis/.
    /// </summary>
    private void SeedPackageContent()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tiles"));
        Directory.CreateDirectory(Path.Combine(_root, "gis"));
        // The tileset must reference content.glb: the payload resolver walks $..content to
        // decide which files the content hash covers, so an empty tileset would leave the
        // glb out of the hash entirely.
        File.WriteAllText(
            Path.Combine(_root, "tiles", "tileset.json"),
            "{\"asset\":{\"version\":\"1.1\"},\"root\":{\"content\":{\"uri\":\"content.glb\"}}}");
        File.WriteAllText(Path.Combine(_root, "tiles", "content.glb"), "GLB-BYTES");
        File.WriteAllText(Path.Combine(_root, "gis", "tower.gpkg"), "GPKG-BYTES");
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

    private string[] RelativePaths => new[] { "tiles/tileset.json", "tiles/content.glb", "gis/tower.gpkg" };

    [Fact]
    public void Compute_IsStableForSameContent()
    {
        string a = CesiumPackageContentHasher.Compute(_root, RelativePaths);
        string b = CesiumPackageContentHasher.Compute(_root, RelativePaths);
        Assert.Equal(a, b);
        Assert.Matches("^[a-f0-9]{64}$", a);
    }

    [Fact]
    public void Compute_IsIndependentOfPathOrder()
    {
        string a = CesiumPackageContentHasher.Compute(_root, RelativePaths);
        string b = CesiumPackageContentHasher.Compute(
            _root, new[] { "gis/tower.gpkg", "tiles/content.glb", "tiles/tileset.json" });
        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_ChangesWhenAFileChanges()
    {
        string before = CesiumPackageContentHasher.Compute(_root, RelativePaths);
        File.WriteAllText(Path.Combine(_root, "tiles", "content.glb"), "GLB-BYTES-CHANGED");
        string after = CesiumPackageContentHasher.Compute(_root, RelativePaths);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Compute_SkipsMissingFilesDeterministically()
    {
        string withMissing = CesiumPackageContentHasher.Compute(
            _root, new[] { "tiles/tileset.json", "tiles/content.glb", "gis/tower.gpkg", "gis/nope.gpkg" });
        string without = CesiumPackageContentHasher.Compute(_root, RelativePaths);
        Assert.Equal(without, withMissing);
    }

    [Fact]
    public void WriteManifest_PopulatesContentHash()
    {
        var builder = new CesiumPackageLayoutBuilder();
        CesiumPackageLayout layout = builder.CreateLayout(_root);
        SeedPackageContent();
        CesiumPackageManifest manifest = builder.WriteManifest(layout, new CesiumPackageBuildInputs
        {
            BuildingId = "tower",
            BuildingName = "Tower",
            GeneratorVersion = "1.0.0",
        });

        Assert.False(string.IsNullOrEmpty(manifest.ContentHash));

        // Same content → same hash on a rewrite; changed content → different.
        CesiumPackageManifest again = builder.WriteManifest(layout, new CesiumPackageBuildInputs
        {
            BuildingId = "tower",
            BuildingName = "Tower",
            GeneratorVersion = "1.0.0",
        });
        Assert.Equal(manifest.ContentHash, again.ContentHash);

        File.WriteAllText(Path.Combine(_root, "tiles", "content.glb"), "DIFFERENT");
        CesiumPackageManifest changed = builder.WriteManifest(layout, new CesiumPackageBuildInputs
        {
            BuildingId = "tower",
            BuildingName = "Tower",
            GeneratorVersion = "1.0.0",
        });
        Assert.NotEqual(manifest.ContentHash, changed.ContentHash);
    }
}
