using System.Collections.Generic;
using RevitGeoSuite.Core.Plateau.Tiles;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportPreview
{
    public string StatusMessage { get; set; } = string.Empty;

    public IReadOnlyCollection<DetailRow> CurrentStateRows { get; set; } = new DetailRow[0];

    public IReadOnlyCollection<PlateauTileCandidate> SuggestedTiles { get; set; } = new PlateauTileCandidate[0];

    public IReadOnlyCollection<DetailRow> FileRows { get; set; } = new DetailRow[0];

    public IReadOnlyCollection<string> FeatureNames { get; set; } = new string[0];

    public bool CanImport { get; set; }
}
