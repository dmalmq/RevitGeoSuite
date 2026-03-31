namespace RevitGeoSuite.Core.Plateau.Tiles;

public sealed class PlateauTileCandidate
{
    public string TileId { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public string Source { get; set; } = string.Empty;
}
