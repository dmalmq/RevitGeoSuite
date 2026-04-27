using System;
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

        object document = new
        {
            version = 1,
            generator = "RevitGeoSuite.Tiles3DExport",
            tileset = "tileset.json",
            content = package.ContentFileName,
            levels = levelGrouper.Group(package.Meshes)
                .Select(group => new
                {
                    levelKey = group.LevelKey,
                    levelName = group.LevelName,
                    levelElevationMeters = group.LevelElevationMeters,
                    elementCount = group.ElementCount,
                    minZMeters = group.MinZMeters,
                    maxZMeters = group.MaxZMeters
                })
                .ToArray()
        };

        return JsonConvert.SerializeObject(document, Formatting.Indented);
    }
}
