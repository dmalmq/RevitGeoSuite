using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitGeoSuite.CesiumHandoff;

/// <summary>
/// Writes a <c>cesium-package.json</c> into an arbitrary existing export folder (a 3D Tiles
/// bundle or a floor-plan GIS output) so the individual export dialogs' "Send to Cesium viewer"
/// action can push a tiles-only or GIS-only package without the tiles/-gis/ layout the combined
/// export produces. Artifacts are described wherever they actually are, path-relative to the folder.
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
        CesiumPackageTiles? tiles = FindTiles(root);
        CesiumPackageGis? gis = FindGis(root, inputs);
        if (tiles is null && gis is null)
        {
            throw new InvalidOperationException(
                $"'{root}' contains neither a tileset.json nor GIS artifacts (.gpkg/.shp); nothing to send.");
        }

        var manifest = new CesiumPackageManifest
        {
            PackageId = Guid.NewGuid().ToString(),
            CreatedUtc = DateTime.UtcNow,
            Generator = new CesiumPackageGenerator { Version = inputs.GeneratorVersion },
            Source = new CesiumPackageSource { Model = inputs.SourceModel, DocumentKey = inputs.DocumentKey },
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
            ContentHash = CesiumPackageContentHasher.Compute(
                root,
                CesiumPackagePayloadResolver.Resolve(root, new CesiumPackageManifest { Tiles = tiles, Gis = gis })),
        };

        File.WriteAllText(
            Path.Combine(root, "cesium-package.json"),
            CesiumPackageManifestSerializer.Serialize(manifest));
        return manifest;
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
        return new CesiumPackageTiles
        {
            Tileset = ToRelative(root, tilesetPath),
            Levels = File.Exists(levelsPath) ? ToRelative(root, levelsPath) : null,
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
