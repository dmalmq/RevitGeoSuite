using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>One floor type of the standard: what its floors export as, and how they look.</summary>
public sealed class StandardFloorType
{
    public StandardFloorType(string name, string gisCategory, string? gisRestriction, string area, string material, string fillHex)
    {
        Name = name;
        GisCategory = gisCategory;
        GisRestriction = gisRestriction;
        Area = area;
        Material = material;
        FillHex = fillHex;
    }

    public string Name { get; }

    public string GisCategory { get; }

    /// <summary>Null when the restriction depends on where the floor is (shops, restrooms).</summary>
    public string? GisRestriction { get; }

    /// <summary>The area key in Kanizu's colour table.</summary>
    public string Area { get; }

    public string Material { get; }

    public string FillHex { get; }
}

/// <summary>One material of the standard, named <c>GIS_&lt;area&gt;</c>, in its theme colour.</summary>
public sealed class StandardMaterial
{
    public StandardMaterial(string name, string area, string fillHex, string nameJa)
    {
        Name = name;
        Area = area;
        FillHex = fillHex;
        NameJa = nameJa;
    }

    public string Name { get; }

    public string Area { get; }

    public string FillHex { get; }

    public string NameJa { get; }
}

/// <summary>
/// The floor types of the Kanizu GIS standard. Each type carries its own <c>gis_category</c> and
/// <c>gis_restriction</c> and a material in the theme colour of its area, so the type alone decides
/// what a floor exports as and how it looks. <c>standards/floor-types.json</c> is generated from this
/// list and the vendored colour table, and a test pins the three together: a theme change is one
/// regenerate, not hand editing.
/// </summary>
public sealed class FloorTypeStandard
{
    public const string FileName = "floor-types.json";

    public const string MaterialPrefix = "GIS_";

    /// <summary>The material for a category the colour table does not list.</summary>
    public const string UnlistedMaterialName = MaterialPrefix + "unlisted";

    public const string UnlistedFillHex = "#E5E5E5";

    private static readonly (string Name, string Category, string? Restriction, string Area)[] Definitions =
    {
        ("j ラチ外コンコース_床", "walkway", "rachi_gai", "free_area"),
        ("j ラチ内コンコース_床", "walkway", "rachi_nai", "paid_area"),
        ("j ホーム_床", "platform", "rachi_nai", "paid_area"),
        ("j 新幹線コンコース_床", "walkway", "rachi_nai", "paid_area_shinkansen"),
        ("j 新幹線ホーム_床", "platform", "rachi_nai", "paid_area_shinkansen"),
        ("j 店舗_床", "retail", null, "facilities"),
        ("j 男子トイレ_床", "restroom.male", null, "facilities"),
        ("j 女子トイレ_床", "restroom.female", null, "facilities"),
        ("j 多目的トイレ_床", "restroom.unisex", null, "facilities"),
        ("j みどりの窓口_床", "ticketing", null, "facilities"),
        ("j 待合室_床", "waitingroom", null, "restricted"),
        ("j 駅事務室_床", "nonpublic", null, "restricted"),
    };

    private FloorTypeStandard(
        int standardVersion,
        string outlineHex,
        IReadOnlyList<StandardMaterial> materials,
        IReadOnlyList<StandardFloorType> floorTypes)
    {
        StandardVersion = standardVersion;
        OutlineHex = outlineHex;
        Materials = materials;
        FloorTypes = floorTypes;
    }

    public int StandardVersion { get; }

    public string OutlineHex { get; }

    public IReadOnlyList<StandardMaterial> Materials { get; }

    public IReadOnlyList<StandardFloorType> FloorTypes { get; }

    /// <summary>
    /// Every unit category the export accepts, so tools outside RevitGeoSuite (the pyRevit audit)
    /// can check a <c>gis_category</c> value without a copy of the list.
    /// </summary>
    public static IReadOnlyList<string> KnownCategories =>
        ImdfUnitCategoryCatalog.GetCategories(includeLegacy: true, includeUnspecified: false);

    public bool TryGetFloorType(string? name, out StandardFloorType floorType)
    {
        floorType = FloorTypes.FirstOrDefault(entry => string.Equals(entry.Name, name?.Trim(), StringComparison.Ordinal))!;
        return floorType != null;
    }

    /// <summary>The standard built from the colour table: one material per area, plus one for unlisted categories.</summary>
    public static FloorTypeStandard Generate(KanizuColorTheme theme)
    {
        if (theme is null)
        {
            throw new ArgumentNullException(nameof(theme));
        }

        List<StandardMaterial> materials = theme.Areas
            .Select(area => new StandardMaterial(MaterialPrefix + area.Key, area.Key, area.Hex, area.NameJa))
            .ToList();
        materials.Add(new StandardMaterial(UnlistedMaterialName, "unlisted", UnlistedFillHex, "その他"));

        List<StandardFloorType> floorTypes = Definitions
            .Select(definition => new StandardFloorType(
                definition.Name,
                definition.Category,
                definition.Restriction,
                definition.Area,
                MaterialPrefix + definition.Area,
                theme.GetArea(definition.Area).Hex))
            .ToList();

        return new FloorTypeStandard(GisStandard.Version, theme.OutlineHex, materials, floorTypes);
    }

    public static FloorTypeStandard LoadEmbedded()
    {
        return Parse(GisStandardFiles.ReadFloorTypes());
    }

    public static FloorTypeStandard Parse(string json)
    {
        JObject root = JObject.Parse(json);
        List<StandardMaterial> materials = ((JArray?)root["materials"] ?? new JArray())
            .Select(token => new StandardMaterial(
                (string)token["name"]!,
                (string)token["area"]!,
                (string)token["fill"]!,
                (string?)token["name_ja"] ?? string.Empty))
            .ToList();
        List<StandardFloorType> floorTypes = ((JArray?)root["floor_types"] ?? new JArray())
            .Select(token => new StandardFloorType(
                (string)token["name"]!,
                (string)token["gis_category"]!,
                string.IsNullOrWhiteSpace((string?)token["gis_restriction"]) ? null : (string?)token["gis_restriction"],
                (string)token["area"]!,
                (string)token["material"]!,
                (string)token["fill"]!))
            .ToList();
        return new FloorTypeStandard(
            (int?)root["standard_version"] ?? 0,
            (string?)root["outline"] ?? string.Empty,
            materials,
            floorTypes);
    }

    /// <summary>The text of <c>standards/floor-types.json</c>: UTF-8, LF line endings, two-space indent.</summary>
    public string ToJson()
    {
        JObject root = new()
        {
            ["standard_version"] = StandardVersion,
            ["generated_from"] = "standards/color_theme.json (Kanizu crates/kanizu-core/src/recolor/color_theme.json)",
            ["note"] = "Generated by FloorTypeStandard.Generate. Do not edit by hand; see standards/README.md.",
            ["outline"] = OutlineHex,
            ["unlisted_fill"] = UnlistedFillHex,
            ["known_categories"] = new JArray(KnownCategories),
            ["materials"] = new JArray(Materials.Select(material => new JObject
            {
                ["name"] = material.Name,
                ["area"] = material.Area,
                ["name_ja"] = material.NameJa,
                ["fill"] = material.FillHex,
            })),
            ["floor_types"] = new JArray(FloorTypes.Select(floorType => new JObject
            {
                ["name"] = floorType.Name,
                ["gis_category"] = floorType.GisCategory,
                ["gis_restriction"] = floorType.GisRestriction ?? string.Empty,
                ["area"] = floorType.Area,
                ["material"] = floorType.Material,
                ["fill"] = floorType.FillHex,
            })),
        };

        return root.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";
    }
}
