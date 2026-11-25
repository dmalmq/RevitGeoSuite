using System.Collections.Generic;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Models;

public sealed class FixtureFeatureAttributesTests
{
    private static Dictionary<string, object?> CreateSampleUnit()
    {
        return new Dictionary<string, object?>
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
    }

    [Fact]
    public void FromUnit_MapsColumnsAndKeepsSourceKeys()
    {
        Dictionary<string, object?> fixture = FixtureFeatureAttributes.FromUnit(CreateSampleUnit());

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

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(2)]
    public void V1AndV2Models_IgnoreGisFixtureTypeAndMatchLegacyOutputByteForByte(int? standardVersion)
    {
        Dictionary<string, object?> unit = CreateSampleUnit();
        Dictionary<string, object?> legacy = FixtureFeatureAttributes.FromUnit(unit);
        Dictionary<string, object?> gated = FixtureFeatureAttributes.FromUnit(unit, "turnstile", standardVersion);

        Assert.Equal(legacy.Count, gated.Count);
        foreach (KeyValuePair<string, object?> entry in legacy)
        {
            Assert.True(gated.ContainsKey(entry.Key));
            Assert.Equal(entry.Value, gated[entry.Key]);
        }

        Assert.Equal("fixture", FixtureFeatureAttributes.ResolveType("kiosk", standardVersion));
    }

    [Fact]
    public void V3_FromUnit_EmitsEveryValidClosedDomainFixtureType()
    {
        Dictionary<string, object?> unit = CreateSampleUnit();
        foreach (string fixtureType in GisStandardDomains.FixtureTypes)
        {
            Dictionary<string, object?> exact = FixtureFeatureAttributes.FromUnit(unit, fixtureType, standardVersion: 3);
            Assert.Equal(fixtureType, exact["type"]);
            Assert.Equal("fixture-1", exact["id"]);
            Assert.Equal("model|unique|0", exact[RevitKey.AttributeName]);
            Assert.Equal(1001L, exact["source_element_id"]);

            Dictionary<string, object?> normalized = FixtureFeatureAttributes.FromUnit(unit, $" {fixtureType.ToUpperInvariant()} ", standardVersion: 3);
            Assert.Equal(fixtureType, normalized["type"]);
        }
    }

    [Theory]
    [InlineData(null, "fixture")]
    [InlineData("", "fixture")]
    [InlineData("   ", "fixture")]
    [InlineData("bench", "fixture")]
    [InlineData("ticket_gate", "fixture")]
    [InlineData("turnstile\n", "fixture")]
    [InlineData("kiosk\r", "fixture")]
    [InlineData("wall\t", "fixture")]
    [InlineData("equipment\u2028", "fixture")]
    [InlineData("furniture\u2029", "fixture")]
    [InlineData(" Turnstile ", "turnstile")]
    [InlineData("OBSTRUCTION", "obstruction")]
    public void V3_ResolveTypeAndFromUnit_FallBackToFixtureForBlankInvalidOrControlCharacterValues(
        string? gisFixtureType,
        string expectedType)
    {
        Dictionary<string, object?> fixture = FixtureFeatureAttributes.FromUnit(CreateSampleUnit(), gisFixtureType, standardVersion: 3);

        Assert.Equal(expectedType, fixture["type"]);
        Assert.Equal(expectedType, FixtureFeatureAttributes.ResolveType(gisFixtureType, standardVersion: 3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(2)]
    public void V1AndV2Models_IgnoreEveryGisFixtureTypeValue(int? standardVersion)
    {
        foreach (string fixtureType in GisStandardDomains.FixtureTypes)
        {
            Assert.Equal("fixture", FixtureFeatureAttributes.FromUnit(CreateSampleUnit(), fixtureType, standardVersion)["type"]);
            Assert.Equal("fixture", FixtureFeatureAttributes.ResolveType(fixtureType, standardVersion));
        }
    }

    [Fact]
    public void V3_BlankOrInvalidFixtureType_KeepsTheUnitsOwnLegacyTypeValue()
    {
        Dictionary<string, object?> unit = CreateSampleUnit();
        unit["category"] = "Fixture";

        Assert.Equal("Fixture", FixtureFeatureAttributes.FromUnit(unit)["type"]);
        Assert.Equal("Fixture", FixtureFeatureAttributes.FromUnit(unit, null, standardVersion: 3)["type"]);
        Assert.Equal("Fixture", FixtureFeatureAttributes.FromUnit(unit, " ", standardVersion: 3)["type"]);
        Assert.Equal("Fixture", FixtureFeatureAttributes.FromUnit(unit, "bench", standardVersion: 3)["type"]);
        Assert.Equal("Fixture", FixtureFeatureAttributes.FromUnit(unit, "kiosk", standardVersion: 2)["type"]);
        Assert.Equal("kiosk", FixtureFeatureAttributes.FromUnit(unit, "kiosk", standardVersion: 3)["type"]);
    }
}
