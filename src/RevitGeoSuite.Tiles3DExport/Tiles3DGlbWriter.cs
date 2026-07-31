using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DGlbWriter
{
    private const uint GlbMagic = 0x46546C67;
    private const uint GlbVersion = 2;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinChunkType = 0x004E4942;
    private const string ExtMeshFeatures = "EXT_mesh_features";
    private const string ExtStructuralMetadata = "EXT_structural_metadata";
    private static readonly double[] ZUpToYUpMatrix =
    {
        1d, 0d, 0d, 0d,
        0d, 0d, -1d, 0d,
        0d, 1d, 0d, 0d,
        0d, 0d, 0d, 1d
    };

    public void Write(string outputPath, Tiles3DExportPackage package)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path cannot be empty.", nameof(outputPath));
        }

        if (package is null)
        {
            throw new ArgumentNullException(nameof(package));
        }

        GlbDocument document = BuildDocument(package);
        byte[] jsonBytes = PadJsonChunk(Encoding.UTF8.GetBytes(document.Json));
        byte[] binaryBytes = PadBinaryChunk(document.Binary.ToArray());

        using FileStream stream = File.Create(outputPath);
        using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        uint totalLength = (uint)(12 + 8 + jsonBytes.Length + 8 + binaryBytes.Length);
        writer.Write(GlbMagic);
        writer.Write(GlbVersion);
        writer.Write(totalLength);
        writer.Write((uint)jsonBytes.Length);
        writer.Write(JsonChunkType);
        writer.Write(jsonBytes);
        writer.Write((uint)binaryBytes.Length);
        writer.Write(BinChunkType);
        writer.Write(binaryBytes);
    }

    private static GlbDocument BuildDocument(Tiles3DExportPackage package)
    {
        List<Tiles3DMeshPrimitive> meshes = package.Meshes
            .Where(mesh => mesh.Triangles.Count > 0)
            .ToList();
        List<object> accessors = new List<object>();
        List<object> bufferViews = new List<object>();
        List<object> primitives = new List<object>();
        List<Tiles3DMaterialColor> materialColors = new List<Tiles3DMaterialColor>();
        Dictionary<Tiles3DMaterialColor, int> materialIndexes = new Dictionary<Tiles3DMaterialColor, int>();
        MemoryStream binary = new MemoryStream();

        int featureCount = meshes.Count;
        for (int featureIndex = 0; featureIndex < meshes.Count; featureIndex++)
        {
            Tiles3DMeshPrimitive mesh = meshes[featureIndex];
            foreach (MaterialTriangleGroup group in GroupTrianglesByMaterial(mesh.Triangles))
            {
                int materialIndex = GetMaterialIndex(group.MaterialColor, materialColors, materialIndexes);
                AddPositionBufferView(binary, group.Triangles, bufferViews, out int positionAccessor, out int normalAccessor, accessors);
                AddFeatureIdBufferView(binary, group.Triangles.Count * 3, featureIndex, bufferViews, out int featureIdAccessor, accessors);
                AddIndexBufferView(binary, group.Triangles, bufferViews, out int indexAccessor, accessors);

                Dictionary<string, object> primitive = new Dictionary<string, object>
                {
                    ["attributes"] = new Dictionary<string, object>
                    {
                        ["POSITION"] = positionAccessor,
                        ["NORMAL"] = normalAccessor,
                        ["_FEATURE_ID_0"] = featureIdAccessor
                    },
                    ["indices"] = indexAccessor,
                    ["material"] = materialIndex,
                    ["mode"] = 4,
                    ["extensions"] = new Dictionary<string, object>
                    {
                        [ExtMeshFeatures] = new
                        {
                            featureIds = new object[]
                            {
                                new
                                {
                                    featureCount,
                                    attribute = 0,
                                    propertyTable = 0,
                                    label = "element"
                                }
                            }
                        }
                    }
                };

                primitives.Add(primitive);
            }
        }

        if (materialColors.Count == 0)
        {
            materialColors.Add(Tiles3DMaterialColor.Default);
        }

        object structuralMetadata = BuildStructuralMetadataExtension(binary, bufferViews, meshes.Select(mesh => mesh.Metadata).ToList());
        Dictionary<string, object> document = new Dictionary<string, object>
        {
            ["asset"] = new
            {
                version = "2.0",
                generator = "RevitGeoSuite.Tiles3DExport"
            },
            ["extensionsUsed"] = new[] { ExtMeshFeatures, ExtStructuralMetadata },
            ["extensions"] = new Dictionary<string, object>
            {
                [ExtStructuralMetadata] = structuralMetadata
            },
            ["scene"] = 0,
            ["scenes"] = new object[]
            {
                new
                {
                    nodes = new[] { 0 }
                }
            },
            ["nodes"] = new object[]
            {
                new
                {
                    name = "Root",
                    mesh = 0,
                    matrix = ZUpToYUpMatrix
                }
            },
            ["meshes"] = new object[]
            {
                new
                {
                    primitives
                }
            },
            ["materials"] = materialColors.Select(BuildMaterial).ToArray(),
            ["accessors"] = accessors,
            ["bufferViews"] = bufferViews,
            ["buffers"] = new object[]
            {
                new
                {
                    byteLength = (int)binary.Length
                }
            }
        };

        return new GlbDocument
        {
            Json = JsonConvert.SerializeObject(document, Formatting.None),
            Binary = binary
        };
    }

    private static int AddPositionBufferView(
        MemoryStream binary,
        IReadOnlyList<Tiles3DTriangle> triangles,
        List<object> bufferViews,
        out int positionAccessorIndex,
        out int normalAccessorIndex,
        List<object> accessors)
    {
        AlignToFourBytes(binary);
        int positionOffset = (int)binary.Position;
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double minZ = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        double maxZ = double.MinValue;
        List<Tiles3DPoint> normals = new List<Tiles3DPoint>();

        foreach (Tiles3DTriangle triangle in triangles)
        {
            Tiles3DPoint normal = CalculateNormal(triangle);
            AppendPoint(binary, triangle.A);
            AppendPoint(binary, triangle.B);
            AppendPoint(binary, triangle.C);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            UpdateExtents(triangle.A, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            UpdateExtents(triangle.B, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            UpdateExtents(triangle.C, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
        }

        int positionByteLength = (int)binary.Position - positionOffset;
        int positionViewIndex = bufferViews.Count;
        bufferViews.Add(new
        {
            buffer = 0,
            byteOffset = positionOffset,
            byteLength = positionByteLength,
            target = 34962
        });

        positionAccessorIndex = accessors.Count;
        accessors.Add(new
        {
            bufferView = positionViewIndex,
            componentType = 5126,
            count = triangles.Count * 3,
            type = "VEC3",
            min = new[] { minX, minY, minZ },
            max = new[] { maxX, maxY, maxZ }
        });

        AlignToFourBytes(binary);
        int normalOffset = (int)binary.Position;
        foreach (Tiles3DPoint normal in normals)
        {
            AppendPoint(binary, normal);
        }

        int normalByteLength = (int)binary.Position - normalOffset;
        int normalViewIndex = bufferViews.Count;
        bufferViews.Add(new
        {
            buffer = 0,
            byteOffset = normalOffset,
            byteLength = normalByteLength,
            target = 34962
        });

        normalAccessorIndex = accessors.Count;
        accessors.Add(new
        {
            bufferView = normalViewIndex,
            componentType = 5126,
            count = triangles.Count * 3,
            type = "VEC3"
        });

        return positionViewIndex;
    }

    private static int AddFeatureIdBufferView(
        MemoryStream binary,
        int vertexCount,
        int featureId,
        List<object> bufferViews,
        out int featureIdAccessorIndex,
        List<object> accessors)
    {
        AlignToFourBytes(binary);
        int featureIdOffset = (int)binary.Position;
        for (int index = 0; index < vertexCount; index++)
        {
            WriteUInt32(binary, (uint)featureId);
        }

        int featureIdByteLength = (int)binary.Position - featureIdOffset;
        int featureIdViewIndex = bufferViews.Count;
        bufferViews.Add(new
        {
            buffer = 0,
            byteOffset = featureIdOffset,
            byteLength = featureIdByteLength,
            target = 34962
        });

        featureIdAccessorIndex = accessors.Count;
        accessors.Add(new
        {
            bufferView = featureIdViewIndex,
            componentType = 5125,
            count = vertexCount,
            type = "SCALAR",
            min = new[] { (uint)featureId },
            max = new[] { (uint)featureId }
        });

        return featureIdViewIndex;
    }

    private static int AddIndexBufferView(
        MemoryStream binary,
        IReadOnlyList<Tiles3DTriangle> triangles,
        List<object> bufferViews,
        out int indexAccessorIndex,
        List<object> accessors)
    {
        AlignToFourBytes(binary);
        int indexOffset = (int)binary.Position;
        uint maxIndex = 0;
        uint nextIndex = 0;
        foreach (Tiles3DTriangle _ in triangles)
        {
            WriteUInt32(binary, nextIndex++);
            WriteUInt32(binary, nextIndex++);
            WriteUInt32(binary, nextIndex++);
            maxIndex = nextIndex - 1;
        }

        int indexByteLength = (int)binary.Position - indexOffset;
        int indexViewIndex = bufferViews.Count;
        bufferViews.Add(new
        {
            buffer = 0,
            byteOffset = indexOffset,
            byteLength = indexByteLength,
            target = 34963
        });

        indexAccessorIndex = accessors.Count;
        accessors.Add(new
        {
            bufferView = indexViewIndex,
            componentType = 5125,
            count = triangles.Count * 3,
            type = "SCALAR",
            min = new[] { 0u },
            max = new[] { maxIndex }
        });

        return indexViewIndex;
    }

    private static object BuildStructuralMetadataExtension(
        MemoryStream binary,
        List<object> bufferViews,
        IReadOnlyList<Tiles3DObjectMetadata> metadata)
    {
        Dictionary<string, object> properties = new Dictionary<string, object>
        {
            ["revitElementId"] = AddStringProperty(binary, bufferViews, metadata.Select(item => item.RevitElementId)),
            ["revitUniqueId"] = AddStringProperty(binary, bufferViews, metadata.Select(item => item.RevitUniqueId)),
            ["name"] = AddStringProperty(binary, bufferViews, metadata.Select(item => item.Name)),
            ["category"] = AddStringProperty(binary, bufferViews, metadata.Select(item => item.Category)),
            ["familyName"] = AddStringProperty(binary, bufferViews, metadata.Select(item => item.FamilyName)),
            ["typeName"] = AddStringProperty(binary, bufferViews, metadata.Select(item => item.TypeName)),
            ["levelName"] = AddStringProperty(binary, bufferViews, metadata.Select(item => item.LevelName)),
            ["levelKey"] = AddStringProperty(binary, bufferViews, metadata.Select(item => item.LevelKey)),
            ["levelElevationMeters"] = AddFloat32Property(binary, bufferViews, metadata.Select(item => item.LevelElevationMeters)),
            ["heightMeters"] = AddFloat32Property(binary, bufferViews, metadata.Select(item => item.HeightMeters)),
            ["minZMeters"] = AddFloat32Property(binary, bufferViews, metadata.Select(item => item.MinZMeters)),
            ["maxZMeters"] = AddFloat32Property(binary, bufferViews, metadata.Select(item => item.MaxZMeters)),
            ["sourceDocument"] = AddStringProperty(binary, bufferViews, metadata.Select(item => item.SourceDocument)),
            ["sourceLinkName"] = AddStringProperty(binary, bufferViews, metadata.Select(item => item.SourceLinkName))
        };

        Dictionary<string, object> propertyTable = new Dictionary<string, object>
        {
            ["class"] = "element",
            ["count"] = metadata.Count,
            ["properties"] = properties
        };

        return new Dictionary<string, object>
        {
            ["schema"] = new
            {
                classes = new Dictionary<string, object>
                {
                    ["element"] = new
                    {
                        properties = new Dictionary<string, object>
                        {
                            ["revitElementId"] = StringPropertySchema(),
                            ["revitUniqueId"] = StringPropertySchema(),
                            ["name"] = StringPropertySchema(),
                            ["category"] = StringPropertySchema(),
                            ["familyName"] = StringPropertySchema(),
                            ["typeName"] = StringPropertySchema(),
                            ["levelName"] = StringPropertySchema(),
                            ["levelKey"] = StringPropertySchema(),
                            ["levelElevationMeters"] = Float32PropertySchema(),
                            ["heightMeters"] = Float32PropertySchema(),
                            ["minZMeters"] = Float32PropertySchema(),
                            ["maxZMeters"] = Float32PropertySchema(),
                            ["sourceDocument"] = StringPropertySchema(),
                            ["sourceLinkName"] = StringPropertySchema()
                        }
                    }
                }
            },
            ["propertyTables"] = new object[] { propertyTable }
        };
    }

    private static object AddStringProperty(
        MemoryStream binary,
        List<object> bufferViews,
        IEnumerable<string> values)
    {
        MemoryStream valueBytes = new MemoryStream();
        List<uint> offsets = new List<uint>();
        foreach (string value in values)
        {
            offsets.Add((uint)valueBytes.Length);
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            valueBytes.Write(bytes, 0, bytes.Length);
        }

        offsets.Add((uint)valueBytes.Length);

        AlignToFourBytes(binary);
        int offsetByteOffset = (int)binary.Position;
        foreach (uint offset in offsets)
        {
            WriteUInt32(binary, offset);
        }

        int offsetByteLength = (int)binary.Position - offsetByteOffset;
        int offsetsViewIndex = bufferViews.Count;
        bufferViews.Add(new
        {
            buffer = 0,
            byteOffset = offsetByteOffset,
            byteLength = offsetByteLength
        });

        AlignToFourBytes(binary);
        int valueByteOffset = (int)binary.Position;
        byte[] bytesArray = valueBytes.ToArray();
        if (bytesArray.Length == 0)
        {
            binary.WriteByte(0);
        }
        else
        {
            binary.Write(bytesArray, 0, bytesArray.Length);
        }

        int valueByteLength = Math.Max(bytesArray.Length, 1);
        int valuesViewIndex = bufferViews.Count;
        bufferViews.Add(new
        {
            buffer = 0,
            byteOffset = valueByteOffset,
            byteLength = valueByteLength
        });

        return new
        {
            values = valuesViewIndex,
            stringOffsets = offsetsViewIndex
        };
    }

    private static object AddFloat32Property(
        MemoryStream binary,
        List<object> bufferViews,
        IEnumerable<double> values)
    {
        AlignToFourBytes(binary);
        int valueByteOffset = (int)binary.Position;
        foreach (double value in values)
        {
            WriteFloat32(binary, value);
        }

        int valueByteLength = (int)binary.Position - valueByteOffset;
        int valuesViewIndex = bufferViews.Count;
        bufferViews.Add(new
        {
            buffer = 0,
            byteOffset = valueByteOffset,
            byteLength = valueByteLength
        });

        return new
        {
            values = valuesViewIndex
        };
    }

    private static object StringPropertySchema()
    {
        return new
        {
            type = "STRING"
        };
    }

    private static object Float32PropertySchema()
    {
        return new
        {
            type = "SCALAR",
            componentType = "FLOAT32"
        };
    }

    private static IReadOnlyList<MaterialTriangleGroup> GroupTrianglesByMaterial(IReadOnlyList<Tiles3DTriangle> triangles)
    {
        List<MaterialTriangleGroup> groups = new List<MaterialTriangleGroup>();
        Dictionary<Tiles3DMaterialColor, int> indexes = new Dictionary<Tiles3DMaterialColor, int>();
        foreach (Tiles3DTriangle triangle in triangles)
        {
            Tiles3DMaterialColor materialColor = triangle.MaterialColor ?? Tiles3DMaterialColor.Default;
            if (!indexes.TryGetValue(materialColor, out int groupIndex))
            {
                groupIndex = groups.Count;
                indexes[materialColor] = groupIndex;
                groups.Add(new MaterialTriangleGroup(materialColor));
            }

            groups[groupIndex].Triangles.Add(triangle);
        }

        return groups;
    }

    private static int GetMaterialIndex(
        Tiles3DMaterialColor materialColor,
        List<Tiles3DMaterialColor> materialColors,
        Dictionary<Tiles3DMaterialColor, int> materialIndexes)
    {
        if (materialIndexes.TryGetValue(materialColor, out int materialIndex))
        {
            return materialIndex;
        }

        materialIndex = materialColors.Count;
        materialIndexes[materialColor] = materialIndex;
        materialColors.Add(materialColor);
        return materialIndex;
    }

    private static object BuildMaterial(Tiles3DMaterialColor color)
    {
        Dictionary<string, object> material = new Dictionary<string, object>
        {
            ["pbrMetallicRoughness"] = new
            {
                baseColorFactor = color.ToBaseColorFactor(),
                metallicFactor = 0d,
                roughnessFactor = 0.95d
            },
            ["doubleSided"] = true
        };

        if (color.Alpha < 255)
        {
            material["alphaMode"] = "BLEND";
        }

        return material;
    }

    private static Tiles3DPoint CalculateNormal(Tiles3DTriangle triangle)
    {
        double ux = triangle.B.X - triangle.A.X;
        double uy = triangle.B.Y - triangle.A.Y;
        double uz = triangle.B.Z - triangle.A.Z;
        double vx = triangle.C.X - triangle.A.X;
        double vy = triangle.C.Y - triangle.A.Y;
        double vz = triangle.C.Z - triangle.A.Z;
        double nx = (uy * vz) - (uz * vy);
        double ny = (uz * vx) - (ux * vz);
        double nz = (ux * vy) - (uy * vx);
        double length = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        if (length < 1e-9)
        {
            return new Tiles3DPoint(0d, 0d, 1d);
        }

        return new Tiles3DPoint(nx / length, ny / length, nz / length);
    }

    private static void UpdateExtents(Tiles3DPoint point, ref double minX, ref double minY, ref double minZ, ref double maxX, ref double maxY, ref double maxZ)
    {
        minX = Math.Min(minX, point.X);
        minY = Math.Min(minY, point.Y);
        minZ = Math.Min(minZ, point.Z);
        maxX = Math.Max(maxX, point.X);
        maxY = Math.Max(maxY, point.Y);
        maxZ = Math.Max(maxZ, point.Z);
    }

    private static void AppendPoint(Stream stream, Tiles3DPoint point)
    {
        WriteFloat32(stream, point.X);
        WriteFloat32(stream, point.Y);
        WriteFloat32(stream, point.Z);
    }

    private static void WriteFloat32(Stream stream, double value)
    {
        byte[] bytes = BitConverter.GetBytes((float)value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void AlignToFourBytes(Stream stream)
    {
        while ((stream.Position % 4) != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static byte[] PadJsonChunk(byte[] jsonBytes)
    {
        int paddedLength = (jsonBytes.Length + 3) & ~3;
        if (paddedLength == jsonBytes.Length)
        {
            return jsonBytes;
        }

        byte[] padded = new byte[paddedLength];
        Buffer.BlockCopy(jsonBytes, 0, padded, 0, jsonBytes.Length);
        for (int index = jsonBytes.Length; index < padded.Length; index++)
        {
            padded[index] = 0x20;
        }

        return padded;
    }

    private static byte[] PadBinaryChunk(byte[] binaryBytes)
    {
        int paddedLength = (binaryBytes.Length + 3) & ~3;
        if (paddedLength == binaryBytes.Length)
        {
            return binaryBytes;
        }

        byte[] padded = new byte[paddedLength];
        Buffer.BlockCopy(binaryBytes, 0, padded, 0, binaryBytes.Length);
        return padded;
    }

    private sealed class GlbDocument
    {
        public string Json { get; set; } = string.Empty;

        public MemoryStream Binary { get; set; } = new MemoryStream();
    }

    private sealed class MaterialTriangleGroup
    {
        public MaterialTriangleGroup(Tiles3DMaterialColor materialColor)
        {
            MaterialColor = materialColor;
        }

        public Tiles3DMaterialColor MaterialColor { get; }

        public List<Tiles3DTriangle> Triangles { get; } = new List<Tiles3DTriangle>();
    }
}
