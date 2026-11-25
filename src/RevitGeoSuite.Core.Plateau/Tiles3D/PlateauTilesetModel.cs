using System;
using System.Collections.Generic;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

/// <summary>
/// Aggregated, CRS-transformed contents of a PLATEAU 3D Tiles dataset, ready to be
/// converted into Revit DirectShape elements.
/// </summary>
public sealed class PlateauTilesetModel
{
    public PlateauTilesetModel(string sourceUrl, string typeEn, string? lod, bool? texture, string? areaCode, IReadOnlyList<PlateauTilesetFeature> features)
    {
        SourceUrl = sourceUrl;
        TypeEn = typeEn;
        Lod = lod;
        Texture = texture;
        AreaCode = areaCode;
        Features = features;
    }

    public string SourceUrl { get; }
    public string TypeEn { get; }
    public string? Lod { get; }
    public bool? Texture { get; }
    public string? AreaCode { get; }
    public IReadOnlyList<PlateauTilesetFeature> Features { get; }
}

/// <summary>One feature (typically one building) with all its triangles already in project CRS.</summary>
public sealed class PlateauTilesetFeature
{
    public PlateauTilesetFeature(
        string id,
        IReadOnlyDictionary<string, object?> attributes,
        List<PlateauTilesetTriangle> triangles,
        string? gridId = null)
    {
        Id = id;
        Attributes = attributes;
        Triangles = triangles;
        GridId = string.IsNullOrWhiteSpace(gridId) ? null : gridId!.Trim();
    }

    public string Id { get; }
    public IReadOnlyDictionary<string, object?> Attributes { get; }
    public List<PlateauTilesetTriangle> Triangles { get; }

    /// <summary>
    /// Spatial grid or tile identifier (Japan tertiary mesh code for PLATEAU areas, or tileset
    /// leaf id outside Japan mesh coverage). Preserved across downstream filters so online import
    /// replacement is scoped strictly per (area, dataset, LOD, grid) and batched per tile.
    /// </summary>
    public string? GridId { get; }

    public string? GetStringAttribute(string key) =>
        Attributes.TryGetValue(key, out object? value) ? value?.ToString() : null;
}
