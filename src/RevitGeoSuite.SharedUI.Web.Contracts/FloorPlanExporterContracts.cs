using System.Collections.Generic;

namespace RevitGeoSuite.SharedUI.Web.Contracts;

[TsExport]
public sealed class ExporterEmptyRequest
{
}

[TsExport]
public sealed class ExporterInitialStateRequest
{
}

[TsExport]
public sealed class ExporterInitialStateResponse
{
    public string DocumentName { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string CoordinateStatus { get; set; } = string.Empty;

    public string CoordinateDetail { get; set; } = string.Empty;

    public ExporterSettingsPayload Settings { get; set; } = new();

    public List<ExporterViewOption> Views { get; set; } = new();

    public List<ExporterLinkOption> Links { get; set; } = new();

    public List<ExporterProfileOption> Profiles { get; set; } = new();

    public List<ExporterNamedOption> SchemaProfiles { get; set; } = new();

    public List<ExporterNamedOption> ValidationPolicies { get; set; } = new();

    public List<ExporterCrsPresetGroup> CrsPresetGroups { get; set; } = new();
}

[TsExport]
public sealed class ExporterViewOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string LevelName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}

[TsExport]
public sealed class ExporterLinkOption
{
    public long Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string SourceDocumentName { get; set; } = string.Empty;
}

[TsExport]
public sealed class ExporterProfileOption
{
    public string Name { get; set; } = string.Empty;

    public string Scope { get; set; } = "global";

    public string DisplayName { get; set; } = string.Empty;

    public ExporterSettingsPayload Settings { get; set; } = new();
}

[TsExport]
public sealed class ExporterNamedOption
{
    public string Name { get; set; } = string.Empty;
}

[TsExport]
public sealed class ExporterCrsPresetGroup
{
    public string Region { get; set; } = string.Empty;

    public List<ExporterCrsPreset> Entries { get; set; } = new();
}

[TsExport]
public sealed class ExporterCrsPreset
{
    public int Epsg { get; set; }

    public string DisplayName { get; set; } = string.Empty;
}

[TsExport]
public sealed class ExporterSettingsPayload
{
    public string OutputDirectory { get; set; } = string.Empty;

    public int TargetEpsg { get; set; } = 4326;

    public string CoordinateMode { get; set; } = "shared";

    public string OutputFormat { get; set; } = "geopackage";

    public string IncrementalExportMode { get; set; } = "all";

    public string PackagingMode { get; set; } = "perFeature";

    public List<long> SelectedViewIds { get; set; } = new();

    public List<long> SelectedLinkIds { get; set; } = new();

    public bool IncludeLinkedModels { get; set; }

    public bool Unit { get; set; } = true;

    public bool Detail { get; set; } = true;

    public bool Opening { get; set; } = true;

    public bool Level { get; set; } = true;

    public bool Fixture { get; set; } = true;

    public bool GenerateDiagnosticsReport { get; set; } = true;

    public bool GeneratePackageOutput { get; set; }

    public bool IncludePackageLegend { get; set; } = true;

    public bool ValidateAfterWrite { get; set; } = true;

    public bool GenerateQgisArtifacts { get; set; }

    public bool OpenOutputFolder { get; set; }

    public bool LaunchQgis { get; set; }

    public string UnitGeometrySource { get; set; } = "floors";

    public string UnitAttributeSource { get; set; } = "floors";

    public string RoomCategoryParameterName { get; set; } = "Name";

    public string ActiveSchemaProfileName { get; set; } = "Core";

    public string ActiveValidationPolicyProfileName { get; set; } = "Recommended";

    public bool SimplifyStairUnits { get; set; }

    public bool SimplifyEscalatorUnits { get; set; }

    public bool Use3DSectionBoxExport { get; set; }

    public double SectionBoxAboveFloorMeters { get; set; } = 1.2;

    public double SectionBoxBelowFloorMeters { get; set; }

    public bool Keep3DTempViewsForDebug { get; set; }

    public string? SelectedProfileName { get; set; }
}

[TsExport]
public sealed class ExporterSaveProfileRequest
{
    public string Scope { get; set; } = "global";

    public string Name { get; set; } = string.Empty;

    public ExporterSettingsPayload Settings { get; set; } = new();
}

[TsExport]
public sealed class ExporterDeleteProfileRequest
{
    public string Scope { get; set; } = "global";

    public string Name { get; set; } = string.Empty;
}

[TsExport]
public sealed class ExporterSubmitResponse
{
    public bool Accepted { get; set; }

    public string? Error { get; set; }
}

[TsExport]
public sealed class ExporterRunResponse
{
    public bool Success { get; set; }

    public string? Error { get; set; }

    public ExportResultInitialStateResponse? Result { get; set; }
}
