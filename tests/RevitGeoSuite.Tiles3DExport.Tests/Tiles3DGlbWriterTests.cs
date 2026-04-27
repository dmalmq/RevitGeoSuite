using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DGlbWriterTests
{
    [Fact]
    public void Write_creates_valid_glb_header_and_embeds_mesh_json()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string outputPath = Path.Combine(tempDirectory, "content.glb");
        Tiles3DExportPackage package = CreatePackage();

        new Tiles3DGlbWriter().Write(outputPath, package);

        byte[] bytes = File.ReadAllBytes(outputPath);
        uint magic = BitConverter.ToUInt32(bytes, 0);
        uint version = BitConverter.ToUInt32(bytes, 4);
        int jsonLength = BitConverter.ToInt32(bytes, 12);
        string json = Encoding.UTF8.GetString(bytes, 20, jsonLength).TrimEnd(' ', '\0');

        Assert.Equal(0x46546C67u, magic);
        Assert.Equal(2u, version);
        Assert.Contains("\"meshes\"", json, StringComparison.Ordinal);
        Assert.Contains("\"materials\"", json, StringComparison.Ordinal);
        Assert.Contains("\"EXT_mesh_features\"", json, StringComparison.Ordinal);
        Assert.Contains("\"EXT_structural_metadata\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_groups_triangles_by_revit_material_color()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string outputPath = Path.Combine(tempDirectory, "content.glb");
        Tiles3DExportPackage package = CreatePackage();
        package.Meshes[0].Triangles = new List<Tiles3DTriangle>
        {
            new Tiles3DTriangle(
                new Tiles3DPoint(0d, 0d, 0d),
                new Tiles3DPoint(1d, 0d, 0d),
                new Tiles3DPoint(0d, 1d, 0d),
                new Tiles3DMaterialColor(255, 0, 0, 255)),
            new Tiles3DTriangle(
                new Tiles3DPoint(0d, 0d, 1d),
                new Tiles3DPoint(1d, 0d, 1d),
                new Tiles3DPoint(0d, 1d, 1d),
                new Tiles3DMaterialColor(0, 128, 255, 128))
        };

        new Tiles3DGlbWriter().Write(outputPath, package);

        JObject json = JObject.Parse(ReadGlbJson(outputPath));
        JArray materials = Assert.IsType<JArray>(json["materials"]);
        JArray primitives = Assert.IsType<JArray>(json["meshes"]?[0]?["primitives"]);

        Assert.Equal(2, materials.Count);
        Assert.Equal(2, primitives.Count);
        Assert.Equal(0, (int?)primitives[0]?["material"]);
        Assert.Equal(1, (int?)primitives[1]?["material"]);
        Assert.Equal(1d, materials[0]?["pbrMetallicRoughness"]?["baseColorFactor"]?[0]?.Value<double>() ?? 0d);
        Assert.Equal(128d / 255d, materials[1]?["pbrMetallicRoughness"]?["baseColorFactor"]?[1]?.Value<double>() ?? 0d, 6);
        Assert.Equal(128d / 255d, materials[1]?["pbrMetallicRoughness"]?["baseColorFactor"]?[3]?.Value<double>() ?? 0d, 6);
        Assert.Equal("BLEND", (string?)materials[1]?["alphaMode"]);
    }

    [Fact]
    public void Write_embeds_per_object_feature_ids_and_structural_metadata()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string outputPath = Path.Combine(tempDirectory, "content.glb");
        Tiles3DExportPackage package = CreatePackageWithTwoObjectsSameMaterial();

        new Tiles3DGlbWriter().Write(outputPath, package);

        JObject json = JObject.Parse(ReadGlbJson(outputPath));
        JArray extensionsUsed = Assert.IsType<JArray>(json["extensionsUsed"]);
        Assert.Contains(extensionsUsed, token => (string?)token == "EXT_mesh_features");
        Assert.Contains(extensionsUsed, token => (string?)token == "EXT_structural_metadata");

        JArray primitives = Assert.IsType<JArray>(json["meshes"]?[0]?["primitives"]);
        Assert.Equal(2, primitives.Count);
        Assert.NotNull(primitives[0]?["attributes"]?["_FEATURE_ID_0"]);
        Assert.Equal("element", (string?)primitives[0]?["extensions"]?["EXT_mesh_features"]?["featureIds"]?[0]?["label"]);
        Assert.Equal(2, (int?)primitives[0]?["extensions"]?["EXT_mesh_features"]?["featureIds"]?[0]?["featureCount"]);

        JToken? structuralMetadata = json["extensions"]?["EXT_structural_metadata"];
        Assert.Equal(2, (int?)structuralMetadata?["propertyTables"]?[0]?["count"]);
        Assert.NotNull(structuralMetadata?["schema"]?["classes"]?["element"]?["properties"]?["levelKey"]);
        Assert.NotNull(structuralMetadata?["propertyTables"]?[0]?["properties"]?["heightMeters"]?["values"]);
        Assert.NotNull(structuralMetadata?["propertyTables"]?[0]?["properties"]?["levelName"]?["stringOffsets"]);
    }

    [Fact]
    public void Write_keeps_same_material_objects_as_distinct_filterable_features()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string outputPath = Path.Combine(tempDirectory, "content.glb");
        Tiles3DExportPackage package = CreatePackageWithTwoObjectsSameMaterial();

        new Tiles3DGlbWriter().Write(outputPath, package);

        JObject json = JObject.Parse(ReadGlbJson(outputPath));
        JArray primitives = Assert.IsType<JArray>(json["meshes"]?[0]?["primitives"]);
        JArray accessors = Assert.IsType<JArray>(json["accessors"]);
        int firstFeatureAccessor = (int?)primitives[0]?["attributes"]?["_FEATURE_ID_0"] ?? -1;
        int secondFeatureAccessor = (int?)primitives[1]?["attributes"]?["_FEATURE_ID_0"] ?? -1;

        Assert.Equal(2, primitives.Count);
        Assert.Equal((int?)primitives[0]?["material"], (int?)primitives[1]?["material"]);
        Assert.Equal(0u, accessors[firstFeatureAccessor]?["min"]?[0]?.Value<uint>());
        Assert.Equal(1u, accessors[secondFeatureAccessor]?["min"]?[0]?.Value<uint>());
    }

    private static string ReadGlbJson(string outputPath)
    {
        byte[] bytes = File.ReadAllBytes(outputPath);
        int jsonLength = BitConverter.ToInt32(bytes, 12);
        return Encoding.UTF8.GetString(bytes, 20, jsonLength).TrimEnd(' ', '\0');
    }

    private static Tiles3DExportPackage CreatePackage()
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
            Meshes = new List<Tiles3DMeshPrimitive>
            {
                new Tiles3DMeshPrimitive
                {
                    Metadata = new Tiles3DObjectMetadata
                    {
                        RevitElementId = "1",
                        RevitUniqueId = "unique-1",
                        Name = "Triangle",
                        Category = "Generic Models",
                        FamilyName = "Generic Model",
                        TypeName = "Triangle Type",
                        LevelName = "Ground Floor",
                        LevelKey = "ground_floor",
                        LevelElevationMeters = 0d,
                        HeightMeters = 1d,
                        MinZMeters = 0d,
                        MaxZMeters = 1d,
                        SourceDocument = "Host",
                        SourceLinkName = string.Empty
                    },
                    Triangles = new List<Tiles3DTriangle>
                    {
                        new Tiles3DTriangle(
                            new Tiles3DPoint(0d, 0d, 0d),
                            new Tiles3DPoint(1d, 0d, 0d),
                            new Tiles3DPoint(0d, 1d, 0d))
                    }
                }
            },
            ElementCount = 1,
            TriangleCount = 1,
            GeometricError = 1d,
            BoundingBox = new[] { 0.5d, 0.5d, 0d, 0.5d, 0d, 0d, 0d, 0.5d, 0d, 0d, 0d, 0.01d }
        };
    }

    private static Tiles3DExportPackage CreatePackageWithTwoObjectsSameMaterial()
    {
        Tiles3DMaterialColor color = new Tiles3DMaterialColor(80, 90, 100, 255);
        Tiles3DExportPackage package = CreatePackage();
        package.Meshes = new List<Tiles3DMeshPrimitive>
        {
            Mesh("Wall A", "101", "Ground Floor", 0d, color),
            Mesh("Wall B", "102", "First Floor", 3d, color)
        };
        package.ElementCount = 2;
        package.TriangleCount = 2;
        return package;
    }

    private static Tiles3DMeshPrimitive Mesh(string name, string elementId, string levelName, double levelElevationMeters, Tiles3DMaterialColor color)
    {
        return new Tiles3DMeshPrimitive
        {
            Metadata = new Tiles3DObjectMetadata
            {
                RevitElementId = elementId,
                RevitUniqueId = $"unique-{elementId}",
                Name = name,
                Category = "Walls",
                FamilyName = "Basic Wall",
                TypeName = "Generic 200mm",
                LevelName = levelName,
                LevelKey = Tiles3DLevelMetadata.BuildLevelKey(levelName),
                LevelElevationMeters = levelElevationMeters,
                HeightMeters = 2.5d,
                MinZMeters = levelElevationMeters,
                MaxZMeters = levelElevationMeters + 2.5d,
                SourceDocument = "Host",
                SourceLinkName = string.Empty
            },
            Triangles = new List<Tiles3DTriangle>
            {
                new Tiles3DTriangle(
                    new Tiles3DPoint(0d, 0d, levelElevationMeters),
                    new Tiles3DPoint(1d, 0d, levelElevationMeters),
                    new Tiles3DPoint(0d, 1d, levelElevationMeters + 2.5d),
                    color)
            }
        };
    }
}
