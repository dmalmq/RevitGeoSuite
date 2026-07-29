using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitGeoSuite.CesiumHandoff;

/// <summary>
/// Paths of one package folder: <c>&lt;root&gt;/cesium-package.json</c>, <c>tiles/</c>, <c>gis/</c>.
/// </summary>
public sealed class CesiumPackageLayout
{
    public CesiumPackageLayout(string rootDirectory)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? throw new ArgumentException("A package root directory is required.", nameof(rootDirectory))
            : rootDirectory.Trim();
    }

    public string RootDirectory { get; }

    public string TilesDirectory => Path.Combine(RootDirectory, "tiles");

    public string GisDirectory => Path.Combine(RootDirectory, "gis");

    public string ManifestPath => Path.Combine(RootDirectory, "cesium-package.json");
}

/// <summary>
/// Everything the manifest needs that the file system cannot tell us — building identity,
/// CRS, anchor, and the GIS↔tiles level map computed at export time.
/// </summary>
public sealed class CesiumPackageBuildInputs
{
    public string BuildingId { get; set; } = string.Empty;

    public string BuildingName { get; set; } = string.Empty;

    public List<string>? BuildingAliases { get; set; }

    public string SourceModel { get; set; } = string.Empty;

    public string DocumentKey { get; set; } = string.Empty;

    public string GeneratorVersion { get; set; } = string.Empty;

    public int? ProjectEpsg { get; set; }

    public string CoordinateMode { get; set; } = string.Empty;

    public int? GisEpsg { get; set; }

    public double? AnchorLat { get; set; }

    public double? AnchorLon { get; set; }

    public double? AnchorElevationMeters { get; set; }

    public double? GeoidOffsetMeters { get; set; }

    public List<CesiumPackageLevelMapEntry>? LevelMap { get; set; }

    public List<string>? GisLayers { get; set; }
}

/// <summary>
/// Creates the package folder layout and composes <c>cesium-package.json</c> from the
/// artifacts actually present on disk, so partially produced packages (tiles-only,
/// GIS-only) describe themselves correctly.
/// </summary>
public sealed class CesiumPackageLayoutBuilder
{
    public CesiumPackageLayout CreateLayout(string rootDirectory)
    {
        var layout = new CesiumPackageLayout(rootDirectory);
        Directory.CreateDirectory(layout.RootDirectory);
        foreach (string directory in new[] { layout.TilesDirectory, layout.GisDirectory })
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            Directory.CreateDirectory(directory);
        }

        if (File.Exists(layout.ManifestPath))
        {
            File.Delete(layout.ManifestPath);
        }
        return layout;
    }

    public CesiumPackageLayout CreateStagingLayout(string destinationRootDirectory)
    {
        string destination = Path.GetFullPath(new CesiumPackageLayout(destinationRootDirectory).RootDirectory);
        string parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException($"Cannot determine the parent of package directory '{destination}'.");
        Directory.CreateDirectory(parent);
        string name = Path.GetFileName(destination.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string stagingRoot = Path.Combine(parent, $".{name}.staging-{Guid.NewGuid():N}");
        return CreateLayout(stagingRoot);
    }

    public CesiumPackageLayout PublishLayout(CesiumPackageLayout stagingLayout, string destinationRootDirectory)
    {
        if (stagingLayout is null)
        {
            throw new ArgumentNullException(nameof(stagingLayout));
        }

        if (!File.Exists(stagingLayout.ManifestPath))
        {
            throw new InvalidOperationException("The staged package has no manifest.");
        }

        var destination = new CesiumPackageLayout(Path.GetFullPath(destinationRootDirectory));
        string parent = Path.GetDirectoryName(destination.RootDirectory)
            ?? throw new InvalidOperationException($"Cannot determine the parent of package directory '{destination.RootDirectory}'.");
        Directory.CreateDirectory(destination.RootDirectory);
        string backupRoot = Path.Combine(parent, $".{Path.GetFileName(destination.RootDirectory)}.backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupRoot);

        var replacements = new[]
        {
            (Staged: stagingLayout.TilesDirectory, Destination: destination.TilesDirectory, Backup: Path.Combine(backupRoot, "tiles"), IsDirectory: true),
            (Staged: stagingLayout.GisDirectory, Destination: destination.GisDirectory, Backup: Path.Combine(backupRoot, "gis"), IsDirectory: true),
            (Staged: stagingLayout.ManifestPath, Destination: destination.ManifestPath, Backup: Path.Combine(backupRoot, "cesium-package.json"), IsDirectory: false),
        };

        var installedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool backupCanBeDeleted = false;
        try
        {
            foreach (var replacement in replacements)
            {
                if (replacement.IsDirectory && Directory.Exists(replacement.Destination))
                {
                    Directory.Move(replacement.Destination, replacement.Backup);
                }
                else if (!replacement.IsDirectory && File.Exists(replacement.Destination))
                {
                    File.Move(replacement.Destination, replacement.Backup);
                }

                if (replacement.IsDirectory)
                {
                    Directory.Move(replacement.Staged, replacement.Destination);
                }
                else
                {
                    File.Move(replacement.Staged, replacement.Destination);
                }
                installedDestinations.Add(replacement.Destination);
            }
            backupCanBeDeleted = true;
        }
        catch (Exception publicationException)
        {
            try
            {
                foreach (var replacement in replacements.Reverse())
                {
                    if (replacement.IsDirectory)
                    {
                        if (installedDestinations.Contains(replacement.Destination) && Directory.Exists(replacement.Destination))
                        {
                            Directory.Delete(replacement.Destination, recursive: true);
                        }
                        if (Directory.Exists(replacement.Backup))
                        {
                            Directory.Move(replacement.Backup, replacement.Destination);
                        }
                    }
                    else
                    {
                        if (installedDestinations.Contains(replacement.Destination) && File.Exists(replacement.Destination))
                        {
                            File.Delete(replacement.Destination);
                        }
                        if (File.Exists(replacement.Backup))
                        {
                            File.Move(replacement.Backup, replacement.Destination);
                        }
                    }
                }
                backupCanBeDeleted = true;
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException("Package publication failed and the previous package could not be fully restored.", publicationException, rollbackException);
            }
            throw;
        }
        finally
        {
            if (backupCanBeDeleted && Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, recursive: true);
            }
            if (Directory.Exists(stagingLayout.RootDirectory))
            {
                Directory.Delete(stagingLayout.RootDirectory, recursive: true);
            }
        }

        return destination;
    }

    public void DeleteStagingLayout(CesiumPackageLayout stagingLayout)
    {
        if (stagingLayout is not null && Directory.Exists(stagingLayout.RootDirectory))
        {
            Directory.Delete(stagingLayout.RootDirectory, recursive: true);
        }
    }

    public CesiumPackageManifest WriteManifest(CesiumPackageLayout layout, CesiumPackageBuildInputs inputs)
    {
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        if (inputs is null)
        {
            throw new ArgumentNullException(nameof(inputs));
        }

        CesiumPackageTiles? tiles = ScanTiles(layout);
        CesiumPackageGis? gis = ScanGis(layout, inputs);
        if (tiles is null && gis is null)
        {
            throw new InvalidOperationException(
                $"The package at '{layout.RootDirectory}' contains neither a tileset nor GIS artifacts; nothing to describe.");
        }

        var payloadManifest = new CesiumPackageManifest { Tiles = tiles, Gis = gis };
        List<string> payloadPaths = CesiumPackagePayloadResolver.Resolve(layout.RootDirectory, payloadManifest);

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
            Crs = CreateCrs(inputs),
            Anchor = CreateAnchor(inputs),
            Tiles = tiles,
            Gis = gis,
            LevelMap = inputs.LevelMap is { Count: > 0 } ? inputs.LevelMap : null,
            ContentHash = CesiumPackageContentHasher.Compute(layout.RootDirectory, payloadPaths),
        };

        File.WriteAllText(layout.ManifestPath, CesiumPackageManifestSerializer.Serialize(manifest));
        return manifest;
    }

    internal static CesiumPackageCrs? CreateCrs(CesiumPackageBuildInputs inputs)
    {
        return inputs.ProjectEpsg.HasValue && inputs.GisEpsg.HasValue && !string.IsNullOrWhiteSpace(inputs.CoordinateMode)
            ? new CesiumPackageCrs
            {
                ProjectEpsg = inputs.ProjectEpsg.Value,
                CoordinateMode = inputs.CoordinateMode,
                GisEpsg = inputs.GisEpsg.Value,
            }
            : null;
    }

    internal static CesiumPackageAnchor? CreateAnchor(CesiumPackageBuildInputs inputs)
    {
        return inputs.AnchorLat.HasValue && inputs.AnchorLon.HasValue && inputs.AnchorElevationMeters.HasValue
            ? new CesiumPackageAnchor
            {
                Lat = inputs.AnchorLat.Value,
                Lon = inputs.AnchorLon.Value,
                ElevationMeters = inputs.AnchorElevationMeters.Value,
                GeoidOffsetMeters = inputs.GeoidOffsetMeters,
            }
            : null;
    }

    private static CesiumPackageTiles? ScanTiles(CesiumPackageLayout layout)
    {
        string tilesetPath = Path.Combine(layout.TilesDirectory, "tileset.json");
        if (!File.Exists(tilesetPath))
        {
            return null;
        }

        bool hasLevels = File.Exists(Path.Combine(layout.TilesDirectory, "levels.json"));
        return new CesiumPackageTiles
        {
            Tileset = "tiles/tileset.json",
            Levels = hasLevels ? "tiles/levels.json" : null,
        };
    }

    private static CesiumPackageGis? ScanGis(CesiumPackageLayout layout, CesiumPackageBuildInputs inputs)
    {
        if (!Directory.Exists(layout.GisDirectory))
        {
            return null;
        }

        List<string> gpkgFiles = ListRelative(layout, "*.gpkg");
        List<string> shpFiles = ListRelative(layout, "*.shp");
        List<string> artifactPaths = gpkgFiles.Count > 0 ? gpkgFiles : shpFiles;
        if (artifactPaths.Count == 0)
        {
            return null;
        }

        string? packageManifest = ListRelative(layout, "package-manifest.json").FirstOrDefault();
        return new CesiumPackageGis
        {
            Format = gpkgFiles.Count > 0 ? "geopackage" : "shapefile",
            Artifacts = artifactPaths
                .Select(path => new CesiumPackageGisArtifact
                {
                    Path = path,
                    Layers = inputs.GisLayers is { Count: > 0 } ? inputs.GisLayers : null,
                })
                .ToList(),
            PackageManifest = packageManifest,
        };
    }

    private static List<string> ListRelative(CesiumPackageLayout layout, string pattern)
    {
        return Directory.EnumerateFiles(layout.GisDirectory, pattern, SearchOption.AllDirectories)
            .Select(path => ToRelativePackagePath(layout.RootDirectory, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ToRelativePackagePath(string root, string fullPath)
    {
        string trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string relative = fullPath.StartsWith(trimmedRoot, StringComparison.OrdinalIgnoreCase)
            ? fullPath.Substring(trimmedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : fullPath;
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}
