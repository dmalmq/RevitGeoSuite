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

    /// <summary>
    /// The model's stable id from <c>GeoProjectInfo.ModelId</c>. Unlike <see cref="DocumentKey"/>,
    /// which hashes the file path, it is the same for every user and survives Save As and moves.
    /// Absent when the model could not be written to (read-only) on its first export.
    /// </summary>
    public string? ModelId { get; set; }
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

/// <summary>
/// Where the tileset's east-north-up frame sits. <see cref="ElevationMeters"/> is ellipsoidal
/// (orthometric + <see cref="GeoidOffsetMeters"/>); the other fields give the source numbers it
/// came from, so a consumer can redo the placement with its own geoid and projection.
/// </summary>
public sealed class CesiumPackageAnchor
{
    public double Lat { get; set; }

    public double Lon { get; set; }

    /// <summary>Height above the WGS84 ellipsoid, as used in the tileset transform.</summary>
    public double ElevationMeters { get; set; }

    /// <summary>The geoid undulation added to the orthometric height. Always written, 0 included.</summary>
    public double? GeoidOffsetMeters { get; set; }

    /// <summary>"EGM2008", or "typed" when the user entered the offset.</summary>
    public string? GeoidModel { get; set; }

    /// <summary>The anchor height before the geoid offset, in the model's own height datum.</summary>
    public double? OrthometricHeightMeters { get; set; }

    /// <summary>What <see cref="OrthometricHeightMeters"/> is measured from: "TP" or "unknown".</summary>
    public string? HeightDatum { get; set; }

    /// <summary>The anchor's easting in <see cref="CesiumPackageCrs.ProjectEpsg"/>, metres.</summary>
    public double? Easting { get; set; }

    /// <summary>The anchor's northing in <see cref="CesiumPackageCrs.ProjectEpsg"/>, metres.</summary>
    public double? Northing { get; set; }
}

/// <summary>The height datum names written to <see cref="CesiumPackageAnchor.HeightDatum"/>.</summary>
public static class CesiumHeightDatums
{
    /// <summary>Tokyo Peil (東京湾平均海面), Japan's orthometric datum.</summary>
    public const string Tp = "TP";

    public const string Unknown = "unknown";

    /// <summary>Maps a request value ("tp", "TP", anything else) to a manifest value.</summary>
    public static string Normalize(string? value)
    {
        return string.Equals(value?.Trim(), Tp, StringComparison.OrdinalIgnoreCase) ? Tp : Unknown;
    }
}

public sealed class CesiumPackageTiles
{
    public string Tileset { get; set; } = string.Empty;

    public string? Levels { get; set; }

    /// <summary>
    /// Per-element geometry hashes (<c>tiles/elements.json</c>), for change lists. Not part of the
    /// viewer push payload or <c>contentHash</c>; it is derived from the same geometry as the GLB.
    /// </summary>
    public string? Elements { get; set; }

    /// <summary>
    /// Room footprints (<c>tiles/rooms.json</c>), which Kanizu pairs with the GDB's units. Not part
    /// of the viewer push payload or <c>contentHash</c>.
    /// </summary>
    public string? Rooms { get; set; }
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
