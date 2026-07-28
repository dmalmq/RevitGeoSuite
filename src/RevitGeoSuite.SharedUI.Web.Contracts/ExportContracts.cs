using System.Collections.Generic;

namespace RevitGeoSuite.SharedUI.Web.Contracts;

[TsExport]
public sealed class PlateauContextExportRequest
{
    public string FolderPath { get; set; } = string.Empty;

    public string? Path { get; set; }

    public string OutputFolder { get; set; } = string.Empty;

    public string KibanFolderPath { get; set; } = string.Empty;

    public string[] SelectedTileIds { get; set; } = System.Array.Empty<string>();

    public string[]? TileIds { get; set; }

    public string[]? SelectedFeatureTypes { get; set; }

    public string[]? FeatureTypes { get; set; }

    public string[] SelectedKibanLayers { get; set; } = System.Array.Empty<string>();

    public string[]? KibanLayers { get; set; }

    public string[]? AdditionalGreenLandUseTokens { get; set; }

    public string[]? GreenLandUseTokens { get; set; }

    public PlateauContextExportOptions Options { get; set; } = new PlateauContextExportOptions();
}

[TsExport]
public sealed class PlateauContextExportOptions
{
    public PlateauContextExportFormats Formats { get; set; } = new PlateauContextExportFormats();

    public bool? Shapefile { get; set; }

    public bool? Dxf { get; set; }

    public bool IncludeContext { get; set; } = true;

    public bool IncludeKiban { get; set; } = true;

    public bool IncludeRevitModel { get; set; }
}

[TsExport]
public sealed class PlateauContextExportFormats
{
    public bool Shapefile { get; set; } = true;

    public bool Dxf { get; set; } = true;
}

[TsExport]
public sealed class PlateauContextExportPreviewResponse
{
    public int FeatureCount { get; set; }

    public Dictionary<string, int> PerLayerCounts { get; set; } = new Dictionary<string, int>();

    public string Crs { get; set; } = string.Empty;

    public string[] Warnings { get; set; } = System.Array.Empty<string>();
}

[TsExport]
public sealed class PlateauContextExportResponse
{
    public int Files { get; set; }

    public string[] FilePaths { get; set; } = System.Array.Empty<string>();

    public int ExportedFeatures { get; set; }

    public int ExportedElements { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string[] Warnings { get; set; } = System.Array.Empty<string>();
}

[TsExport]
public sealed class Tiles3DExportOptionsRequest { }

[TsExport]
public sealed class Tiles3DLinkOption
{
    public string UniqueId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

[TsExport]
public sealed class Tiles3DViewOption
{
    public long ViewId { get; set; }

    public string UniqueId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

[TsExport]
public sealed class Tiles3DExportOptionsResponse
{
    public Tiles3DLinkOption[] Links { get; set; } = System.Array.Empty<Tiles3DLinkOption>();

    public Tiles3DViewOption[] Views { get; set; } = System.Array.Empty<Tiles3DViewOption>();

    public string DefaultViewUniqueId { get; set; } = string.Empty;
}

[TsExport]
public sealed class Tiles3DExportPrepareRequest
{
    public string Scope { get; set; } = "whole";

    public Tiles3DExportOptions Options { get; set; } = new Tiles3DExportOptions();
}

[TsExport]
public sealed class Tiles3DExportRequest
{
    public string Scope { get; set; } = "whole";

    public string OutputFolder { get; set; } = string.Empty;

    public Tiles3DExportOptions Options { get; set; } = new Tiles3DExportOptions();
}

[TsExport]
public sealed class Tiles3DExportOptions
{
    public string Lod { get; set; } = "fine";

    public string GeometryMode { get; set; } = "lightweight";

    public bool SplitByLevel { get; set; }

    public bool PreciseCrs { get; set; }

    /// <summary>
    /// Null means auto-detect from EGM2008 at the export anchor when precise CRS projection is enabled.
    /// </summary>
    public double? GeoidOffset { get; set; }

    public string[] SelectedLinkUniqueIds { get; set; } = System.Array.Empty<string>();

    public string SelectedViewUniqueId { get; set; } = string.Empty;
}

[TsExport]
public sealed class Tiles3DExportPrepareResponse
{
    public int ElementCount { get; set; }

    public int TriangleCount { get; set; }

    public string Crs { get; set; } = string.Empty;

    public double GeoidOffsetMeters { get; set; }

    public string[] Warnings { get; set; } = System.Array.Empty<string>();
}

[TsExport]
public sealed class Tiles3DExportResponse
{
    public int ExportedElements { get; set; }

    public int TriangleCount { get; set; }

    public int Files { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string TilesetPath { get; set; } = string.Empty;

    public string[] Warnings { get; set; } = System.Array.Empty<string>();
}

[TsExport]
public sealed class CityGmlExportPrepareRequest
{
    public CityGmlExportOptions Options { get; set; } = new CityGmlExportOptions();
}

[TsExport]
public sealed class CityGmlExportRequest
{
    public string OutputFolder { get; set; } = string.Empty;

    public CityGmlExportOptions Options { get; set; } = new CityGmlExportOptions();
}

[TsExport]
public sealed class CityGmlExportOptions
{
    public string SchemaVersion { get; set; } = "2.0";

    public Dictionary<string, string> CategoryOverrides { get; set; } = new Dictionary<string, string>();

    public Dictionary<string, string> CodelistOverrides { get; set; } = new Dictionary<string, string>();
}

[TsExport]
public sealed class CityGmlExportPrepareResponse
{
    public int FeatureCount { get; set; }

    public string Crs { get; set; } = string.Empty;

    public string[] Warnings { get; set; } = System.Array.Empty<string>();
}

[TsExport]
public sealed class CityGmlExportResponse
{
    public int ExportedElements { get; set; }

    public int FeatureCount { get; set; }

    public int Files { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string ExportPath { get; set; } = string.Empty;

    public string[] Warnings { get; set; } = System.Array.Empty<string>();
}
