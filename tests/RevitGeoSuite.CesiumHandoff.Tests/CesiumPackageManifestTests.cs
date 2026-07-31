using System;
using System.Collections.Generic;
using RevitGeoSuite.CesiumHandoff;
using Xunit;

namespace RevitGeoSuite.CesiumHandoff.Tests;

public sealed class CesiumPackageManifestTests
{
    private static CesiumPackageManifest CreateSample()
    {
        return new CesiumPackageManifest
        {
            PackageId = "0f8fad5b-d9cb-469f-a165-70867728950e",
            CreatedUtc = new DateTime(2026, 7, 7, 9, 0, 0, DateTimeKind.Utc),
            Generator = new CesiumPackageGenerator { Name = "RevitGeoSuite", Version = "1.0.0" },
            Source = new CesiumPackageSource { Model = "Tower.rvt", DocumentKey = "doc-key" },
            Building = new CesiumPackageBuilding
            {
                Id = "tower",
                Name = "Shinjuku Tower",
                Aliases = new List<string> { "新宿タワー" },
            },
            Crs = new CesiumPackageCrs { ProjectEpsg = 6677, CoordinateMode = "SharedCoordinates", GisEpsg = 6677 },
            Anchor = new CesiumPackageAnchor
            {
                Lat = 35.690123,
                Lon = 139.700456,
                ElevationMeters = 40.2,
                GeoidOffsetMeters = 36.7,
            },
            Tiles = new CesiumPackageTiles { Tileset = "tiles/tileset.json", Levels = "tiles/levels.json" },
            Gis = new CesiumPackageGis
            {
                Format = "geopackage",
                Artifacts = new List<CesiumPackageGisArtifact>
                {
                    new() { Path = "gis/tower.gpkg", Layers = new List<string> { "unit", "level" } },
                },
            },
            LevelMap = new List<CesiumPackageLevelMapEntry>
            {
                new() { GisLevelId = "lvl-guid-1", TilesLevelKey = "1f", Name = "1F" },
            },
        };
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        CesiumPackageManifest original = CreateSample();
        string json = CesiumPackageManifestSerializer.Serialize(original);
        CesiumPackageManifest parsed = CesiumPackageManifestSerializer.Deserialize(json);

        Assert.Equal("revitgeosuite.cesium-package", parsed.Schema);
        Assert.Equal(1, parsed.Version);
        Assert.Equal(original.PackageId, parsed.PackageId);
        Assert.Equal(original.CreatedUtc, parsed.CreatedUtc);
        Assert.Equal("Shinjuku Tower", parsed.Building!.Name);
        Assert.Equal("新宿タワー", Assert.Single(parsed.Building!.Aliases!));
        Assert.Equal(6677, parsed.Crs!.ProjectEpsg);
        Assert.Equal(35.690123, parsed.Anchor!.Lat, 9);
        Assert.Equal("tiles/tileset.json", parsed.Tiles!.Tileset);
        Assert.Equal("gis/tower.gpkg", Assert.Single(parsed.Gis!.Artifacts!).Path);
        CesiumPackageLevelMapEntry entry = Assert.Single(parsed.LevelMap!);
        Assert.Equal("lvl-guid-1", entry.GisLevelId);
        Assert.Equal("1f", entry.TilesLevelKey);
    }

    [Fact]
    public void Serialize_UsesCamelCasePropertyNames()
    {
        string json = CesiumPackageManifestSerializer.Serialize(CreateSample());
        Assert.Contains("\"packageId\"", json);
        Assert.Contains("\"levelMap\"", json);
        Assert.Contains("\"gisLevelId\"", json);
        Assert.DoesNotContain("\"PackageId\"", json);
    }

    [Fact]
    public void TilesAndGisSections_AreIndependentlyOptional()
    {
        CesiumPackageManifest tilesOnly = CreateSample();
        tilesOnly.Gis = null;
        CesiumPackageManifest parsed = CesiumPackageManifestSerializer.Deserialize(
            CesiumPackageManifestSerializer.Serialize(tilesOnly));
        Assert.Null(parsed.Gis);
        Assert.NotNull(parsed.Tiles);

        CesiumPackageManifest gisOnly = CreateSample();
        gisOnly.Tiles = null;
        parsed = CesiumPackageManifestSerializer.Deserialize(
            CesiumPackageManifestSerializer.Serialize(gisOnly));
        Assert.Null(parsed.Tiles);
        Assert.NotNull(parsed.Gis);
    }

    [Fact]
    public void Deserialize_RejectsWrongSchema()
    {
        Assert.Throws<InvalidOperationException>(
            () => CesiumPackageManifestSerializer.Deserialize("{\"schema\":\"something-else\",\"version\":1}"));
    }

    [Fact]
    public void Deserialize_RejectsNewerVersion()
    {
        Assert.Throws<InvalidOperationException>(
            () => CesiumPackageManifestSerializer.Deserialize(
                "{\"schema\":\"revitgeosuite.cesium-package\",\"version\":99}"));
    }
}
