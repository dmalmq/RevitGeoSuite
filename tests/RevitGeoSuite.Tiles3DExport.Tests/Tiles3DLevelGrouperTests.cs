using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DLevelGrouperTests
{
    private readonly Tiles3DLevelGrouper grouper = new Tiles3DLevelGrouper();

    [Fact]
    public void Empty_input_returns_no_groups()
    {
        IReadOnlyList<Tiles3DLevelGroup> result = grouper.Group(new List<Tiles3DMeshPrimitive>());

        Assert.Empty(result);
    }

    [Fact]
    public void Named_levels_are_sorted_by_elevation_ascending()
    {
        List<Tiles3DMeshPrimitive> meshes = new List<Tiles3DMeshPrimitive>
        {
            Mesh("A", "Second Floor", 3.5d),
            Mesh("B", "Ground Floor", 0d),
            Mesh("C", "First Floor", 3.0d)
        };

        IReadOnlyList<Tiles3DLevelGroup> result = grouper.Group(meshes);

        Assert.Equal(3, result.Count);
        Assert.Equal("Ground Floor", result[0].LevelName);
        Assert.Equal("First Floor", result[1].LevelName);
        Assert.Equal("Second Floor", result[2].LevelName);
    }

    [Fact]
    public void Level_keys_use_sanitized_level_name()
    {
        List<Tiles3DMeshPrimitive> meshes = new List<Tiles3DMeshPrimitive>
        {
            Mesh("A", "Ground Floor", 0d),
            Mesh("B", "First Floor / Level 1", 3.0d)
        };

        IReadOnlyList<Tiles3DLevelGroup> result = grouper.Group(meshes);

        Assert.Equal("ground_floor", result[0].LevelKey);
        Assert.Equal("first_floor_level_1", result[1].LevelKey);
    }

    [Fact]
    public void Unassigned_elements_go_into_last_group_named_unassigned()
    {
        List<Tiles3DMeshPrimitive> meshes = new List<Tiles3DMeshPrimitive>
        {
            Mesh("A", "Ground Floor", 0d),
            Mesh("B", string.Empty, 0d)
        };

        IReadOnlyList<Tiles3DLevelGroup> result = grouper.Group(meshes);

        Assert.Equal(2, result.Count);
        Assert.Equal("Ground Floor", result[0].LevelName);
        Assert.Equal("Unassigned", result[1].LevelName);
        Assert.Equal("unassigned", result[1].LevelKey);
    }

    [Fact]
    public void All_unassigned_elements_produce_single_unassigned_group()
    {
        List<Tiles3DMeshPrimitive> meshes = new List<Tiles3DMeshPrimitive>
        {
            Mesh("A", string.Empty, 0d),
            Mesh("B", string.Empty, 0d)
        };

        IReadOnlyList<Tiles3DLevelGroup> result = grouper.Group(meshes);

        Assert.Single(result);
        Assert.Equal("unassigned", result[0].LevelKey);
        Assert.Equal(2, result[0].Meshes.Count);
    }

    [Fact]
    public void Elements_with_same_level_name_are_grouped_together()
    {
        List<Tiles3DMeshPrimitive> meshes = new List<Tiles3DMeshPrimitive>
        {
            Mesh("A", "Ground Floor", 0d),
            Mesh("B", "Ground Floor", 0d),
            Mesh("C", "First Floor", 3.0d)
        };

        IReadOnlyList<Tiles3DLevelGroup> result = grouper.Group(meshes);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Meshes.Count);
        Assert.Single(result[1].Meshes);
    }

    [Fact]
    public void Level_key_is_sanitized()
    {
        List<Tiles3DMeshPrimitive> meshes = new List<Tiles3DMeshPrimitive>
        {
            Mesh("A", "Level: B1 (Basement/Underground) — Storage & Plant Room", 0d)
        };

        IReadOnlyList<Tiles3DLevelGroup> result = grouper.Group(meshes);

        string levelKey = result[0].LevelKey;
        Assert.StartsWith("level_b1", levelKey);
        Assert.DoesNotContain(":", levelKey);
        Assert.DoesNotContain("/", levelKey);
        Assert.DoesNotContain("—", levelKey);
    }

    private static Tiles3DMeshPrimitive Mesh(string name, string levelName, double elevationMeters)
    {
        return new Tiles3DMeshPrimitive
        {
            Name = name,
            LevelName = levelName,
            LevelElevationMeters = elevationMeters,
            Triangles = new List<Tiles3DTriangle>
            {
                new Tiles3DTriangle(
                    new Tiles3DPoint(0d, 0d, 0d),
                    new Tiles3DPoint(1d, 0d, 0d),
                    new Tiles3DPoint(0d, 1d, 0d))
            }
        };
    }
}
