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
        GltfBufferView view = doc.BufferViews?[bufferViewIndex] ?? throw new InvalidOperationException("Buffer view missing.");
        int start = view.ByteOffset ?? 0;
        if (start < 0 || start + view.ByteLength > bin.Length)
        {
            throw new InvalidOperationException("Buffer view out of range.");
        }
        return new ArraySegment<byte>(bin, start, view.ByteLength);
    }

    private static float[] ReadVec3FloatAccessor(GltfDocument doc, byte[] bin, int accessorIndex)
    {
        GltfAccessor accessor = doc.Accessors?[accessorIndex] ?? throw new InvalidOperationException("Accessor missing.");
        if (accessor.Type != "VEC3" || accessor.ComponentType != 5126)
        {
            throw new InvalidOperationException($"Expected VEC3 FLOAT accessor at {accessorIndex}.");
        }
        ArraySegment<byte> view = SliceBufferView(doc, bin, accessor.BufferView ?? throw new InvalidOperationException("Accessor missing bufferView."));
        int baseOffset = view.Offset + (accessor.ByteOffset ?? 0);
        float[] result = new float[accessor.Count * 3];
        Buffer.BlockCopy(view.Array!, baseOffset, result, 0, result.Length * sizeof(float));
        return result;
    }

    private static uint[] ReadScalarUInt32Accessor(GltfDocument doc, byte[] bin, int accessorIndex)
    {
        GltfAccessor accessor = doc.Accessors?[accessorIndex] ?? throw new InvalidOperationException("Accessor missing.");
        if (accessor.Type != "SCALAR") throw new InvalidOperationException("Expected SCALAR accessor.");
        ArraySegment<byte> view = SliceBufferView(doc, bin, accessor.BufferView ?? throw new InvalidOperationException("Accessor missing bufferView."));
        int baseOffset = view.Offset + (accessor.ByteOffset ?? 0);
        uint[] result = new uint[accessor.Count];
        switch (accessor.ComponentType)
        {
            case 5121: // UNSIGNED_BYTE
                for (int i = 0; i < result.Length; i++) result[i] = view.Array![baseOffset + i];
                break;
            case 5123: // UNSIGNED_SHORT
                for (int i = 0; i < result.Length; i++) result[i] = BitConverter.ToUInt16(view.Array!, baseOffset + i * 2);
                break;
            case 5125: // UNSIGNED_INT
                for (int i = 0; i < result.Length; i++) result[i] = BitConverter.ToUInt32(view.Array!, baseOffset + i * 4);
                break;
            case 5126: // FLOAT (some encoders use float for batch IDs)
                for (int i = 0; i < result.Length; i++) result[i] = (uint)BitConverter.ToSingle(view.Array!, baseOffset + i * 4);
                break;
            default:
                throw new InvalidOperationException($"Unsupported scalar component type {accessor.ComponentType}.");
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
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            uint ia = indices[t + 0];
            uint ib = indices[t + 1];
            uint ic = indices[t + 2];
            Vector3d a = ReadVertex(pos, (int)ia, worldRtc);
            Vector3d b = ReadVertex(pos, (int)ib, worldRtc);
            Vector3d c = ReadVertex(pos, (int)ic, worldRtc);

            int batchId = batchIds is null ? 0 : (int)batchIds[(int)ia];
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
