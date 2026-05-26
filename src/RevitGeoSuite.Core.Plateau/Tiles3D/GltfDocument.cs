using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

public sealed class GltfDocument
{
    [JsonProperty("asset")] public JObject? Asset { get; set; }
    [JsonProperty("accessors")] public List<GltfAccessor>? Accessors { get; set; }
    [JsonProperty("bufferViews")] public List<GltfBufferView>? BufferViews { get; set; }
    [JsonProperty("buffers")] public List<GltfBuffer>? Buffers { get; set; }
    [JsonProperty("meshes")] public List<GltfMesh>? Meshes { get; set; }
    [JsonProperty("nodes")] public List<GltfNode>? Nodes { get; set; }
    [JsonProperty("scenes")] public List<GltfScene>? Scenes { get; set; }
    [JsonProperty("scene")] public int? Scene { get; set; }
    [JsonProperty("extensionsUsed")] public List<string>? ExtensionsUsed { get; set; }
    [JsonProperty("extensionsRequired")] public List<string>? ExtensionsRequired { get; set; }
    [JsonProperty("extensions")] public JObject? Extensions { get; set; }
}

public sealed class GltfAccessor
{
    [JsonProperty("bufferView")] public int? BufferView { get; set; }
    [JsonProperty("byteOffset")] public int? ByteOffset { get; set; }
    [JsonProperty("componentType")] public int ComponentType { get; set; }
    [JsonProperty("count")] public int Count { get; set; }
    [JsonProperty("type")] public string? Type { get; set; }
    [JsonProperty("min")] public double[]? Min { get; set; }
    [JsonProperty("max")] public double[]? Max { get; set; }
}

public sealed class GltfBufferView
{
    [JsonProperty("buffer")] public int Buffer { get; set; }
    [JsonProperty("byteOffset")] public int? ByteOffset { get; set; }
    [JsonProperty("byteLength")] public int ByteLength { get; set; }
    [JsonProperty("byteStride")] public int? ByteStride { get; set; }
}

public sealed class GltfBuffer
{
    [JsonProperty("byteLength")] public int ByteLength { get; set; }
    [JsonProperty("uri")] public string? Uri { get; set; }
}

public sealed class GltfMesh
{
    [JsonProperty("primitives")] public List<GltfPrimitive>? Primitives { get; set; }
}

public sealed class GltfPrimitive
{
    [JsonProperty("attributes")] public Dictionary<string, int>? Attributes { get; set; }
    [JsonProperty("indices")] public int? Indices { get; set; }
    [JsonProperty("mode")] public int? Mode { get; set; }
    [JsonProperty("material")] public int? Material { get; set; }
    [JsonProperty("extensions")] public Dictionary<string, JObject>? Extensions { get; set; }
}

public sealed class GltfNode
{
    [JsonProperty("mesh")] public int? Mesh { get; set; }
    [JsonProperty("matrix")] public double[]? Matrix { get; set; }
    [JsonProperty("translation")] public double[]? Translation { get; set; }
    [JsonProperty("rotation")] public double[]? Rotation { get; set; }
    [JsonProperty("scale")] public double[]? Scale { get; set; }
    [JsonProperty("children")] public List<int>? Children { get; set; }
}

public sealed class GltfScene
{
    [JsonProperty("nodes")] public List<int>? Nodes { get; set; }
}
