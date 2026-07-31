using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace RevitGeoSuite.CesiumHandoff;

/// <summary>
/// Root of <c>cesium-package.json</c> — the lightweight handoff manifest a Cesium viewer
/// consumes to ingest a RevitGeoSuite export (3D Tiles and/or GIS artifacts) without
/// fuzzy matching. The <see cref="Tiles"/> and <see cref="Gis"/> sections are independently
/// optional so tiles-only and GIS-only pushes share the same schema.
/// </summary>
public sealed class CesiumPackageManifest
{
    public const string SchemaId = "revitgeosuite.cesium-package";
    public const int CurrentVersion = 1;

    public string Schema { get; set; } = SchemaId;

    public int Version { get; set; } = CurrentVersion;

    public string PackageId { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public CesiumPackageGenerator? Generator { get; set; }

    public CesiumPackageSource? Source { get; set; }

    public CesiumPackageBuilding? Building { get; set; }

    public CesiumPackageCrs? Crs { get; set; }

    public CesiumPackageAnchor? Anchor { get; set; }

    public CesiumPackageTiles? Tiles { get; set; }

    public CesiumPackageGis? Gis { get; set; }

    public List<CesiumPackageLevelMapEntry>? LevelMap { get; set; }

    /// <summary>SHA-256 over the payload files; lets the viewer skip unchanged re-pushes.</summary>
    public string? ContentHash { get; set; }
}

public sealed class CesiumPackageGenerator
{
    public string Name { get; set; } = "RevitGeoSuite";

    public string Version { get; set; } = string.Empty;
}

public sealed class CesiumPackageSource
{
    public string Model { get; set; } = string.Empty;

    public string DocumentKey { get; set; } = string.Empty;
}

public sealed class CesiumPackageBuilding
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string>? Aliases { get; set; }
}

public sealed class CesiumPackageCrs
{
    public int ProjectEpsg { get; set; }

    public string CoordinateMode { get; set; } = string.Empty;

    public int GisEpsg { get; set; }
}

public sealed class CesiumPackageAnchor
{
    public double Lat { get; set; }

    public double Lon { get; set; }

    public double ElevationMeters { get; set; }

    public double? GeoidOffsetMeters { get; set; }
}

public sealed class CesiumPackageTiles
{
    public string Tileset { get; set; } = string.Empty;

    public string? Levels { get; set; }
}

public sealed class CesiumPackageGis
{
    public string Format { get; set; } = "geopackage";

    public List<CesiumPackageGisArtifact>? Artifacts { get; set; }

    public string? PackageManifest { get; set; }
}

public sealed class CesiumPackageGisArtifact
{
    public string Path { get; set; } = string.Empty;

    public List<string>? Layers { get; set; }
}

/// <summary>
/// Maps a GIS <c>level_id</c> attribute value (persisted IMDF level id) onto the
/// 3D Tiles <c>levels.json</c> level key (level-name slug). Only the exporter knows
/// both sides, so the map is computed at export time.
/// </summary>
public sealed class CesiumPackageLevelMapEntry
{
    public string GisLevelId { get; set; } = string.Empty;

    public string TilesLevelKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public static class CesiumPackageManifestSerializer
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
    };

    public static string Serialize(CesiumPackageManifest manifest)
    {
        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        return JsonConvert.SerializeObject(manifest, Settings);
    }

    public static CesiumPackageManifest Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Manifest JSON is required.", nameof(json));
        }

        CesiumPackageManifest? manifest = JsonConvert.DeserializeObject<CesiumPackageManifest>(json, Settings);
        if (manifest is null)
        {
            throw new InvalidOperationException("The manifest JSON deserialized to null.");
        }

        if (!string.Equals(manifest.Schema, CesiumPackageManifest.SchemaId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unexpected manifest schema '{manifest.Schema}'. Expected '{CesiumPackageManifest.SchemaId}'.");
        }

        if (manifest.Version > CesiumPackageManifest.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Manifest version {manifest.Version} is newer than the supported version {CesiumPackageManifest.CurrentVersion}.");
        }

        return manifest;
    }
}
