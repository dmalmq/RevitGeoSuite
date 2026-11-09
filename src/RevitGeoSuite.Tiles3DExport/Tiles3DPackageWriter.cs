using System;
using System.IO;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DPackageWriter
{
    private readonly Tiles3DGlbWriter glbWriter;
    private readonly TilesetJsonWriter tilesetJsonWriter;
    private readonly Tiles3DLevelManifestWriter levelManifestWriter;
    private readonly Tiles3DElementIndexWriter elementIndexWriter;

    public Tiles3DPackageWriter(
        Tiles3DGlbWriter? glbWriter = null,
        TilesetJsonWriter? tilesetJsonWriter = null,
        Tiles3DLevelManifestWriter? levelManifestWriter = null,
        Tiles3DElementIndexWriter? elementIndexWriter = null)
    {
        this.glbWriter = glbWriter ?? new Tiles3DGlbWriter();
        this.tilesetJsonWriter = tilesetJsonWriter ?? new TilesetJsonWriter();
        this.levelManifestWriter = levelManifestWriter ?? new Tiles3DLevelManifestWriter();
        this.elementIndexWriter = elementIndexWriter ?? new Tiles3DElementIndexWriter();
    }

    public (string TilesetPath, string ContentPath) Write(string outputDirectory, Tiles3DExportPackage package)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory cannot be empty.", nameof(outputDirectory));
        }

        if (package is null)
        {
            throw new ArgumentNullException(nameof(package));
        }

        Directory.CreateDirectory(outputDirectory);
        string contentPath = Path.Combine(outputDirectory, package.ContentFileName);
        string tilesetPath = Path.Combine(outputDirectory, "tileset.json");

        glbWriter.Write(contentPath, package);
        File.WriteAllText(tilesetPath, tilesetJsonWriter.BuildJson(package));
        File.WriteAllText(Path.Combine(outputDirectory, package.LevelManifestFileName), levelManifestWriter.BuildJson(package));
        elementIndexWriter.Write(outputDirectory, package);
        return (tilesetPath, contentPath);
    }
}
