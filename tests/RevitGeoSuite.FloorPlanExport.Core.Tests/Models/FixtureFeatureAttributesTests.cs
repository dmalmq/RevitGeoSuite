using System.Collections.Generic;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Models;

public sealed class FixtureFeatureAttributesTests
{
    [Fact]
    public void FromUnit_MapsColumnsAndKeepsSourceKeys()
    {
        Dictionary<string, object?> unit = new()
        {
            ["id"] = "fixture-1",
            [RevitKey.AttributeName] = "model|unique|0",
            ["category"] = "fixture",
            ["name"] = "Bench",
            ["alt_name"] = "ベンチ",
            ["level_id"] = "level-1",
            ["source"] = "revit",
            ["display_point"] = "POINT (1 2)",
            ["source_element_id"] = 1001L,
            ["source_document_key"] = "doc",
            ["source_document_name"] = "Host",
            ["has_persisted_export_id"] = true,
            ["is_linked_source"] = false,
            ["source_link_instance_id"] = null,
            ["source_link_instance_name"] = null,
        };

        Dictionary<string, object?> fixture = FixtureFeatureAttributes.FromUnit(unit);

        Assert.Equal("fixture", fixture["type"]);
        Assert.False(fixture.ContainsKey("category"));
        Assert.Equal("model|unique|0", fixture[RevitKey.AttributeName]);
        Assert.Equal(1001L, fixture["source_element_id"]);
        Assert.False((bool)fixture["is_linked_source"]!);
        Assert.Equal("doc", fixture["source_document_key"]);
    }

    [Fact]
    public void FromUnit_KeepsLinkedFlagWithSourceElementId()
    {
        Dictionary<string, object?> fixture = FixtureFeatureAttributes.FromUnit(new Dictionary<string, object?>
        {
            ["source_element_id"] = 77L,
            ["is_linked_source"] = true,
            ["source_link_instance_id"] = 5L,
        });

        Assert.Equal(77L, fixture["source_element_id"]);
        Assert.True((bool)fixture["is_linked_source"]!);
        Assert.Equal(5L, fixture["source_link_instance_id"]);
    }

    [Fact]
    public void FromUnit_DoesNotCarryUnitCustomFields()
    {
        Dictionary<string, object?> fixture = FixtureFeatureAttributes.FromUnit(new Dictionary<string, object?>
        {
            ["id"] = "fixture-1",
            ["unit_only_field"] = "value",
            ["_schema_issues"] = new List<string> { "unit issue" },
        });

        Assert.False(fixture.ContainsKey("unit_only_field"));
        Assert.False(fixture.ContainsKey("_schema_issues"));
    }
}
