using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class ContextImportPlan
{
    public PlateauImportReferenceContext ReferenceContext { get; set; } = new PlateauImportReferenceContext();

    public PlateauCityModel CityModel { get; set; } = new PlateauCityModel();

    public IReadOnlyCollection<ContextSolidPlan> Solids { get; set; } = new ContextSolidPlan[0];
}
