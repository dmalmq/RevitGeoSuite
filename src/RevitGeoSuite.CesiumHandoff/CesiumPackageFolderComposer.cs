using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace RevitGeoSuite.CesiumHandoff;

/// <summary>
/// Writes a <c>cesium-package.json</c> into an arbitrary existing export folder (a 3D Tiles
/// bundle or a floor-plan GIS output) so the individual export dialogs' "Send to Cesium viewer"
/// action can push a tiles-only or GIS-only package without the tiles/-gis/ layout the combined
/// export produces. Artifacts are described wherever they actually are, path-relative to the folder.
/// A station version folder keeps its own manifest and is never written to. A folder that already
/// holds a manifest of the same model keeps what only its export knew: the model id and what the
/// 3D Tiles export covered.
/// </summary>
public static class CesiumPackageFolderComposer
{
    public static CesiumPackageManifest ComposeFromFolder(string folder, CesiumPackageBuildInputs inputs)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new ArgumentException("An export folder is required.", nameof(folder));
        }

        if (inputs is null)
        {
            throw new ArgumentNullException(nameof(inputs));
        }

        string root = folder.Trim();
        if (CesiumStationFolder.IsVersionFolder(root))
        {
            // Kanizu reads this manifest, and a version never changes once published.
            string manifestPath = Path.Combine(root, "cesium-package.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException(
                    $"'{root}' is a station version folder without a manifest, so it cannot be sent.");
            }

            return CesiumPackageManifestSerializer.Deserialize(File.ReadAllText(manifestPath));
        }

        string? enclosingVersion = CesiumStationFolder.FindEnclosingVersionFolder(root);
        if (enclosingVersion != null)
        {
            throw new InvalidOperationException(
                $"'{root}' is inside the station version '{enclosingVersion}'. Send the version folder itself.");
        }

        CesiumPackageTiles? tiles = FindTiles(root);
        CesiumPackageGis? gis = FindGis(root, inputs);
        if (tiles is null && gis is null)
        {
            throw new InvalidOperationException(
                $"'{root}' contains neither a tileset.json nor GIS artifacts (.gpkg/.shp); nothing to send.");
        }

        CesiumPackageManifest? previous = ReadPreviousManifestOfSameModel(root, inputs);
        string? modelId = !string.IsNullOrWhiteSpace(previous?.Source?.ModelId)
            ? previous!.Source!.ModelId
            : inputs.ModelId;
        string? modelRole = CesiumModelRoles.Normalize(inputs.ModelRole)
            ?? CesiumModelRoles.Normalize(previous?.Source?.ModelRole);
        var manifest = new CesiumPackageManifest
        {
            PackageId = Guid.NewGuid().ToString(),
            CreatedUtc = DateTime.UtcNow,
            Generator = new CesiumPackageGenerator { Version = inputs.GeneratorVersion },
            Source = new CesiumPackageSource
            {
                Model = inputs.SourceModel,
                DocumentKey = inputs.DocumentKey,
                ModelId = string.IsNullOrWhiteSpace(modelId) ? null : modelId,
                ModelRole = modelRole,
            },
            Building = new CesiumPackageBuilding
            {
                Id = inputs.BuildingId,
                Name = inputs.BuildingName,
                Aliases = inputs.BuildingAliases is { Count: > 0 } ? inputs.BuildingAliases : null,
            },
            Crs = CesiumPackageLayoutBuilder.CreateCrs(inputs),
            Anchor = CesiumPackageLayoutBuilder.CreateAnchor(inputs),
            Tiles = tiles,
            Gis = gis,
            LevelMap = inputs.LevelMap is { Count: > 0 } ? inputs.LevelMap : null,
            Export = tiles is null ? null : inputs.Export ?? previous?.Export,
            ContentHash = CesiumPackageContentHasher.Compute(
                root,
                CesiumPackagePayloadResolver.Resolve(root, new CesiumPackageManifest { Tiles = tiles, Gis = gis })),
        };

        File.WriteAllText(
            Path.Combine(root, "cesium-package.json"),
            CesiumPackageManifestSerializer.Serialize(manifest));
        return manifest;
    }

    /// <summary>
    /// The manifest the folder already holds, when it reads as one and names the model of
    /// <paramref name="inputs"/>; null otherwise, so a folder reused for another model takes
    /// nothing from the model exported there before.
    /// </summary>
    private static CesiumPackageManifest? ReadPreviousManifestOfSameModel(string root, CesiumPackageBuildInputs inputs)
    {
        string manifestPath = Path.Combine(root, "cesium-package.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        CesiumPackageManifest previous;
        try
        {
            previous = CesiumPackageManifestSerializer.Deserialize(File.ReadAllText(manifestPath));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        string previousDocument = previous.Source?.DocumentKey ?? string.Empty;
        return previousDocument.Length > 0 &&
               string.Equals(previousDocument, inputs.DocumentKey, StringComparison.OrdinalIgnoreCase)
            ? previous
            : null;
    }

    private static CesiumPackageTiles? FindTiles(string root)
    {
        string? tilesetPath = Directory
            .EnumerateFiles(root, "tileset.json", SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .FirstOrDefault();
        if (tilesetPath is null)
        {
            return null;
        }

        string tilesetDir = Path.GetDirectoryName(tilesetPath)!;
        string levelsPath = Path.Combine(tilesetDir, "levels.json");
        string elementsPath = Path.Combine(tilesetDir, "elements.json");
        string roomsPath = Path.Combine(tilesetDir, "rooms.json");
        return new CesiumPackageTiles
        {
            Tileset = ToRelative(root, tilesetPath),
            Levels = File.Exists(levelsPath) ? ToRelative(root, levelsPath) : null,
            Elements = File.Exists(elementsPath) ? ToRelative(root, elementsPath) : null,
            Rooms = File.Exists(roomsPath) ? ToRelative(root, roomsPath) : null,
        };
    }

    private static CesiumPackageGis? FindGis(string root, CesiumPackageBuildInputs inputs)
    {
        List<string> gpkg = FindRelative(root, "*.gpkg");
        List<string> shp = FindRelative(root, "*.shp");
        List<string> artifacts = gpkg.Count > 0 ? gpkg : shp;
        if (artifacts.Count == 0)
        {
            return null;
        }

        return new CesiumPackageGis
        {
            Format = gpkg.Count > 0 ? "geopackage" : "shapefile",
            Artifacts = artifacts
                .Select(path => new CesiumPackageGisArtifact
                {
                    Path = path,
                    Layers = inputs.GisLayers is { Count: > 0 } ? inputs.GisLayers : null,
                })
                .ToList(),
            PackageManifest = FindRelative(root, "package-manifest.json").FirstOrDefault(),
            Rules = inputs.GisRules,
        };
    }

    private static List<string> FindRelative(string root, string pattern)
    {
        return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Select(path => ToRelative(root, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ToRelative(string root, string fullPath)
    {
        string trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string relative = fullPath.StartsWith(trimmedRoot, StringComparison.OrdinalIgnoreCase)
            ? fullPath.Substring(trimmedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : fullPath;
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}
