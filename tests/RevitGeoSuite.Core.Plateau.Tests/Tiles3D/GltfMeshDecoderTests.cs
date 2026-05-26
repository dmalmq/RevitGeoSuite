using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Tiles3D;

public sealed class GltfMeshDecoderTests
{
    [Fact]
    public void Decode_handles_non_Draco_indexed_triangle_mesh_and_groups_by_batch_id()
    {
        // Two unrelated triangles (no shared vertices) — matches real PLATEAU batching.
        // Verts 0,1,2 -> batch 0 (a building); verts 3,4,5 -> batch 1 (another building).
        float[] positions =
        {
            0, 0, 0,
            1, 0, 0,
            0, 1, 0,
            5, 0, 0,
            6, 0, 0,
            5, 1, 0,
        };
        ushort[] batchIds = { 0, 0, 0, 1, 1, 1 };
        ushort[] indices = { 0, 1, 2, 3, 4, 5 };

        byte[] bin = ConcatLE(positions, indices, batchIds);
        int bvPosLen = positions.Length * 4;
        int bvIdxLen = indices.Length * 2;
        int bvBatchLen = batchIds.Length * 2;

        string json = "{" +
            "\"asset\":{\"version\":\"2.0\"}," +
            "\"buffers\":[{\"byteLength\":" + bin.Length + "}]," +
            "\"bufferViews\":[" +
                "{\"buffer\":0,\"byteOffset\":0,\"byteLength\":" + bvPosLen + "}," +
                "{\"buffer\":0,\"byteOffset\":" + bvPosLen + ",\"byteLength\":" + bvIdxLen + "}," +
                "{\"buffer\":0,\"byteOffset\":" + (bvPosLen + bvIdxLen) + ",\"byteLength\":" + bvBatchLen + "}" +
            "]," +
            "\"accessors\":[" +
                "{\"bufferView\":0,\"componentType\":5126,\"count\":6,\"type\":\"VEC3\"}," +
                "{\"bufferView\":1,\"componentType\":5123,\"count\":6,\"type\":\"SCALAR\"}," +
                "{\"bufferView\":2,\"componentType\":5123,\"count\":6,\"type\":\"SCALAR\"}" +
            "]," +
            "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"_BATCHID\":2},\"indices\":1,\"mode\":4}]}]," +
            "\"nodes\":[{\"mesh\":0}],\"scenes\":[{\"nodes\":[0]}],\"scene\":0" +
        "}";

        byte[] glb = BuildGlb(json, bin);
        byte[] b3dm = BuildB3dm("{\"BATCH_LENGTH\":2}", "{\"gml_id\":[\"feature-a\",\"feature-b\"]}", glb);

        GltfMeshDecoder decoder = new GltfMeshDecoder(new MissingDracoMeshDecoder());
        B3dmContents b3dmContents = B3dmParser.Parse(b3dm);
        IReadOnlyList<PlateauTilesetFeatureMesh> features = decoder.Decode(b3dmContents);

        Assert.Equal(2, features.Count);
        PlateauTilesetFeatureMesh batch0 = features.Single(f => f.BatchId == 0);
        Assert.Single(batch0.Triangles);
        Assert.Equal("feature-a", batch0.GmlId);

        PlateauTilesetFeatureMesh batch1 = features.Single(f => f.BatchId == 1);
        Assert.Single(batch1.Triangles);
        Assert.Equal("feature-b", batch1.GmlId);
    }

    [Fact]
    public void Decode_routes_Draco_primitives_through_the_draco_decoder()
    {
        // Minimal glTF that says the only primitive is Draco-compressed.
        string json = "{" +
            "\"asset\":{\"version\":\"2.0\"}," +
            "\"buffers\":[{\"byteLength\":4}]," +
            "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":4}]," +
            "\"meshes\":[{\"primitives\":[{\"attributes\":{},\"extensions\":{\"KHR_draco_mesh_compression\":{\"bufferView\":0,\"attributes\":{\"POSITION\":0,\"_BATCHID\":1}}}}]}]" +
        "}";
        byte[] glb = BuildGlb(json, new byte[] { 0, 1, 2, 3 });
        byte[] b3dm = BuildB3dm("{\"BATCH_LENGTH\":1}", "{\"gml_id\":[\"feature-x\"]}", glb);

        FakeDracoDecoder draco = new FakeDracoDecoder();
        GltfMeshDecoder decoder = new GltfMeshDecoder(draco);
        IReadOnlyList<PlateauTilesetFeatureMesh> features = decoder.Decode(B3dmParser.Parse(b3dm));

        Assert.True(draco.DecodeWasCalled);
        Assert.Single(features);
        Assert.Equal("feature-x", features[0].GmlId);
    }

    [Fact]
    public void Decode_translates_after_yup_zup_rotation_when_CESIUM_RTC_present()
    {
        // Single glTF Y-up vertex (1, 10, 5) with a non-zero CESIUM_RTC center
        // (100, 200, 300) given in the Z-up 3D Tiles world frame. Per Cesium's
        // Model.js the RTC center must translate AFTER the Y-up -> Z-up rotation, so
        // the decoded vertex must be (1 + 100, -5 + 200, 10 + 300) = (101, 195, 310).
        // If the implementation rotated the RTC center along with the vertex (the
        // earlier regression), output would instead be (101, -205, 210).
        float[] positions = { 1, 10, 5 };
        ushort[] batchIds = { 0 };
        ushort[] indices = { 0, 0, 0 };

        byte[] bin = ConcatLE(positions, indices, batchIds);
        int bvPosLen = positions.Length * 4;
        int bvIdxLen = indices.Length * 2;
        int bvBatchLen = batchIds.Length * 2;

        string json = "{" +
            "\"asset\":{\"version\":\"2.0\"}," +
            "\"extensionsUsed\":[\"CESIUM_RTC\"]," +
            "\"extensions\":{\"CESIUM_RTC\":{\"center\":[100,200,300]}}," +
            "\"buffers\":[{\"byteLength\":" + bin.Length + "}]," +
            "\"bufferViews\":[" +
                "{\"buffer\":0,\"byteOffset\":0,\"byteLength\":" + bvPosLen + "}," +
                "{\"buffer\":0,\"byteOffset\":" + bvPosLen + ",\"byteLength\":" + bvIdxLen + "}," +
                "{\"buffer\":0,\"byteOffset\":" + (bvPosLen + bvIdxLen) + ",\"byteLength\":" + bvBatchLen + "}" +
            "]," +
            "\"accessors\":[" +
                "{\"bufferView\":0,\"componentType\":5126,\"count\":1,\"type\":\"VEC3\"}," +
                "{\"bufferView\":1,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}," +
                "{\"bufferView\":2,\"componentType\":5123,\"count\":1,\"type\":\"SCALAR\"}" +
            "]," +
            "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"_BATCHID\":2},\"indices\":1,\"mode\":4}]}]," +
            "\"nodes\":[{\"mesh\":0}],\"scenes\":[{\"nodes\":[0]}],\"scene\":0" +
        "}";

        byte[] glb = BuildGlb(json, bin);
        byte[] b3dm = BuildB3dm("{\"BATCH_LENGTH\":1}", "{\"gml_id\":[\"feature-a\"]}", glb);

        GltfMeshDecoder decoder = new GltfMeshDecoder(new MissingDracoMeshDecoder());
        IReadOnlyList<PlateauTilesetFeatureMesh> features = decoder.Decode(B3dmParser.Parse(b3dm));

        PlateauTilesetTriangle tri = Assert.Single(Assert.Single(features).Triangles);
        Assert.Equal(101.0, tri.A.X, 6);
        Assert.Equal(195.0, tri.A.Y, 6);
        Assert.Equal(310.0, tri.A.Z, 6);
    }

    private sealed class FakeDracoDecoder : IDracoMeshDecoder
    {
        public bool DecodeWasCalled { get; private set; }

        public DracoDecodedMesh Decode(ReadOnlySpan<byte> dracoBuffer, DracoMeshAttributes attributes)
        {
            DecodeWasCalled = true;
            float[] positions = { 0, 0, 0, 1, 0, 0, 1, 1, 0 };
            uint[] batchIds = { 0, 0, 0 };
            uint[] indices = { 0, 1, 2 };
            return new DracoDecodedMesh(positions, batchIds, indices);
        }
    }

    private static byte[] ConcatLE(float[] positions, ushort[] indices, ushort[] batchIds)
    {
        byte[] result = new byte[positions.Length * 4 + indices.Length * 2 + batchIds.Length * 2];
        int offset = 0;
        Buffer.BlockCopy(positions, 0, result, offset, positions.Length * 4); offset += positions.Length * 4;
        Buffer.BlockCopy(indices, 0, result, offset, indices.Length * 2); offset += indices.Length * 2;
        Buffer.BlockCopy(batchIds, 0, result, offset, batchIds.Length * 2);
        return result;
    }

    private static byte[] BuildGlb(string json, byte[] bin)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(PadTo4(json));
        // Pad bin chunk to multiple of 4 bytes per spec
        int binPadding = (4 - (bin.Length % 4)) % 4;
        int binPaddedLen = bin.Length + binPadding;
        int total = 12 + 8 + jsonBytes.Length + 8 + binPaddedLen;
        byte[] glb = new byte[total];
        Encoding.ASCII.GetBytes("glTF", 0, 4, glb, 0);
        BitConverter.GetBytes(2).CopyTo(glb, 4);
        BitConverter.GetBytes(total).CopyTo(glb, 8);

        int offset = 12;
        BitConverter.GetBytes(jsonBytes.Length).CopyTo(glb, offset);
        Encoding.ASCII.GetBytes("JSON", 0, 4, glb, offset + 4);
        Buffer.BlockCopy(jsonBytes, 0, glb, offset + 8, jsonBytes.Length);
        offset += 8 + jsonBytes.Length;

        BitConverter.GetBytes(binPaddedLen).CopyTo(glb, offset);
        glb[offset + 4] = (byte)'B';
        glb[offset + 5] = (byte)'I';
        glb[offset + 6] = (byte)'N';
        glb[offset + 7] = 0x00;
        Buffer.BlockCopy(bin, 0, glb, offset + 8, bin.Length);
        return glb;
    }

    private static byte[] BuildB3dm(string featureTableJson, string batchTableJson, byte[] gltf)
    {
        byte[] ftj = Encoding.UTF8.GetBytes(PadTo4(featureTableJson));
        byte[] btj = batchTableJson.Length == 0 ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(PadTo4(batchTableJson));
        int total = 28 + ftj.Length + btj.Length + gltf.Length;
        byte[] payload = new byte[total];
        Encoding.ASCII.GetBytes("b3dm", 0, 4, payload, 0);
        BitConverter.GetBytes(1).CopyTo(payload, 4);
        BitConverter.GetBytes(total).CopyTo(payload, 8);
        BitConverter.GetBytes(ftj.Length).CopyTo(payload, 12);
        BitConverter.GetBytes(0).CopyTo(payload, 16);
        BitConverter.GetBytes(btj.Length).CopyTo(payload, 20);
        BitConverter.GetBytes(0).CopyTo(payload, 24);
        int offset = 28;
        Buffer.BlockCopy(ftj, 0, payload, offset, ftj.Length); offset += ftj.Length;
        Buffer.BlockCopy(btj, 0, payload, offset, btj.Length); offset += btj.Length;
        Buffer.BlockCopy(gltf, 0, payload, offset, gltf.Length);
        return payload;
    }

    private static string PadTo4(string text)
    {
        int padding = (4 - (text.Length % 4)) % 4;
        return text + new string(' ', padding);
    }
}
