using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DLevelManifestWriter
{
    private readonly Tiles3DLevelGrouper levelGrouper;

    public Tiles3DLevelManifestWriter(Tiles3DLevelGrouper? levelGrouper = null)
    {
        this.levelGrouper = levelGrouper ?? new Tiles3DLevelGrouper();
    }

    public string BuildJson(Tiles3DExportPackage package)
    {
        if (package is null)
        {
            throw new ArgumentNullException(nameof(package));
        }

        // Per-link level grouping: keyed by sourceLinkName (empty string = host model).
        // Lets the viewer immediately populate per-building floor lists without waiting
        // for tile feature inspection, and avoids ambiguity when two models share level names.
        Dictionary<string, object> linkLevels = package.Meshes
            .GroupBy(m => m.Metadata.SourceLinkName, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (object)levelGrouper.Group(g.ToList())
                    .Select(BuildLevelEntry)
                    .ToArray());

        object document = new
        {
            version = 2,
            generator = "RevitGeoSuite.Tiles3DExport",
            tileset = "tileset.json",
            content = package.ContentFileName,
            linkLevels,
            levels = levelGrouper.Group(package.Meshes)
                .Select(BuildLevelEntry)
                .ToArray()
        };

        return JsonConvert.SerializeObject(document, Formatting.Indented);
    }

    /// <summary>
    /// <c>levels.json</c> for a package without tiles (Send to Kanizu): the host model's levels only,
    /// with no <c>tileset</c> or <c>content</c>. Same version and level fields as a tiles package.
    /// </summary>
    public string BuildLevelsOnlyJson(IReadOnlyList<Tiles3DLevelGroup> hostLevels)
    {
        if (hostLevels is null)
        {
            throw new ArgumentNullException(nameof(hostLevels));
        }

        object[] levels = hostLevels.Select(BuildLevelEntry).ToArray();
        object document = new
        {
            version = 2,
            generator = "RevitGeoSuite.Tiles3DExport",
            linkLevels = new Dictionary<string, object> { [string.Empty] = levels },
            levels,
        };

        return JsonConvert.SerializeObject(document, Formatting.Indented);
    }

    private static object BuildLevelEntry(Tiles3DLevelGroup group) => new
    {
        levelKey = group.LevelKey,
        levelName = group.LevelName,
        levelUniqueIds = group.LevelUniqueIds,
        levelElevationMeters = group.LevelElevationMeters,
        elementCount = group.ElementCount,
        minZMeters = group.MinZMeters,
        maxZMeters = group.MaxZMeters
    };
}
