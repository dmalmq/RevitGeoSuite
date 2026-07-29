using System;
using System.Collections.Generic;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlExportPackage
{
    public CityGmlExportReferenceContext ReferenceContext { get; set; } = new CityGmlExportReferenceContext();

    public string TargetSchemaVersion { get; set; } = CityGmlExportProfile.LightweightCityGml20;

    public IReadOnlyList<CityGmlFeature> Features { get; set; } = Array.Empty<CityGmlFeature>();

    public IReadOnlyDictionary<CityGmlSemanticType, int> SemanticCounts { get; set; } = new Dictionary<CityGmlSemanticType, int>();

    public CityGmlValidationReport ValidationReport { get; set; } = new CityGmlValidationReport();

    public string OutputFileName { get; set; } = "city-model.gml";

    public string XmlPreview { get; set; } = string.Empty;
}

