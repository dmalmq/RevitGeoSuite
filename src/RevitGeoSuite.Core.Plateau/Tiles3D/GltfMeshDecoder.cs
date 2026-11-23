using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

public sealed class GltfMeshDecoder
{
    private readonly IDracoMeshDecoder dracoDecoder;

    public GltfMeshDecoder(IDracoMeshDecoder dracoDecoder)
    {
        this.dracoDecoder = dracoDecoder ?? throw new ArgumentNullException(nameof(dracoDecoder));
    }

    /// <summary>
    /// Decodes the meshes in a single b3dm payload into per-batch grouped triangle lists.
    /// Coordinates are in the b3dm's local frame (caller applies tile transform + RTC center).
    /// </summary>
    public IReadOnlyList<PlateauTilesetFeatureMesh> Decode(B3dmContents b3dm)
    {
        if (b3dm is null) throw new ArgumentNullException(nameof(b3dm));

        GlbContents glb = GlbReader.Read(b3dm.GltfBytes);
        GltfDocument doc = JsonConvert.DeserializeObject<GltfDocument>(glb.Json)
            ?? throw new InvalidOperationException("glTF JSON was empty.");

        // Both CESIUM_RTC.center and b3dm RTC_CENTER are translations in the Z-up 3D
        // Tiles world frame (matches Cesium's Model.js). They apply *after* the glTF
        // Y-up -> Z-up rotation, so we combine and translate post-rotation. Adding
        // them before the rotation would spin large ECEF magnitudes 90 degrees and
        // throw vertices millions of metres off-anchor.
        Vector3d worldRtc = ExtractCesiumRtcCenter(doc) + (b3dm.RtcCenter ?? Vector3d.Zero);
        int batchLength = b3dm.BatchLength ?? 0;

        Dictionary<int, List<PlateauTilesetTriangle>> byBatch = new Dictionary<int, List<PlateauTilesetTriangle>>();

        if (doc.Meshes is null) return Array.Empty<PlateauTilesetFeatureMesh>();

        foreach (GltfMesh mesh in doc.Meshes)
        {
            if (mesh.Primitives is null) continue;
            foreach (GltfPrimitive primitive in mesh.Primitives)
            {
                DecodedPrimitive decoded = DecodePrimitive(primitive, doc, glb.Binary);
                if (decoded.VertexCount == 0 || decoded.Indices.Length == 0) continue;
                AppendTrianglesByBatch(decoded, worldRtc, byBatch);
            }
        }

        List<PlateauTilesetFeatureMesh> result = new List<PlateauTilesetFeatureMesh>(byBatch.Count);
        foreach (KeyValuePair<int, List<PlateauTilesetTriangle>> kvp in byBatch)
        {
            IReadOnlyDictionary<string, object?> attrs = BatchTableReader.ReadAttributesForBatch(
                b3dm.BatchTableJson, b3dm.BatchTableBinary, kvp.Key, batchLength);
            result.Add(new PlateauTilesetFeatureMesh(kvp.Key, kvp.Value, attrs));
        }
        return result;
    }

    private static Vector3d ExtractCesiumRtcCenter(GltfDocument doc)
    {
        if (doc.Extensions?["CESIUM_RTC"] is JObject rtc)
        {
            JArray? center = rtc["center"] as JArray;
            if (center is not null && center.Count == 3)
            {
                return new Vector3d((double)center[0], (double)center[1], (double)center[2]);
            }
        }
        return Vector3d.Zero;
    }

    private DecodedPrimitive DecodePrimitive(GltfPrimitive primitive, GltfDocument doc, byte[] bin)
    {
        if (primitive.Extensions is not null && primitive.Extensions.TryGetValue("KHR_draco_mesh_compression", out JObject? dracoExt) && dracoExt is not null)
        {
            return DecodeDracoPrimitive(dracoExt, doc, bin);
        }
        return DecodePlainPrimitive(primitive, doc, bin);
    }

    private DecodedPrimitive DecodeDracoPrimitive(JObject dracoExt, GltfDocument doc, byte[] bin)
    {
        int bufferViewIndex = dracoExt["bufferView"]?.Value<int>() ?? throw new InvalidOperationException("Draco extension missing bufferView.");
        Dictionary<string, int> attributeIds = new Dictionary<string, int>(StringComparer.Ordinal);
        JObject? attrs = dracoExt["attributes"] as JObject;
        if (attrs is not null)
        {
            foreach (var prop in attrs.Properties())
            {
                attributeIds[prop.Name] = prop.Value.Value<int>();
            }
        }

        if (!attributeIds.TryGetValue("POSITION", out int positionId))
        {
            throw new InvalidOperationException("Draco primitive missing POSITION attribute.");
        }
        int batchIdAttrId = attributeIds.TryGetValue("_BATCHID", out int b) ? b : -1;
        int normalAttrId = attributeIds.TryGetValue("NORMAL", out int n) ? n : -1;

        ArraySegment<byte> dracoBytes = SliceBufferView(doc, bin, bufferViewIndex);
        DracoMeshAttributes dracoAttributes = new DracoMeshAttributes(positionId, batchIdAttrId, normalAttrId);
        DracoDecodedMesh decoded = dracoDecoder.Decode(dracoBytes.AsSpan(), dracoAttributes);

        return new DecodedPrimitive(decoded.Positions, decoded.BatchIds, decoded.Indices, decoded.VertexCount, decoded.Indices.Length);
    }

    private static DecodedPrimitive DecodePlainPrimitive(GltfPrimitive primitive, GltfDocument doc, byte[] bin)
    {
        if (primitive.Attributes is null || !primitive.Attributes.TryGetValue("POSITION", out int positionAccessor))
        {
            throw new InvalidOperationException("Primitive missing POSITION attribute.");
        }
        float[] positions = ReadVec3FloatAccessor(doc, bin, positionAccessor);
        uint[]? batchIds = null;
        if (primitive.Attributes.TryGetValue("_BATCHID", out int batchAccessor))
        {
            batchIds = ReadScalarUInt32Accessor(doc, bin, batchAccessor);
        }
        uint[] indices = primitive.Indices.HasValue
            ? ReadIndicesAccessor(doc, bin, primitive.Indices.Value)
            : BuildSequentialIndices(positions.Length / 3);
        return new DecodedPrimitive(positions, batchIds, indices, positions.Length / 3, indices.Length);
    }

    private static ArraySegment<byte> SliceBufferView(GltfDocument doc, byte[] bin, int bufferViewIndex)
    {
        if (doc.BufferViews is null || bufferViewIndex < 0 || bufferViewIndex >= doc.BufferViews.Count)
        {
            throw new InvalidOperationException("Buffer view missing or index out of range.");
        }

        GltfBufferView view = doc.BufferViews[bufferViewIndex];
        int start = view.ByteOffset ?? 0;
        if (start < 0 || view.ByteLength < 0 || (long)start + view.ByteLength > bin.Length)
        {
            throw new InvalidOperationException("Buffer view out of range.");
        }
        return new ArraySegment<byte>(bin, start, view.ByteLength);
    }

    private static GltfAccessor GetAccessor(GltfDocument doc, int accessorIndex)
    {
        if (doc.Accessors is null || accessorIndex < 0 || accessorIndex >= doc.Accessors.Count)
        {
            throw new InvalidOperationException("Accessor missing or index out of range.");
        }

        GltfAccessor accessor = doc.Accessors[accessorIndex];
        if (accessor.Count < 0)
        {
            throw new InvalidOperationException("Accessor count cannot be negative.");
        }

        if ((accessor.ByteOffset ?? 0) < 0)
        {
            throw new InvalidOperationException("Accessor byteOffset cannot be negative.");
        }

        return accessor;
    }

    private static void ValidateAccessorRange(int accessorByteOffset, int count, int elementSizeBytes, int strideBytes, int viewLength)
    {
        if (strideBytes < elementSizeBytes)
        {
            throw new InvalidOperationException($"Buffer view byteStride ({strideBytes}) is smaller than element size ({elementSizeBytes}).");
        }

        if (count == 0)
        {
            return;
        }

        long lastByteEnd = (long)accessorByteOffset + ((long)(count - 1) * strideBytes) + elementSizeBytes;
        if (lastByteEnd > viewLength)
        {
            throw new InvalidOperationException("Accessor range exceeds buffer view length.");
        }
    }

    private static float[] ReadVec3FloatAccessor(GltfDocument doc, byte[] bin, int accessorIndex)
    {
        GltfAccessor accessor = GetAccessor(doc, accessorIndex);
        if (accessor.Type != "VEC3" || accessor.ComponentType != 5126)
        {
            throw new InvalidOperationException($"Expected VEC3 FLOAT accessor at {accessorIndex}.");
        }

        int bufferViewIndex = accessor.BufferView ?? throw new InvalidOperationException("Accessor missing bufferView.");
        ArraySegment<byte> view = SliceBufferView(doc, bin, bufferViewIndex);
        GltfBufferView bufferView = doc.BufferViews![bufferViewIndex];
        const int elementSize = 3 * sizeof(float);
        int stride = bufferView.ByteStride ?? elementSize;
        int accessorOffset = accessor.ByteOffset ?? 0;
        ValidateAccessorRange(accessorOffset, accessor.Count, elementSize, stride, view.Count);

        int baseOffset = view.Offset + accessorOffset;
        float[] result = new float[accessor.Count * 3];
        if (stride == elementSize)
        {
            Buffer.BlockCopy(view.Array!, baseOffset, result, 0, result.Length * sizeof(float));
        }
        else
        {
            byte[] source = view.Array!;
            for (int i = 0; i < accessor.Count; i++)
            {
                int elemOffset = baseOffset + (i * stride);
                result[i * 3 + 0] = BitConverter.ToSingle(source, elemOffset);
                result[i * 3 + 1] = BitConverter.ToSingle(source, elemOffset + 4);
                result[i * 3 + 2] = BitConverter.ToSingle(source, elemOffset + 8);
            }
        }
        return result;
    }

    private static uint[] ReadScalarUInt32Accessor(GltfDocument doc, byte[] bin, int accessorIndex)
    {
        GltfAccessor accessor = GetAccessor(doc, accessorIndex);
        if (accessor.Type != "SCALAR") throw new InvalidOperationException("Expected SCALAR accessor.");

        int bufferViewIndex = accessor.BufferView ?? throw new InvalidOperationException("Accessor missing bufferView.");
        ArraySegment<byte> view = SliceBufferView(doc, bin, bufferViewIndex);
        GltfBufferView bufferView = doc.BufferViews![bufferViewIndex];
        int elementSize = accessor.ComponentType switch
        {
            5121 => 1,
            5123 => 2,
            5125 => 4,
            5126 => 4,
            _ => throw new InvalidOperationException($"Unsupported scalar component type {accessor.ComponentType}.")
        };
        int stride = bufferView.ByteStride ?? elementSize;
        int accessorOffset = accessor.ByteOffset ?? 0;
        ValidateAccessorRange(accessorOffset, accessor.Count, elementSize, stride, view.Count);

        int baseOffset = view.Offset + accessorOffset;
        byte[] source = view.Array!;
        uint[] result = new uint[accessor.Count];
        switch (accessor.ComponentType)
        {
            case 5121: // UNSIGNED_BYTE
                for (int i = 0; i < result.Length; i++) result[i] = source[baseOffset + (i * stride)];
                break;
            case 5123: // UNSIGNED_SHORT
                for (int i = 0; i < result.Length; i++) result[i] = BitConverter.ToUInt16(source, baseOffset + (i * stride));
                break;
            case 5125: // UNSIGNED_INT
                for (int i = 0; i < result.Length; i++) result[i] = BitConverter.ToUInt32(source, baseOffset + (i * stride));
                break;
            case 5126: // FLOAT (some encoders use float for batch IDs)
                for (int i = 0; i < result.Length; i++) result[i] = (uint)BitConverter.ToSingle(source, baseOffset + (i * stride));
                break;
        }
        return result;
    }

    private static uint[] ReadIndicesAccessor(GltfDocument doc, byte[] bin, int accessorIndex) => ReadScalarUInt32Accessor(doc, bin, accessorIndex);

    private static uint[] BuildSequentialIndices(int vertexCount)
    {
        uint[] indices = new uint[vertexCount];
        for (int i = 0; i < indices.Length; i++) indices[i] = (uint)i;
        return indices;
    }

    private static void AppendTrianglesByBatch(DecodedPrimitive primitive, Vector3d worldRtc, Dictionary<int, List<PlateauTilesetTriangle>> byBatch)
    {
        uint[] indices = primitive.Indices;
        float[] pos = primitive.Positions;
        uint[]? batchIds = primitive.BatchIds;
        int maxVertexCount = Math.Min(primitive.VertexCount, pos.Length / 3);
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            uint ia = indices[t + 0];
            uint ib = indices[t + 1];
            uint ic = indices[t + 2];
            if (ia >= (uint)maxVertexCount || ib >= (uint)maxVertexCount || ic >= (uint)maxVertexCount)
            {
                throw new InvalidOperationException("glTF triangle vertex index is out of range.");
            }

            Vector3d a = ReadVertex(pos, (int)ia, worldRtc);
            Vector3d b = ReadVertex(pos, (int)ib, worldRtc);
            Vector3d c = ReadVertex(pos, (int)ic, worldRtc);

            int batchId;
            if (batchIds is null)
            {
                batchId = 0;
            }
            else if (ia >= (uint)batchIds.Length)
            {
                throw new InvalidOperationException("glTF _BATCHID accessor is shorter than vertex index.");
            }
            else
            {
                batchId = (int)batchIds[(int)ia];
            }

            if (!byBatch.TryGetValue(batchId, out List<PlateauTilesetTriangle>? list))
            {
                list = new List<PlateauTilesetTriangle>();
                byBatch[batchId] = list;
            }
            list.Add(new PlateauTilesetTriangle(a, b, c));
        }
    }

    private static Vector3d ReadVertex(float[] positions, int vertexIndex, Vector3d worldRtc)
    {
        int o = vertexIndex * 3;
        double px = positions[o];
        double py = positions[o + 1];
        double pz = positions[o + 2];
        // glTF Y-up -> 3D Tiles Z-up: (x, y, z) -> (x, -z, y). worldRtc is already Z-up.
        return new Vector3d(px + worldRtc.X, -pz + worldRtc.Y, py + worldRtc.Z);
    }

    private readonly struct DecodedPrimitive
    {
        public readonly float[] Positions;
        public readonly uint[]? BatchIds;
        public readonly uint[] Indices;
        public readonly int VertexCount;
        public readonly int IndexCount;

        public DecodedPrimitive(float[] positions, uint[]? batchIds, uint[] indices, int vertexCount, int indexCount)
        {
            Positions = positions;
            BatchIds = batchIds;
            Indices = indices;
            VertexCount = vertexCount;
            IndexCount = indexCount;
        }
    }
}
