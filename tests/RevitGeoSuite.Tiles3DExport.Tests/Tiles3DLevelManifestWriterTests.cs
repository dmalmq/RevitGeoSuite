using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DLevelManifestWriterTests
{
    [Fact]
    public void Build_json_writes_sorted_level_manifest_for_viewer_filters()
    {
        Tiles3DExportPackage package = new Tiles3DExportPackage
        {
            Meshes = new List<Tiles3DMeshPrimitive>
            {
                Mesh("Wall 2", "Second Floor", 6d, 6d, 8.4d),
                Mesh("Wall 1", "Ground Floor", 0d, 0d, 2.8d),
                Mesh("Site Object", string.Empty, 0d, -0.5d, 0.2d)
            }
        };

        JObject document = JObject.Parse(new Tiles3DLevelManifestWriter().BuildJson(package));

        Assert.Equal(2, (int?)document["version"]);
        Assert.Equal("tileset.json", (string?)document["tileset"]);
        Assert.Equal("content.glb", (string?)document["content"]);
        JObject linkLevels = Assert.IsType<JObject>(document["linkLevels"]);
        Assert.True(linkLevels.ContainsKey(string.Empty));
        JArray levels = Assert.IsType<JArray>(document["levels"]);
        Assert.Equal(3, levels.Count);
        Assert.Equal("ground_floor", (string?)levels[0]?["levelKey"]);
        Assert.Equal("second_floor", (string?)levels[1]?["levelKey"]);
        Assert.Equal("unassigned", (string?)levels[2]?["levelKey"]);
        Assert.Equal("Unassigned", (string?)levels[2]?["levelName"]);
        Assert.Equal(1, (int?)levels[0]?["elementCount"]);
        Assert.Equal(2.8d, (double?)levels[0]?["maxZMeters"]);
    }

    [Fact]
    public void Build_json_lists_distinct_level_unique_ids_per_level()
    {
        Tiles3DMeshPrimitive host = Mesh("Wall 1", "1F", 0d, 0d, 2.8d);
        host.Metadata.LevelUniqueId = "level-host-1f";
        Tiles3DMeshPrimitive hostAgain = Mesh("Wall 2", "1F", 0d, 0d, 2.8d);
        hostAgain.Metadata.LevelUniqueId = "level-host-1f";
        Tiles3DMeshPrimitive linked = Mesh("Wall 3", "1F", 0d, 0d, 2.8d);
        linked.Metadata.LevelUniqueId = "level-link-1f";
        Tiles3DMeshPrimitive site = Mesh("Site Object", string.Empty, 0d, -0.5d, 0.2d);
        Tiles3DExportPackage package = new Tiles3DExportPackage
        {
            Meshes = new List<Tiles3DMeshPrimitive> { linked, host, hostAgain, site }
        };

        JObject document = JObject.Parse(new Tiles3DLevelManifestWriter().BuildJson(package));

        JArray levels = Assert.IsType<JArray>(document["levels"]);
        JArray firstFloorIds = Assert.IsType<JArray>(levels[0]?["levelUniqueIds"]);
        Assert.Equal(new[] { "level-host-1f", "level-link-1f" }, firstFloorIds.Values<string>());
        JArray unassignedIds = Assert.IsType<JArray>(levels[1]?["levelUniqueIds"]);
        Assert.Empty(unassignedIds);
    }

    private static Tiles3DMeshPrimitive Mesh(string name, string levelName, double elevationMeters, double minZ, double maxZ)
    {
        string resolvedLevelName = string.IsNullOrWhiteSpace(levelName)
            ? Tiles3DLevelMetadata.UnassignedLevelName
            : levelName;

        return new Tiles3DMeshPrimitive
        {
            Metadata = new Tiles3DObjectMetadata
            {
                Name = name,
                LevelName = resolvedLevelName,
                LevelKey = Tiles3DLevelMetadata.BuildLevelKey(resolvedLevelName),
                LevelElevationMeters = elevationMeters,
                MinZMeters = minZ,
                MaxZMeters = maxZ,
                HeightMeters = maxZ - minZ
            },
            Triangles = new List<Tiles3DTriangle>
            {
                new Tiles3DTriangle(
                    new Tiles3DPoint(0d, 0d, minZ),
                    new Tiles3DPoint(1d, 0d, minZ),
                    new Tiles3DPoint(0d, 1d, maxZ))
            }
        };
    }
}
