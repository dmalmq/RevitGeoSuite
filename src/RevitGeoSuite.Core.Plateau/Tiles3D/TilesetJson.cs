using System.Collections.Generic;
using Newtonsoft.Json;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

public sealed class TilesetJson
{
    [JsonProperty("asset")]
    public TilesetAsset? Asset { get; set; }

    [JsonProperty("geometricError")]
    public double? GeometricError { get; set; }

    [JsonProperty("root")]
    public TilesetTile? Root { get; set; }
}

public sealed class TilesetAsset
{
    [JsonProperty("version")]
    public string? Version { get; set; }

    [JsonProperty("tilesetVersion")]
    public string? TilesetVersion { get; set; }
}

public sealed class TilesetTile
{
    [JsonProperty("boundingVolume")]
    public TilesetBoundingVolume? BoundingVolume { get; set; }

    [JsonProperty("geometricError")]
    public double? GeometricError { get; set; }

    [JsonProperty("refine")]
    public string? Refine { get; set; }

    [JsonProperty("transform")]
    public double[]? Transform { get; set; }

    [JsonProperty("content")]
    public TilesetContent? Content { get; set; }

    [JsonProperty("children")]
    public List<TilesetTile>? Children { get; set; }
}

public sealed class TilesetBoundingVolume
{
    [JsonProperty("box")]
    public double[]? Box { get; set; }

    [JsonProperty("region")]
    public double[]? Region { get; set; }

    [JsonProperty("sphere")]
    public double[]? Sphere { get; set; }
}

public sealed class TilesetContent
{
    [JsonProperty("uri")]
    public string? Uri { get; set; }

    [JsonProperty("url")]
    public string? Url { get; set; }

    public string? ResolvedUri => string.IsNullOrEmpty(Uri) ? Url : Uri;
}
