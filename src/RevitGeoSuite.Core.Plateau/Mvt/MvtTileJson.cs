using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace RevitGeoSuite.Core.Plateau.Mvt;

/// <summary>
/// Parsed TileJSON (the PLATEAU MVT dataset's <c>composite_url</c>), describing the XYZ tile template,
/// zoom range, optional bounds, and vector layer ids (e.g. roads → "Road", land use → "LandUse").
/// </summary>
public sealed class MvtTileJson
{
    public MvtTileJson(
        IReadOnlyList<string> tileTemplates,
        int? minZoom,
        int? maxZoom,
        double[]? bounds,
        IReadOnlyList<string> vectorLayerIds)
    {
        TileTemplates = tileTemplates ?? Array.Empty<string>();
        MinZoom = minZoom;
        MaxZoom = maxZoom;
        Bounds = bounds;
        VectorLayerIds = vectorLayerIds ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> TileTemplates { get; }

    public int? MinZoom { get; }

    public int? MaxZoom { get; }

    /// <summary>[west, south, east, north] degrees, when present.</summary>
    public double[]? Bounds { get; }

    public IReadOnlyList<string> VectorLayerIds { get; }

    public static MvtTileJson Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("TileJSON content is required.", nameof(json));
        }

        TileJsonDto? dto = JsonConvert.DeserializeObject<TileJsonDto>(json)
            ?? throw new InvalidOperationException("TileJSON response was empty or invalid.");

        IReadOnlyList<string> layerIds = dto.VectorLayers?
            .Where(layer => !string.IsNullOrWhiteSpace(layer?.Id))
            .Select(layer => layer!.Id!)
            .ToArray() ?? Array.Empty<string>();

        return new MvtTileJson(
            dto.Tiles?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? Array.Empty<string>(),
            dto.MinZoom,
            dto.MaxZoom,
            dto.Bounds,
            layerIds);
    }

    /// <summary>Builds a tile URL from the first template by substituting {z}/{x}/{y}.</summary>
    public string BuildTileUrl(int zoom, int x, int y)
    {
        if (TileTemplates.Count == 0)
        {
            throw new InvalidOperationException("TileJSON has no tile templates.");
        }

        return TileTemplates[0]
            .Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture))
            .Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
            .Replace("{y}", y.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class TileJsonDto
    {
        [JsonProperty("tiles")]
        public List<string>? Tiles { get; set; }

        [JsonProperty("minzoom")]
        public int? MinZoom { get; set; }

        [JsonProperty("maxzoom")]
        public int? MaxZoom { get; set; }

        [JsonProperty("bounds")]
        public double[]? Bounds { get; set; }

        [JsonProperty("vector_layers")]
        public List<VectorLayerDto>? VectorLayers { get; set; }
    }

    private sealed class VectorLayerDto
    {
        [JsonProperty("id")]
        public string? Id { get; set; }
    }
}
