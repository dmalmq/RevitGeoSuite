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
        List<object> accessors = new List<object>();
        List<object> bufferViews = new List<object>();
        List<object> primitives = new List<object>();
        MemoryStream binary = new MemoryStream();

        IReadOnlyList<Tiles3DMeshPrimitive> activeMeshes = package.Meshes.Where(mesh => mesh.Triangles.Count > 0).ToArray();
        Dictionary<Tiles3DMaterialColor, int> materialIndexMap = new Dictionary<Tiles3DMaterialColor, int>();
        foreach (Tiles3DMeshPrimitive mesh in activeMeshes)
        {
            if (!materialIndexMap.ContainsKey(mesh.Color))
            {
                materialIndexMap[mesh.Color] = materialIndexMap.Count;
            }
        }

        foreach (Tiles3DMeshPrimitive mesh in activeMeshes)
        {
            int positionView = AddPositionBufferView(binary, mesh, bufferViews, out int positionAccessor, out int normalAccessor, accessors);
            int normalView = positionView + 1;
            int indexView = AddIndexBufferView(binary, mesh, bufferViews, out int indexAccessor, accessors);

            primitives.Add(new
            {
                attributes = new
                {
                    POSITION = positionAccessor,
                    NORMAL = normalAccessor
                },
                indices = indexAccessor,
                material = materialIndexMap[mesh.Color],
                mode = 4
            });
        }

        object document = new
        {
            asset = new
            {
                version = "2.0",
                generator = "RevitGeoSuite.Tiles3DExport"
            },
            scene = 0,
            scenes = new object[]
            {
                new
                {
                    nodes = new[] { 0 }
                }
            },
            nodes = new object[]
            {
                new
                {
                    name = "Root",
                    mesh = 0,
                    matrix = ZUpToYUpMatrix
                }
            },
            meshes = new object[]
            {
                new
                {
                    primitives = primitives
                }
            },
            materials = BuildMaterials(materialIndexMap),
            accessors = accessors,
            bufferViews = bufferViews,
            buffers = new object[]
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

    private static object[] BuildMaterials(Dictionary<Tiles3DMaterialColor, int> materialIndexMap)
    {
        object[] materials = new object[materialIndexMap.Count];
        foreach (KeyValuePair<Tiles3DMaterialColor, int> entry in materialIndexMap)
        {
            materials[entry.Value] = new
            {
                pbrMetallicRoughness = new
                {
                    baseColorFactor = entry.Key.ToNormalizedArray(),
                    metallicFactor = 0d,
                    roughnessFactor = 0.95d
                },
                doubleSided = true
            };
        }

        return materials;
    }

    private static int AddPositionBufferView(
        MemoryStream binary,
        Tiles3DMeshPrimitive mesh,
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

        foreach (Tiles3DTriangle triangle in mesh.Triangles)
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
            count = mesh.Triangles.Count * 3,
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
            count = mesh.Triangles.Count * 3,
            type = "VEC3"
        });

        return positionViewIndex;
    }

    private static int AddIndexBufferView(
        MemoryStream binary,
        Tiles3DMeshPrimitive mesh,
        List<object> bufferViews,
        out int indexAccessorIndex,
        List<object> accessors)
    {
        AlignToFourBytes(binary);
        int indexOffset = (int)binary.Position;
        uint maxIndex = 0;
        uint nextIndex = 0;
        foreach (Tiles3DTriangle _ in mesh.Triangles)
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
            count = mesh.Triangles.Count * 3,
            type = "SCALAR",
            min = new[] { 0u },
            max = new[] { maxIndex }
        });

        return indexViewIndex;
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
}
