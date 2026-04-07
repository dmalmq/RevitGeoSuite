using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class ContextImportPlan
{
    public string SourceFolderPath { get; set; } = string.Empty;

    public PlateauImportReferenceContext ReferenceContext { get; set; } = new PlateauImportReferenceContext();

    public IReadOnlyCollection<PlateauCityModel> SourceModels { get; set; } = new PlateauCityModel[0];

    public IReadOnlyCollection<PlateauFeatureType> SelectedFeatureTypes { get; set; } = new PlateauFeatureType[0];

    public IReadOnlyCollection<string> SelectedTileIds { get; set; } = new string[0];

    public IReadOnlyCollection<ContextSolidPlan> Solids { get; set; } = new ContextSolidPlan[0];

    public IReadOnlyCollection<string> WarningMessages { get; set; } = new string[0];

    public int SourceFeatureCount { get; set; }
}
