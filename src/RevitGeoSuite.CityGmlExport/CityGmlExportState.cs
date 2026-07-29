using System;
using System.Collections.Generic;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlExportState
{
    public Dictionary<string, string> CategoryMappingOverrides { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> CodelistOverrides { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string TargetSchemaVersion { get; set; } = CityGmlExportProfile.LightweightCityGml20;

    public string LastExportPath { get; set; } = string.Empty;

    public DateTime? LastExportDateUtc { get; set; }

    public CityGmlExportReferenceSource LastReferenceSource { get; set; } = CityGmlExportReferenceSource.WorkingProjectBasePoint;

    public string LastViewUniqueId { get; set; } = string.Empty;

    public string LastViewName { get; set; } = string.Empty;

    public List<string> LastSelectedLinkUniqueIds { get; set; } = new List<string>();

    public List<string> LastSelectedLinkNames { get; set; } = new List<string>();

    public int LastExportedFeatureCount { get; set; }
}
