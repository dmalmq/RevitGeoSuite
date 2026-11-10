using System.Collections.Generic;

namespace RevitGeoSuite.Tiles3DExport;

/// <summary>
/// One placed Revit room as <c>rooms.json</c> carries it: its identity, its level, and its
/// footprint in plane-grid easting and northing of the project CRS.
/// </summary>
public sealed class Tiles3DRoomFootprint
{
    public string UniqueId { get; set; } = string.Empty;

    public string ElementId { get; set; } = string.Empty;

    public string SourceDocument { get; set; } = string.Empty;

    public string SourceLinkName { get; set; } = string.Empty;

    /// <summary>The link instance's UniqueId; empty for host rooms.</summary>
    public string SourceLinkInstanceUniqueId { get; set; } = string.Empty;

    public string LevelUniqueId { get; set; } = string.Empty;

    public string LevelName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Number { get; set; } = string.Empty;

    /// <summary>The room's level in the anchor's height datum, in metres.</summary>
    public double ElevationMeters { get; set; }

    /// <summary>Revit's own room area, in square metres.</summary>
    public double AreaSquareMeters { get; set; }

    /// <summary>
    /// The boundary loops as Revit returns them at the finish face: the outer loop first, then
    /// the islands. Each point is <c>{ easting, northing }</c> in metres.
    /// </summary>
    public List<List<double[]>> Rings { get; set; } = new List<List<double[]>>();
}
