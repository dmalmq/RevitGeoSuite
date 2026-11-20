using System;
using System.Collections.Generic;
using System.IO;

namespace RevitGeoSuite.Tiles3DExport;

/// <summary>
/// The index files of a package without tiles, for Send to Kanizu: the levels the GIS export uses
/// and the host's rooms, in the frame a tiles export would use, so Kanizu pairs and places them as
/// it does a full export's. No geometry is extracted, so it takes seconds.
/// </summary>
public sealed class Tiles3DIndexPackage
{
    public const string LevelsFileName = "levels.json";

    public const string RoomsFileName = "rooms.json";

    /// <summary>The anchor, its elevation lifted by the geoid to ellipsoidal as in a tiles package; never rebased.</summary>
    public Tiles3DExportReferenceContext ReferenceContext { get; set; } = new Tiles3DExportReferenceContext();

    public double GeoidHeightOffsetMeters { get; set; }

    public string GeoidModel { get; set; } = string.Empty;

    public IReadOnlyList<Tiles3DLevelGroup> Levels { get; set; } = Array.Empty<Tiles3DLevelGroup>();

    public IReadOnlyList<Tiles3DRoomFootprint> Rooms { get; set; } = Array.Empty<Tiles3DRoomFootprint>();

    /// <summary>Writes <c>levels.json</c> and <c>rooms.json</c> into <paramref name="tilesDirectory"/>.</summary>
    public void Write(string tilesDirectory)
    {
        Directory.CreateDirectory(tilesDirectory);
        File.WriteAllText(Path.Combine(tilesDirectory, LevelsFileName), new Tiles3DLevelManifestWriter().BuildLevelsOnlyJson(Levels));
        File.WriteAllText(Path.Combine(tilesDirectory, RoomsFileName), new Tiles3DRoomIndexWriter().BuildJson(Rooms));
    }
}
