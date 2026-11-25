using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

/// <summary>
/// The Kanizu additions to the 3D Tiles export: optional GIS join columns, the K_Footprint
/// exclusion and the opt-in palette. Exports without any of them must not change by a byte.
/// </summary>
public sealed class Tiles3DKanizuExportTests
{
    private static readonly string[] LegacyGlbProperties =
    {
        "revitElementId", "revitUniqueId", "name", "category", "familyName", "typeName", "levelName",
        "levelKey", "levelUniqueId", "levelElevationMeters", "heightMeters", "minZMeters", "maxZMeters",
        "sourceDocument", "sourceLinkName", "sourceLinkInstanceUniqueId",
    };

    private static readonly string[] GisGlbProperties = { "gisId", "gisRevitKey", "gisKind", "gisCategory", "gisLevelId" };

    [Theory]
    [InlineData("K_Footprint")]
    [InlineData("k_footprint")]
    [InlineData("K_FOOTPRINT")]
    [InlineData("  K_Footprint ")]
    public void Footprint_subcategory_is_excluded_case_insensitively(string name)
    {
        Assert.True(Tiles3DGeometryExtractor.IsExcludedSubcategoryName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Floors")]
    [InlineData("K_Footprints")]
    [InlineData("K_Footprint_Outline")]
    [InlineData("Footprint")]
    public void Other_subcategories_are_kept(string? name)
    {
        Assert.False(Tiles3DGeometryExtractor.IsExcludedSubcategoryName(name));
    }

    [Fact]
    public void Elements_json_without_gis_data_has_no_gis_fields()
    {
        Tiles3DExportPackage package = IndexPackage(Cube("wall-1", 0d), Cube("wall-2", 5d));

        string json = new Tiles3DElementIndexWriter().BuildJson(package);

        Assert.DoesNotContain("gis", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, (int?)JObject.Parse(json)["version"]);
    }

    [Fact]
    public void Elements_json_ignores_blank_gis_values()
    {
        Tiles3DExportPackage plain = IndexPackage(Cube("wall-1", 0d));
        Tiles3DMeshPrimitive blankMesh = Cube("wall-1", 0d);
        blankMesh.Metadata.GisId = "   ";
        blankMesh.Metadata.GisKind = string.Empty;
        Tiles3DExportPackage blank = IndexPackage(blankMesh);

        Tiles3DElementIndexWriter writer = new Tiles3DElementIndexWriter();

        Assert.Equal(writer.BuildJson(plain), writer.BuildJson(blank));
    }

    [Fact]
    public void Elements_json_carries_gis_join_fields_and_omits_the_empty_ones()
    {
        Tiles3DMeshPrimitive floor = Cube("floor-1", 0d);
        floor.Metadata.GisId = " unit-0001 ";
        floor.Metadata.GisRevitKey = "rk-42";
        floor.Metadata.GisCategory = "walkway";
        floor.Metadata.GisLevelId = "level-b1";
        Tiles3DMeshPrimitive escalator = Cube("esc-1", 5d);
        escalator.Metadata.GisKind = "escalator";
        Tiles3DMeshPrimitive wall = Cube("wall-1", 10d);
        Tiles3DExportPackage package = IndexPackage(floor, escalator, wall);

        JObject document = JObject.Parse(new Tiles3DElementIndexWriter().BuildJson(package));

        Assert.Equal(1, (int?)document["version"]);
        Dictionary<string, JObject> entries = document["elements"]!
            .Cast<JObject>()
            .ToDictionary(entry => (string)entry["uniqueId"]!, entry => entry);

        JObject floorEntry = entries["floor-1"];
        Assert.Equal("unit-0001", (string?)floorEntry["gisId"]);
        Assert.Equal("rk-42", (string?)floorEntry["gisRevitKey"]);
        Assert.Equal("walkway", (string?)floorEntry["gisCategory"]);
        Assert.Equal("level-b1", (string?)floorEntry["gisLevelId"]);
        Assert.Null(floorEntry["gisKind"]);

        JObject escalatorEntry = entries["esc-1"];
        Assert.Equal("escalator", (string?)escalatorEntry["gisKind"]);
        Assert.Null(escalatorEntry["gisId"]);

        Assert.DoesNotContain(entries["wall-1"].Properties(), property => property.Name.StartsWith("gis", StringComparison.Ordinal));
    }

    [Fact]
    public void Glb_without_gis_data_keeps_the_legacy_metadata_columns()
    {
        string path = WriteGlb(GlbPackage(Mesh("101", 0d), Mesh("102", 3d)));

        JObject json = JObject.Parse(ReadGlbJson(path));
        JToken metadata = json["extensions"]!["EXT_structural_metadata"]!;
        JObject schemaProperties = (JObject)metadata["schema"]!["classes"]!["element"]!["properties"]!;
        JObject tableProperties = (JObject)metadata["propertyTables"]![0]!["properties"]!;

        Assert.Equal(LegacyGlbProperties, schemaProperties.Properties().Select(property => property.Name));
        Assert.Equal(LegacyGlbProperties, tableProperties.Properties().Select(property => property.Name));
    }

    [Fact]
    public void Glb_with_only_blank_gis_values_is_byte_identical_to_one_without()
    {
        Tiles3DExportPackage plain = GlbPackage(Mesh("101", 0d), Mesh("102", 3d));
        Tiles3DExportPackage blank = GlbPackage(Mesh("101", 0d), Mesh("102", 3d));
        blank.Meshes[0].Metadata.GisId = "  ";
        blank.Meshes[1].Metadata.GisLevelId = string.Empty;

        Assert.Equal(File.ReadAllBytes(WriteGlb(plain)), File.ReadAllBytes(WriteGlb(blank)));
    }

    [Fact]
    public void Glb_with_gis_data_adds_the_join_columns_after_the_legacy_ones()
    {
        Tiles3DExportPackage package = GlbPackage(Mesh("101", 0d), Mesh("102", 3d));
        package.Meshes[0].Metadata.GisId = "unit-0001";
        package.Meshes[0].Metadata.GisRevitKey = "rk-42";
        package.Meshes[0].Metadata.GisCategory = "walkway";
        package.Meshes[0].Metadata.GisLevelId = " level-1f ";
        package.Meshes[1].Metadata.GisKind = "elevator";
        string path = WriteGlb(package);

        JObject json = JObject.Parse(ReadGlbJson(path));
        JToken metadata = json["extensions"]!["EXT_structural_metadata"]!;
        JObject schemaProperties = (JObject)metadata["schema"]!["classes"]!["element"]!["properties"]!;
        JObject tableProperties = (JObject)metadata["propertyTables"]![0]!["properties"]!;
        string[] expectedColumns = LegacyGlbProperties.Concat(GisGlbProperties).ToArray();

        Assert.Equal(expectedColumns, schemaProperties.Properties().Select(property => property.Name));
        Assert.Equal(expectedColumns, tableProperties.Properties().Select(property => property.Name));
        foreach (string column in GisGlbProperties)
        {
            Assert.Equal("STRING", (string?)schemaProperties[column]!["type"]);
        }

        byte[] bin = ReadGlbBin(path);
        JArray bufferViews = (JArray)json["bufferViews"]!;
        Assert.Equal(new[] { "unit-0001", string.Empty }, ReadStrings(bin, bufferViews, tableProperties["gisId"]!, 2));
        Assert.Equal(new[] { "rk-42", string.Empty }, ReadStrings(bin, bufferViews, tableProperties["gisRevitKey"]!, 2));
        Assert.Equal(new[] { string.Empty, "elevator" }, ReadStrings(bin, bufferViews, tableProperties["gisKind"]!, 2));
        Assert.Equal(new[] { "walkway", string.Empty }, ReadStrings(bin, bufferViews, tableProperties["gisCategory"]!, 2));
        Assert.Equal(new[] { "level-1f", string.Empty }, ReadStrings(bin, bufferViews, tableProperties["gisLevelId"]!, 2));
    }

    [Fact]
    public void Source_palette_writes_srgb_bytes_as_factors()
    {
        Tiles3DExportPackage package = GlbPackage(Mesh("101", 0d, new Tiles3DMaterialColor(0xFB, 0xF9, 0xF4, 255)));

        JArray factor = BaseColorFactor(WriteGlb(package));

        Assert.Equal(new[] { 0xFB / 255d, 0xF9 / 255d, 0xF4 / 255d, 1d }, factor.Values<double>());
    }

    [Fact]
    public void Kanizu_palettes_write_linear_light_factors()
    {
        Tiles3DExportPackage package = GlbPackage(Mesh("101", 0d, new Tiles3DMaterialColor(0xFB, 0xF9, 0xF4, 255)));
        package.MaterialPalette = Tiles3DMaterialPalette.KanizuSoft;

        JArray factor = BaseColorFactor(WriteGlb(package));

        double[] expected = new Tiles3DMaterialColor(0xFB, 0xF9, 0xF4, 255).ToLinearBaseColorFactor();
        Assert.Equal(expected, factor.Values<double>());
        Assert.Equal(246d / 255d, expected[0]);
    }

    [Fact]
    public void Legacy_state_json_without_a_palette_loads_as_source()
    {
        const string legacy = "{\"LastExportPath\":\"C:\\\\tiles\",\"LastLodSetting\":\"Fine\",\"LastExportedElementCount\":3}";

        Tiles3DExportState? state = JsonConvert.DeserializeObject<Tiles3DExportState>(legacy);

        Assert.NotNull(state);
        Assert.Equal(@"C:\tiles", state!.LastExportPath);
        Assert.Equal(Tiles3DMaterialPalette.Source, state.LastMaterialPalette);
        Assert.Equal(Tiles3DMaterialPalette.Source, new Tiles3DExportState().LastMaterialPalette);
        Assert.Equal(Tiles3DMaterialPalette.Source, new Tiles3DExportPackage().MaterialPalette);
    }

    [Fact]
    public void State_round_trips_the_palette()
    {
        Tiles3DExportState state = new Tiles3DExportState { LastMaterialPalette = Tiles3DMaterialPalette.KanizuWhite };

        Tiles3DExportState? loaded = JsonConvert.DeserializeObject<Tiles3DExportState>(JsonConvert.SerializeObject(state));

        Assert.Equal(Tiles3DMaterialPalette.KanizuWhite, loaded!.LastMaterialPalette);
    }

    [Fact]
    public void Updated_state_records_the_package_palette()
    {
        Tiles3DExportPackage package = new Tiles3DExportPackage
        {
            LevelOfDetail = Tiles3DLevelOfDetail.Fine,
            MaterialPalette = Tiles3DMaterialPalette.KanizuFull,
        };

        Tiles3DExportState state = Tiles3DExportCoordinator.BuildUpdatedState(
            null,
            @"C:\exports\tiles",
            package,
            Tiles3DExportReferenceSource.CanonicalOrigin,
            new Tiles3DExportScopeSelection());

        Assert.Equal(Tiles3DMaterialPalette.KanizuFull, state.LastMaterialPalette);
    }

    private static JArray BaseColorFactor(string glbPath)
    {
        JObject json = JObject.Parse(ReadGlbJson(glbPath));
        return (JArray)json["materials"]![0]!["pbrMetallicRoughness"]!["baseColorFactor"]!;
    }

    private static string[] ReadStrings(byte[] bin, JArray bufferViews, JToken property, int count)
    {
        JToken offsetsView = bufferViews[(int)property["stringOffsets"]!]!;
        JToken valuesView = bufferViews[(int)property["values"]!]!;
        int offsetsStart = (int)offsetsView["byteOffset"]!;
        int valuesStart = (int)valuesView["byteOffset"]!;
        string[] values = new string[count];
        for (int index = 0; index < count; index++)
        {
            int start = (int)BitConverter.ToUInt32(bin, offsetsStart + (index * 4));
            int end = (int)BitConverter.ToUInt32(bin, offsetsStart + ((index + 1) * 4));
            values[index] = Encoding.UTF8.GetString(bin, valuesStart + start, end - start);
        }

        return values;
    }

    private static string WriteGlb(Tiles3DExportPackage package)
    {
        string directory = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "content.glb");
        new Tiles3DGlbWriter().Write(path, package);
        return path;
    }

    private static string ReadGlbJson(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int jsonLength = BitConverter.ToInt32(bytes, 12);
        return Encoding.UTF8.GetString(bytes, 20, jsonLength).TrimEnd(' ', '\0');
    }

    private static byte[] ReadGlbBin(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int jsonLength = BitConverter.ToInt32(bytes, 12);
        int binHeader = 20 + jsonLength;
        int binLength = BitConverter.ToInt32(bytes, binHeader);
        byte[] bin = new byte[binLength];
        Array.Copy(bytes, binHeader + 8, bin, 0, binLength);
        return bin;
    }

    private static Tiles3DExportPackage GlbPackage(params Tiles3DMeshPrimitive[] meshes)
    {
        return new Tiles3DExportPackage
        {
            ReferenceContext = new Tiles3DExportReferenceContext
            {
                Title = "Canonical Origin",
                ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
                AnchorLatitude = 36d,
                AnchorLongitude = 139.833333333333d,
                AnchorElevationMeters = 0d
            },
            Meshes = meshes.ToList(),
            ElementCount = meshes.Length,
            TriangleCount = meshes.Sum(mesh => mesh.Triangles.Count),
            GeometricError = 1d,
            BoundingBox = new[] { 0.5d, 0.5d, 0d, 0.5d, 0d, 0d, 0d, 0.5d, 0d, 0d, 0d, 0.01d }
        };
    }

    private static Tiles3DMeshPrimitive Mesh(string elementId, double levelElevationMeters, Tiles3DMaterialColor? color = null)
    {
        Tiles3DMaterialColor resolved = color ?? new Tiles3DMaterialColor(80, 90, 100, 255);
        return new Tiles3DMeshPrimitive
        {
            Metadata = new Tiles3DObjectMetadata
            {
                RevitElementId = elementId,
                RevitUniqueId = $"unique-{elementId}",
                Name = $"Floor {elementId}",
                Category = "Floors",
                FamilyName = "Floor",
                TypeName = "Generic 150mm",
                LevelName = "Ground Floor",
                LevelKey = "ground_floor",
                LevelElevationMeters = levelElevationMeters,
                HeightMeters = 0.15d,
                MinZMeters = levelElevationMeters,
                MaxZMeters = levelElevationMeters + 0.15d,
                SourceDocument = "Host",
                SourceLinkName = string.Empty
            },
            Triangles = new List<Tiles3DTriangle>
            {
                new Tiles3DTriangle(
                    new Tiles3DPoint(0d, 0d, levelElevationMeters),
                    new Tiles3DPoint(1d, 0d, levelElevationMeters),
                    new Tiles3DPoint(0d, 1d, levelElevationMeters + 0.15d),
                    resolved)
            }
        };
    }

    private static Tiles3DExportPackage IndexPackage(params Tiles3DMeshPrimitive[] meshes)
    {
        return new Tiles3DExportPackage
        {
            ReferenceContext = new Tiles3DExportReferenceContext
            {
                AnchorProjectedCoordinate = new ProjectedCoordinate(1000d, 2000d),
                AnchorElevationMeters = 40d,
            },
            GeoidHeightOffsetMeters = 36.7d,
            Meshes = meshes.ToList(),
        };
    }

    private static Tiles3DMeshPrimitive Cube(string uniqueId, double x)
    {
        Tiles3DPoint P(double dx, double dy, double dz) => new Tiles3DPoint(x + dx, dy, dz);
        Tiles3DPoint[] v =
        {
            P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0),
            P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1),
        };
        int[][] faces =
        {
            new[] { 0, 2, 1 }, new[] { 0, 3, 2 }, new[] { 4, 5, 6 }, new[] { 4, 6, 7 },
            new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 1, 2, 6 }, new[] { 1, 6, 5 },
            new[] { 2, 3, 7 }, new[] { 2, 7, 6 }, new[] { 3, 0, 4 }, new[] { 3, 4, 7 },
        };
        return new Tiles3DMeshPrimitive
        {
            Metadata = new Tiles3DObjectMetadata
            {
                RevitUniqueId = uniqueId,
                RevitElementId = "1",
                LevelUniqueId = "level-1f",
                LevelKey = "1f",
                Category = "Floors",
                SourceDocument = "Host",
            },
            Triangles = faces.Select(f => new Tiles3DTriangle(v[f[0]], v[f[1]], v[f[2]])).ToList(),
        };
    }
}
