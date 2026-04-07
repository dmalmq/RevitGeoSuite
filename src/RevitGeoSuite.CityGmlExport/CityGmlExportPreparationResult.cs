using System;
using System.Collections.Generic;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlExportPreparationResult
{
    public CityGmlExportPackage Package { get; set; } = new CityGmlExportPackage();

    public IReadOnlyCollection<DetailRow> PreparedRows { get; set; } = Array.Empty<DetailRow>();

    public IReadOnlyCollection<string> FeatureNames { get; set; } = Array.Empty<string>();

    public IReadOnlyCollection<string> ValidationMessages { get; set; } = Array.Empty<string>();

    public string StatusMessage { get; set; } = string.Empty;
}

