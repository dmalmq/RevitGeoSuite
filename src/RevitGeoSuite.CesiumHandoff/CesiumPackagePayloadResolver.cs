using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RevitGeoSuite.CesiumHandoff;

internal static class CesiumPackagePayloadResolver
{
    private static readonly string[] ShapefileExtensions =
    {
        ".shp", ".shx", ".dbf", ".prj", ".cpg", ".qix", ".sbn", ".sbx", ".shp.xml",
    };

    public static List<string> Resolve(string root, CesiumPackageManifest manifest)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (manifest.Tiles is not null)
        {
            AddPath(fullRoot, paths, manifest.Tiles.Tileset);
            AddPath(fullRoot, paths, manifest.Tiles.Levels);
            AddTilesetReferences(fullRoot, paths, manifest.Tiles.Tileset, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        if (manifest.Gis is not null)
        {
            foreach (CesiumPackageGisArtifact artifact in manifest.Gis.Artifacts ?? new List<CesiumPackageGisArtifact>())
            {
                AddPath(fullRoot, paths, artifact.Path);
                if (string.Equals(Path.GetExtension(artifact.Path), ".shp", StringComparison.OrdinalIgnoreCase))
                {
                    AddShapefileComponents(fullRoot, paths, artifact.Path);
                }
            }

            AddPath(fullRoot, paths, manifest.Gis.PackageManifest);
            AddPackageManifestReferences(fullRoot, paths, manifest.Gis.PackageManifest);
        }

        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddTilesetReferences(
        string root,
        HashSet<string> paths,
        string tilesetPath,
        HashSet<string> visitedTilesets)
    {
        string normalizedTileset = NormalizeRelativePath(root, tilesetPath);
        if (!visitedTilesets.Add(normalizedTileset))
        {
            return;
        }

        string fullTilesetPath = ToFullPath(root, normalizedTileset);
        if (!File.Exists(fullTilesetPath))
        {
            return;
        }

        JToken document = JToken.Parse(File.ReadAllText(fullTilesetPath));
        string tilesetDirectory = Path.GetDirectoryName(normalizedTileset)?.Replace('\\', '/') ?? string.Empty;
        foreach (JToken content in document.SelectTokens("$..content"))
        {
            string? uri = (string?)content["uri"] ?? (string?)content["url"];
            if (string.IsNullOrWhiteSpace(uri) || Uri.TryCreate(uri, UriKind.Absolute, out _))
            {
                continue;
            }

            string cleanUri = uri.Split(new[] { '?', '#' }, 2)[0].Replace('/', Path.DirectorySeparatorChar);
            string referencedPath = Path.Combine(tilesetDirectory, cleanUri).Replace(Path.DirectorySeparatorChar, '/');
            AddPath(root, paths, referencedPath);
            if (string.Equals(Path.GetExtension(cleanUri), ".json", StringComparison.OrdinalIgnoreCase))
            {
                AddTilesetReferences(root, paths, referencedPath, visitedTilesets);
            }
        }
    }

    private static void AddPackageManifestReferences(string root, HashSet<string> paths, string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return;
        }

        string normalizedManifest = NormalizeRelativePath(root, manifestPath);
        string fullManifestPath = ToFullPath(root, normalizedManifest);
        if (!File.Exists(fullManifestPath))
        {
            return;
        }

        JObject document = JObject.Parse(File.ReadAllText(fullManifestPath));
        string manifestDirectory = Path.GetDirectoryName(normalizedManifest)?.Replace('\\', '/') ?? string.Empty;
        foreach (JToken file in document.GetValue("files", StringComparison.OrdinalIgnoreCase) ?? new JArray())
        {
            string? relativePath = file is JObject fileObject
                ? (string?)fileObject.GetValue("relativePath", StringComparison.OrdinalIgnoreCase)
                : null;
            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                string referencedPath = Path.Combine(manifestDirectory, relativePath).Replace(Path.DirectorySeparatorChar, '/');
                AddPath(root, paths, referencedPath);
                if (string.Equals(Path.GetExtension(relativePath), ".shp", StringComparison.OrdinalIgnoreCase))
                {
                    AddShapefileComponents(root, paths, referencedPath);
                }
            }
        }
    }

    private static void AddShapefileComponents(string root, HashSet<string> paths, string shapefilePath)
    {
        string normalized = NormalizeRelativePath(root, shapefilePath);
        string directory = Path.GetDirectoryName(normalized) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(normalized);
        string fullDirectory = ToFullPath(root, string.IsNullOrEmpty(directory) ? "." : directory);
        foreach (string componentPath in Directory.EnumerateFiles(fullDirectory))
        {
            string fileName = Path.GetFileName(componentPath);
            if (!fileName.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string suffix = fileName.Substring(stem.Length);
            if (ShapefileExtensions.Contains(suffix, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(NormalizeRelativePath(root, Path.Combine(directory, fileName)));
            }
        }
    }

    private static void AddPath(string root, HashSet<string> paths, string? relativePath)
    {
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            paths.Add(NormalizeRelativePath(root, relativePath));
        }
    }

    private static string NormalizeRelativePath(string root, string relativePath)
    {
        string fullPath = ToFullPath(root, relativePath);
        string prefix = root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Package path '{relativePath}' is outside the package root.");
        }

        return fullPath.Substring(prefix.Length).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string ToFullPath(string root, string relativePath)
    {
        string platformPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(root, platformPath));
    }
}
