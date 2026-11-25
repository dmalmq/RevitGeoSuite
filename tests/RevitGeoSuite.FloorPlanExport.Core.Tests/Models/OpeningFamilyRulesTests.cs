using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Models;

public sealed class OpeningFamilyRulesTests
{
    [Theory]
    [InlineData("EV扉")]
    [InlineData("ev扉")]
    public void IsAcceptedElevatorDoorFamilyName_ReturnsTrueForConfiguredFamilies(string familyName)
    {
        Assert.True(OpeningFamilyRules.IsAcceptedElevatorDoorFamilyName(familyName));
    }

    [Fact]
    public void IsAcceptedElevatorDoorFamilyName_ReturnsFalseForOtherFamilies()
    {
        Assert.False(OpeningFamilyRules.IsAcceptedElevatorDoorFamilyName("Generic Door"));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("3", 3)]
    [InlineData(" 3 ", 3)]
    [InlineData("4", 4)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("v3", null)]
    [InlineData("abc", null)]
    [InlineData("3\n", 3)]
    [InlineData("3\r", 3)]
    [InlineData("3\t", 3)]
    [InlineData("3\u0000", 3)]
    [InlineData("3.0", null)]
    public void ParseStandardVersion_MatchesTheStandardCheck(string? input, int? expected)
    {
        Assert.Equal(expected, OpeningFamilyRules.ParseStandardVersion(input));

        GisStandardStatus status = GisStandardCheck.Check(new GisStandardModelState { StandardVersionText = input ?? string.Empty });
        Assert.Equal(expected, status.ModelVersion);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void IsStandardV3OrNewer_GatesStrictlyAtVersionThree(int? standardVersion, bool expected)
    {
        Assert.Equal(expected, OpeningFamilyRules.IsStandardV3OrNewer(standardVersion));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(2)]
    public void V1AndV2Models_PreserveExactLegacyOpeningCategoryAndElevatorBehavior(int? standardVersion)
    {
        // Legacy EV扉 family name still detected as elevator door across all versions.
        Assert.True(OpeningFamilyRules.IsElevatorDoor("EV扉", null, standardVersion));
        Assert.True(OpeningFamilyRules.IsElevatorDoor("ev扉", "sliding", standardVersion));

        // gis_door="elevator" MUST be ignored on v1/v2/uninitialized models for non-EV扉 families.
        Assert.False(OpeningFamilyRules.IsElevatorDoor("j EVドア", "elevator", standardVersion));
        Assert.False(OpeningFamilyRules.IsElevatorDoor("Standard Door", "ELEVATOR", standardVersion));

        // v3 parameters (gis_opening_category, gis_access_con="emergency") MUST be ignored on v1/v2.
        Assert.Equal("pedestrian", OpeningFamilyRules.ResolveOpeningCategory("Standard Door", "service", "emergency", standardVersion));
        Assert.Equal("pedestrian", OpeningFamilyRules.ResolveOpeningCategory("Shutter", "automobile", "emergency", standardVersion));
        Assert.Equal("pedestrian", OpeningFamilyRules.ResolveOpeningCategory("Gate", "pedestrian.transit", null, standardVersion));
        Assert.Equal("pedestrian", OpeningFamilyRules.ResolveOpeningCategory("EV扉", "service", "emergency", standardVersion));
        Assert.Equal("emergencyexit", OpeningFamilyRules.ResolveOpeningCategory("Emergency Exit Door", "pedestrian", "none", standardVersion));
        Assert.Equal("emergencyexit", OpeningFamilyRules.ResolveOpeningCategory("j EMERGENCY Door", "service", null, standardVersion));
    }

    [Theory]
    [InlineData("j EVドア", "elevator", 3, true)]
    [InlineData("Standard Door", "ELEVATOR", 3, true)]
    [InlineData("Standard Door", " Elevator ", 3, true)]
    [InlineData("Standard Door", "elevator", 4, true)]
    [InlineData("EV扉", null, 3, true)]
    [InlineData("EV扉", "sliding", 3, true)]
    [InlineData("Standard Door", "swinging", 3, false)]
    [InlineData("Standard Door", "sliding", 3, false)]
    [InlineData("Standard Door", "automatic", 3, false)]
    [InlineData("Standard Door", "shutter", 3, false)]
    [InlineData("Standard Door", "revolving", 3, false)]
    [InlineData("Standard Door", "open", 3, false)]
    [InlineData("Standard Door", "", 3, false)]
    [InlineData("Standard Door", null, 3, false)]
    [InlineData("Standard Door", "elevator\n", 3, false)]
    [InlineData("Standard Door", "elevator\r", 3, false)]
    [InlineData("Standard Door", "elevator\t", 3, false)]
    [InlineData("Standard Door", "elevator\u2028", 3, false)]
    [InlineData("Standard Door", "elevator\u2029", 3, false)]
    public void V3_IsElevatorDoor_RecognizesNormalizedGisDoorAlongsideLegacyFamilyName(
        string familyName,
        string? gisDoor,
        int standardVersion,
        bool expected)
    {
        Assert.Equal(expected, OpeningFamilyRules.IsElevatorDoor(familyName, gisDoor, standardVersion));
    }

    [Fact]
    public void V3_ResolveOpeningCategory_SupportsAllClosedDomainOpeningCategories()
    {
        foreach (string category in GisStandardDomains.OpeningCategories)
        {
            Assert.Equal(category, OpeningFamilyRules.ResolveOpeningCategory("Standard Door", category, null, standardVersion: 3));
            Assert.Equal(category, OpeningFamilyRules.ResolveOpeningCategory("Standard Door", $" {category.ToUpperInvariant()} ", null, standardVersion: 3));
        }
    }

    [Theory]
    // Precedence 1: valid gis_opening_category wins over gis_access_con="emergency" and family name.
    [InlineData("Emergency Exit Door", "service", "emergency", "service")]
    [InlineData("Emergency Exit Door", "pedestrian", "emergency", "pedestrian")]
    [InlineData("Standard Door", " Pedestrian.Principal ", "emergency", "pedestrian.principal")]
    [InlineData("Standard Door", "PEDESTRIAN.TRANSIT", "none", "pedestrian.transit")]
    [InlineData("Standard Door", "automobile", null, "automobile")]
    [InlineData("Standard Door", "bicycle", null, "bicycle")]
    // Precedence 2: blank/invalid gis_opening_category falls through to gis_access_con="emergency".
    [InlineData("Standard Door", null, "emergency", "emergencyexit")]
    [InlineData("Standard Door", "", " EMERGENCY ", "emergencyexit")]
    [InlineData("Shutter", "   ", "Emergency", "emergencyexit")]
    [InlineData("Standard Door", "invalid_category", "emergency", "emergencyexit")]
    [InlineData("Standard Door", "service\n", "emergency", "emergencyexit")]
    // Precedence 3: blank/invalid gis_opening_category and non-emergency gis_access_con fall back to family name heuristic.
    [InlineData("Emergency Exit Door", null, "none", "emergencyexit")]
    [InlineData("Emergency Exit Door", "unknown", "employeesonly", "emergencyexit")]
    [InlineData("Emergency Exit Door", "pedestrian\n", "restricted", "emergencyexit")]
    [InlineData("Standard Door", null, "none", "pedestrian")]
    [InlineData("Shutter", null, "scheduled", "pedestrian")]
    [InlineData("Gate", null, "badgereader", "pedestrian")]
    [InlineData("Standard Door", "main_gate", "restricted", "pedestrian")]
    // Control-character rejection on both gis_opening_category and gis_access_con.
    [InlineData("Standard Door", "service\r", "emergency\n", "pedestrian")]
    [InlineData("Standard Door", "bicycle\t", "emergency\t", "pedestrian")]
    [InlineData("Standard Door", "automobile\u2028", "emergency\u2029", "pedestrian")]
    public void V3_ResolveOpeningCategory_EnforcesPrecedenceNormalizationAndControlCharacterRejection(
        string familyName,
        string? gisOpeningCategory,
        string? gisAccessCon,
        string expected)
    {
        Assert.Equal(expected, OpeningFamilyRules.ResolveOpeningCategory(familyName, gisOpeningCategory, gisAccessCon, standardVersion: 3));
    }

    [Theory]
    [InlineData("Emergency Exit Door", "emergencyexit")]
    [InlineData("j emergency", "emergencyexit")]
    [InlineData("Standard Door", "pedestrian")]
    [InlineData("EV\u6249", "pedestrian")]
    [InlineData("", "pedestrian")]
    [InlineData(null, "pedestrian")]
    public void V3_WithoutV3Values_GivesTheLegacyCategory(string? familyName, string expected)
    {
        Assert.Equal(expected, OpeningFamilyRules.ResolveOpeningCategory(familyName, null, null, standardVersion: null));
        Assert.Equal(expected, OpeningFamilyRules.ResolveOpeningCategory(familyName, null, null, standardVersion: 1));
        Assert.Equal(expected, OpeningFamilyRules.ResolveOpeningCategory(familyName, null, null, standardVersion: 2));
        Assert.Equal(expected, OpeningFamilyRules.ResolveOpeningCategory(familyName, null, null, standardVersion: 3));
        Assert.Equal(expected, OpeningFamilyRules.ResolveOpeningCategory(familyName, string.Empty, "  ", standardVersion: 3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ElevatorDoorFamilyName_IsAnElevatorDoorOnEveryVersion_WithOrWithoutGisDoor(int? standardVersion)
    {
        Assert.True(OpeningFamilyRules.IsElevatorDoor("EV\u6249", null, standardVersion));
        Assert.True(OpeningFamilyRules.IsElevatorDoor(" EV\u6249 ", "elevator", standardVersion));
        Assert.True(OpeningFamilyRules.IsElevatorDoor("EV\u6249", "swinging", standardVersion));
        Assert.False(OpeningFamilyRules.IsElevatorDoor("Standard Door", null, standardVersion));
    }
}
