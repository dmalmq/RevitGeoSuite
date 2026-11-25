using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

/// <summary>
/// Pins the files in <c>standards/</c> to the code that generates them. After a deliberate change
/// (a new parameter, a new floor type, a new colour table from Kanizu), regenerate with
/// <c>REVITGEOSUITE_REGENERATE_STANDARDS=1</c> set and run these tests once.
/// </summary>
public sealed class GisStandardTests
{
    private const string RegenerateVariable = "REVITGEOSUITE_REGENERATE_STANDARDS";

    [Fact]
    public void SharedParameterFile_MatchesGeneratedText()
    {
        AssertShippedFile(GisSharedParameters.FileName, GisSharedParameters.ToSharedParameterFileText());
        Assert.Equal(GisSharedParameters.ToSharedParameterFileText(), GisStandardFiles.ReadSharedParameterFile());
    }

    [Fact]
    public void BindingsFile_MatchesGeneratedJson()
    {
        AssertShippedFile(GisSharedParameters.BindingsFileName, GisSharedParameters.ToBindingsJson());
    }

    [Fact]
    public void FloorTypesFile_MatchesTypesGeneratedFromTheVendoredTheme()
    {
        string expected = FloorTypeStandard.Generate(KanizuColorTheme.Parse(ReadShipped(GisStandardFiles.ColorThemeFileName))).ToJson();

        AssertShippedFile(FloorTypeStandard.FileName, expected);
        Assert.Equal(expected, GisStandardFiles.ReadFloorTypes());
    }

    [Fact]
    public void FieldListFile_MatchesTheGeneratedStandardList()
    {
        string expected = GisFieldList.CreateStandard().ToJson();

        AssertShippedFile(GisFieldList.FileName, expected);
        Assert.Equal(expected, GisStandardFiles.ReadFieldList());
    }

    [Fact]
    public void OpeningTypesFile_MatchesTheGeneratedDomainsJson()
    {
        string expected = GisStandardDomains.ToOpeningTypesJson();

        AssertShippedFile(GisStandardDomains.OpeningTypesFileName, expected);
        Assert.Equal(expected, GisStandardFiles.ReadOpeningTypes());
    }

    [Fact]
    public void GisModelTemplate_HoldsTheDefaults()
    {
        string expected = GisModelFile.CreateDefault().ToJson();

        AssertShippedFile(GisModelFile.TemplateFileName, expected);
        Assert.Equal(expected, GisStandardFiles.ReadGisModelTemplate());
    }

    [Fact]
    public void SharedParameters_HaveTheStandardNamesWithUniqueGuids()
    {
        Assert.Equal(
            new[]
            {
                "id", "level_id", "name", "short_name", "category", "ordinal",
                "gis_category", "gis_restriction", "gis_kind", "gis_direction",
                "gis_levels_served", "gis_door", "gis_access_con", "gis_key", "gis_standard_version",
                "gis_opening_category", "gis_fixture_type", "gis_model_role", "gis_kit_version",
            },
            GisSharedParameters.All.Select(definition => definition.Name));
        Assert.Equal(GisSharedParameters.All.Count, GisSharedParameters.All.Select(definition => definition.Guid).Distinct().Count());
        Assert.DoesNotContain(GisSharedParameters.All, definition => definition.Guid == Guid.Empty);
    }

    [Fact]
    public void SharedParameterFile_IsPlainAscii()
    {
        string text = GisSharedParameters.ToSharedParameterFileText();

        Assert.All(text, character => Assert.True(character < 128, $"Non-ASCII character U+{(int)character:X4}."));
        Assert.DoesNotContain("\r", text);
    }

    [Theory]
    [InlineData("gis_category", GisParameterBinding.Type)]
    [InlineData("gis_restriction", GisParameterBinding.Type)]
    [InlineData("gis_kind", GisParameterBinding.Type)]
    [InlineData("gis_direction", GisParameterBinding.Instance)]
    [InlineData("category", GisParameterBinding.Instance)]
    [InlineData("gis_key", GisParameterBinding.Instance)]
    [InlineData("gis_opening_category", GisParameterBinding.Type)]
    [InlineData("gis_fixture_type", GisParameterBinding.Type)]
    [InlineData("gis_model_role", GisParameterBinding.Instance)]
    [InlineData("gis_kit_version", GisParameterBinding.Instance)]
    public void SharedParameters_BindTypeAndInstanceAsAgreed(string name, GisParameterBinding binding)
    {
        Assert.True(GisSharedParameters.TryGet(name, out GisParameterDefinition definition));
        Assert.Equal(binding, definition.Binding);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("short_name")]
    [InlineData("category")]
    [InlineData("gis_category")]
    [InlineData("gis_restriction")]
    [InlineData("gis_direction")]
    [InlineData("gis_key")]
    public void V3ExtendedParameters_IncludeMechanicalEquipment(string name)
    {
        Assert.True(GisSharedParameters.TryGet(name, out GisParameterDefinition definition));
        Assert.Contains("OST_MechanicalEquipment", definition.Categories);
    }

    [Fact]
    public void GisKey_IsBoundToDoorsAndWindowsInStandardV3()
    {
        Assert.True(GisSharedParameters.TryGet(GisSharedParameters.GisKey, out GisParameterDefinition definition));
        Assert.Contains("OST_Doors", definition.Categories);
        Assert.Contains("OST_Windows", definition.Categories);
    }

    [Fact]
    public void V3NewParameters_HaveExpectedCategoriesAndRfc4122V4Guids()
    {
        Assert.True(GisSharedParameters.TryGet(GisSharedParameters.GisOpeningCategory, out GisParameterDefinition openingCat));
        Assert.Equal(new[] { "OST_Doors", "OST_Windows" }, openingCat.Categories);
        Assert.Equal(GisParameterDataType.Text, openingCat.DataType);

        Assert.True(GisSharedParameters.TryGet(GisSharedParameters.GisFixtureType, out GisParameterDefinition fixtureType));
        Assert.Equal(new[] { "OST_SpecialityEquipment", "OST_GenericModel", "OST_MechanicalEquipment" }, fixtureType.Categories);
        Assert.Equal(GisParameterDataType.Text, fixtureType.DataType);

        Assert.True(GisSharedParameters.TryGet(GisSharedParameters.GisModelRole, out GisParameterDefinition modelRole));
        Assert.Equal(new[] { "OST_ProjectInformation" }, modelRole.Categories);
        Assert.Equal(GisParameterDataType.Text, modelRole.DataType);

        Assert.True(GisSharedParameters.TryGet(GisSharedParameters.GisKitVersion, out GisParameterDefinition kitVersion));
        Assert.Equal(new[] { "OST_ProjectInformation" }, kitVersion.Categories);
        Assert.Equal(GisParameterDataType.Text, kitVersion.DataType);

        foreach (GisParameterDefinition definition in new[] { openingCat, fixtureType, modelRole, kitVersion })
        {
            byte[] bytes = definition.Guid.ToByteArray();
            int version = (bytes[7] >> 4) & 0x0F;
            int variant = (bytes[8] >> 6) & 0x03;
            Assert.Equal(4, version);
            Assert.Equal(2, variant);
        }
    }

    [Theory]
    [InlineData("category")]
    [InlineData("name")]
    [InlineData("gis_key")]
    public void UnitParameters_AreBoundToStairsAndFamiliesAsWellAsFloors(string name)
    {
        Assert.True(GisSharedParameters.TryGet(name, out GisParameterDefinition definition));
        Assert.Contains("OST_Floors", definition.Categories);
        Assert.Contains("OST_Stairs", definition.Categories);
        Assert.Contains("OST_GenericModel", definition.Categories);
        Assert.Contains("OST_SpecialityEquipment", definition.Categories);
    }

    [Fact]
    public void LevelsServed_IsAnInstanceParameter_BoundToMechanicalEquipment_WhereLumineDrawsItsElevators()
    {
        // LUMINE's 63 elevator objects share one type, so a type value could not differ per shaft.
        Assert.True(GisSharedParameters.TryGet(GisSharedParameters.GisLevelsServed, out GisParameterDefinition definition));
        Assert.Equal(GisParameterBinding.Instance, definition.Binding);
        Assert.Equal(new[] { "OST_GenericModel", "OST_SpecialityEquipment", "OST_MechanicalEquipment" }, definition.Categories);
    }

    [Fact]
    public void FloorTypes_UseTheThemeColourOfTheirArea()
    {
        KanizuColorTheme theme = KanizuColorTheme.LoadEmbedded();
        FloorTypeStandard standard = FloorTypeStandard.LoadEmbedded();

        Assert.Equal(21, standard.FloorTypes.Count);
        foreach (StandardFloorType floorType in standard.FloorTypes)
        {
            Assert.Equal(theme.GetArea(floorType.Area).Hex, floorType.FillHex);
            Assert.Equal("GIS_" + floorType.Area, floorType.Material);
            Assert.Contains(standard.Materials, material => material.Name == floorType.Material && material.FillHex == floorType.FillHex);
        }

        Assert.Equal("#657678", standard.OutlineHex);
        Assert.Contains(standard.Materials, material => material.Name == "GIS_unlisted" && material.FillHex == "#E5E5E5");
    }

    [Theory]
    [InlineData("j ラチ外コンコース_床", "walkway", "rachi_gai", "#FFFFFF")]
    [InlineData("j ラチ内コンコース_床", "walkway", "rachi_nai", "#F2F7FB")]
    [InlineData("j 新幹線ホーム_床", "platform", "rachi_nai", "#E5EFF7")]
    [InlineData("j ラチ外店舗_床", "retail", "rachi_gai", "#DDEBEC")]
    [InlineData("j ラチ内店舗_床", "retail", "rachi_nai", "#DDEBEC")]
    [InlineData("j ラチ内スロープ_床", "ramp", "rachi_nai", "#F2F7FB")]
    [InlineData("j 屋外_床", "unenclosedarea", null, "#F2F2F2")]
    [InlineData("j 多目的トイレ_床", "restroom.unisex", null, "#DDEBEC")]
    [InlineData("j 駅事務室_床", "nonpublic", null, "#F2F2F2")]
    public void FloorTypes_CarryTheAgreedCategoryRestrictionAndFill(string name, string category, string? restriction, string fill)
    {
        Assert.True(FloorTypeStandard.LoadEmbedded().TryGetFloorType(name, out StandardFloorType floorType));
        Assert.Equal(category, floorType.GisCategory);
        Assert.Equal(restriction, floorType.GisRestriction);
        Assert.Equal(fill, floorType.FillHex);
    }

    [Fact]
    public void FloorTypes_UseKnownUnitCategories()
    {
        foreach (StandardFloorType floorType in FloorTypeStandard.LoadEmbedded().FloorTypes)
        {
            Assert.True(ImdfUnitCategoryCatalog.TryGetInfo(floorType.GisCategory, out _), floorType.GisCategory);
        }
    }

    [Fact]
    public void FloorTypes_NamesParseToTheirOwnCategory()
    {
        // A floor of a standard type whose parameters were lost still exports correctly by name.
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();
        foreach (StandardFloorType floorType in FloorTypeStandard.LoadEmbedded().FloorTypes)
        {
            ZoneNameParseResult parsed = ZoneNameParser.Parse(floorType.Name);
            Assert.True(parsed.PatternMatched, floorType.Name);
            if (catalog.TryGetZoneInfo(parsed.ZoneName, out ZoneInfo info))
            {
                Assert.Equal(floorType.GisCategory, info.Category);
            }
        }
    }

    private static void AssertShippedFile(string fileName, string expected)
    {
        string path = GetStandardsPath(fileName);
        if (string.Equals(Environment.GetEnvironmentVariable(RegenerateVariable), "1", StringComparison.Ordinal))
        {
            File.WriteAllText(path, expected, new UTF8Encoding(false));
        }

        Assert.True(
            string.Equals(expected, ReadShipped(fileName), StringComparison.Ordinal),
            $"standards/{fileName} is out of date. Set {RegenerateVariable}=1 and run GisStandardTests once to regenerate it.");
    }

    private static string ReadShipped(string fileName)
    {
        return File.ReadAllText(GetStandardsPath(fileName), Encoding.UTF8).Replace("\r\n", "\n");
    }

    private static string GetStandardsPath(string fileName)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "RevitGeoSuite.sln")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine(current!.FullName, "standards", fileName);
    }
}
