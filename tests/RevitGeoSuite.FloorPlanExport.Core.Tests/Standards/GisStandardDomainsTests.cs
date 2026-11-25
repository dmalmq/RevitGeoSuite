using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

public sealed class GisStandardDomainsTests
{
    [Fact]
    public void ClosedDomains_MatchLockedInV3Values()
    {
        Assert.Equal(
            new[] { "swinging", "sliding", "automatic", "elevator", "shutter", "revolving", "open" },
            GisStandardDomains.DoorTypes);
        Assert.Equal(
            new[] { "none", "employeesonly", "emergency", "scheduled", "badgereader", "restricted" },
            GisStandardDomains.AccessControls);
        Assert.Equal(
            new[] { "pedestrian", "emergencyexit", "service", "automobile", "bicycle", "pedestrian.principal", "pedestrian.transit" },
            GisStandardDomains.OpeningCategories);
        Assert.Equal(
            new[] { "turnstile", "wall", "kiosk", "equipment", "furniture", "obstruction" },
            GisStandardDomains.FixtureTypes);
        Assert.Equal(
            new[] { "authoring", "showcase" },
            GisStandardDomains.ModelRoles);
    }

    [Fact]
    public void EmbeddedOpeningTypesJson_MatchesCSharpDomains()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> parsed =
            GisStandardDomains.ParseOpeningTypesJson(GisStandardFiles.ReadOpeningTypes());

        Assert.Equal(GisStandardDomains.DoorTypes, parsed[GisSharedParameters.GisDoor]);
        Assert.Equal(GisStandardDomains.AccessControls, parsed[GisSharedParameters.GisAccessCon]);
        Assert.Equal(GisStandardDomains.OpeningCategories, parsed[GisSharedParameters.GisOpeningCategory]);
        Assert.Equal(GisStandardDomains.FixtureTypes, parsed[GisSharedParameters.GisFixtureType]);
        Assert.Equal(GisStandardDomains.ModelRoles, parsed[GisSharedParameters.GisModelRole]);
    }

    [Fact]
    public void EmbeddedFieldsJson_MatchesClosedDomainsForOpeningAndFixtureFields()
    {
        GisFieldList fields = GisFieldList.LoadStandard();

        Assert.Equal(
            GisStandardDomains.OpeningCategories,
            fields.Fields.Single(field => field.Key == "opening.category").Values);
        Assert.Equal(
            GisStandardDomains.DoorTypes,
            fields.Fields.Single(field => field.Key == "opening.door").Values);
        Assert.Equal(
            GisStandardDomains.AccessControls,
            fields.Fields.Single(field => field.Key == "opening.access_con").Values);
        Assert.Equal(
            GisStandardDomains.FixtureTypes,
            fields.Fields.Single(field => field.Key == "fixture.type").Values);
    }

    [Theory]
    [InlineData("gis_door", "sliding", true)]
    [InlineData("gis_door", " Sliding ", true)]
    [InlineData("gis_door", "hinged", false)]
    [InlineData("gis_door", "sliding\n", false)]
    [InlineData("gis_access_con", "none", true)]
    [InlineData("gis_access_con", "EMPLOYEESONLY", true)]
    [InlineData("gis_access_con", "public", false)]
    [InlineData("gis_opening_category", "pedestrian.transit", true)]
    [InlineData("gis_opening_category", "Pedestrian.Principal", true)]
    [InlineData("gis_opening_category", "main_gate", false)]
    [InlineData("gis_fixture_type", "turnstile", true)]
    [InlineData("gis_fixture_type", "Obstruction", true)]
    [InlineData("gis_fixture_type", "bench", false)]
    [InlineData("gis_model_role", "authoring", true)]
    [InlineData("gis_model_role", "SHOWCASE", true)]
    [InlineData("gis_model_role", "draft", false)]
    public void DomainValidation_NormalizesCaseAndRejectsUnknownOrControlCharacters(string parameter, string value, bool expected)
    {
        Assert.Equal(expected, GisStandardDomains.IsValidValue(parameter, value));
    }

    [Fact]
    public void ValidateOptionalValue_AllowsBlankAndRejectsInvalidNonEmptyOrControlValues()
    {
        Assert.Null(GisStandardDomains.ValidateOptionalValue(GisSharedParameters.GisOpeningCategory, ""));
        Assert.Null(GisStandardDomains.ValidateOptionalValue(GisSharedParameters.GisOpeningCategory, "   "));
        Assert.Null(GisStandardDomains.ValidateOptionalValue(GisSharedParameters.GisOpeningCategory, "pedestrian.transit"));

        string? badValue = GisStandardDomains.ValidateOptionalValue(GisSharedParameters.GisOpeningCategory, "unknown");
        Assert.NotNull(badValue);
        Assert.Contains("gis_opening_category", badValue);
        Assert.Contains("pedestrian.transit", badValue);

        string? newlineOnly = GisStandardDomains.ValidateOptionalValue(GisSharedParameters.GisDoor, "\n");
        Assert.NotNull(newlineOnly);
    }

    [Fact]
    public void TryNormalize_ReturnsCanonicalLowercaseToken()
    {
        Assert.True(GisStandardDomains.TryNormalizeDoorType(" Revolving ", out string door));
        Assert.Equal("revolving", door);

        Assert.True(GisStandardDomains.TryNormalizeAccessControl("BadgeReader", out string access));
        Assert.Equal("badgereader", access);

        Assert.True(GisStandardDomains.TryNormalizeOpeningCategory("Pedestrian.Transit", out string opening));
        Assert.Equal("pedestrian.transit", opening);

        Assert.True(GisStandardDomains.TryNormalizeFixtureType(" KIOSK ", out string fixture));
        Assert.Equal("kiosk", fixture);

        Assert.True(GisStandardDomains.TryNormalizeModelRole("Showcase", out string role));
        Assert.Equal("showcase", role);
    }
}
