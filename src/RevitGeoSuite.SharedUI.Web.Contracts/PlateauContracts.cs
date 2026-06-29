using System;
using System.Collections.Generic;

namespace RevitGeoSuite.SharedUI.Web.Contracts;

/// <summary>Payload of the <c>plateau.scanFolder</c> request.</summary>
[TsExport]
public sealed class PlateauScanRequest
{
    public string Path { get; set; } = string.Empty;
}

/// <summary>Payload of the <c>plateau.importTiles</c> request.</summary>
[TsExport]
public sealed class PlateauImportRequest
{
    public string Path { get; set; } = string.Empty;
    public string[] TileIds { get; set; } = Array.Empty<string>();

    /// <summary>"solids" (default) builds 3D DirectShapes; "dxf" imports a lightweight 2D CAD basemap.</summary>
    public string Mode { get; set; } = "solids";
}

/// <summary>Payload of the <c>gis.export</c> request.</summary>
[TsExport]
public sealed class GisExportRequest
{
    public string Path { get; set; } = string.Empty;

    public string[] Paths { get; set; } = Array.Empty<string>();

    public string BasemapName { get; set; } = string.Empty;

    public string OutputFolder { get; set; } = string.Empty;

    public GisExportFileAssignment[] FileAssignments { get; set; } = Array.Empty<GisExportFileAssignment>();

    public GisExportCategoryColor[] CategoryColors { get; set; } = Array.Empty<GisExportCategoryColor>();
}

/// <summary>One selected GIS file and the Revit level its DXF should be grouped under.</summary>
[TsExport]
public sealed class GisExportFileAssignment
{
    public string Path { get; set; } = string.Empty;

    public long? LevelId { get; set; }

    public string Category { get; set; } = "detail";
}

/// <summary>Editable DXF layer colour for a GIS export category.</summary>
[TsExport]
public sealed class GisExportCategoryColor
{
    public string Category { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;
}

/// <summary>Payload of the <c>gis.exportOptions</c> request.</summary>
[TsExport]
public sealed class GisExportOptionsRequest
{
}

/// <summary>Response of the <c>gis.exportOptions</c> request.</summary>
[TsExport]
public sealed class GisExportOptionsResponse
{
    public GisLevelOption[] Levels { get; set; } = Array.Empty<GisLevelOption>();

    public long? DefaultLevelId { get; set; }
}

/// <summary>One Revit level available for floor-aware GIS DXF export.</summary>
[TsExport]
public sealed class GisLevelOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public double ElevationFeet { get; set; }
}

/// <summary>Payload of the <c>plateau.onlineCatalog</c> request.</summary>
[TsExport]
public sealed class PlateauOnlineCatalogRequest
{
}

/// <summary>Payload of the <c>plateau.areaLocation</c> request.</summary>
[TsExport]
public sealed class PlateauAreaLocationRequest
{
    public string AreaCode { get; set; } = string.Empty;
}

/// <summary>Representative map location for a searchable PLATEAU Online area.</summary>
[TsExport]
public sealed class PlateauAreaLocationResponse
{
    public string AreaCode { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public int Zoom { get; set; }
}

/// <summary>One searchable PLATEAU Online municipality/ward row.</summary>
[TsExport]
public sealed class PlateauOnlineArea
{
    public string Code { get; set; } = string.Empty;
    public string[] Aliases { get; set; } = Array.Empty<string>();
    public string Label { get; set; } = string.Empty;
    public string Prefecture { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string CodeLabel { get; set; } = string.Empty;
    public string SearchTokens { get; set; } = string.Empty;
    public bool HasBuildings { get; set; }

    /// <summary>Representative point (bbox center) for plotting the area on the import map; null when unknown.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

/// <summary>One area in a multi-municipality suggestion (border-area detection).</summary>
[TsExport]
public sealed class PlateauOnlineSuggestionArea
{
    public string AreaCode { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string CodeLabel { get; set; } = string.Empty;
    public double NearestDistanceMeters { get; set; }
}

/// <summary>Project-location based PLATEAU Online area suggestion.</summary>
[TsExport]
public sealed class PlateauOnlineSuggestion
{
    public string AreaCode { get; set; } = string.Empty;
    public string AreaLabel { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string CodeLabel { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public PlateauOnlineSuggestionArea[] Areas { get; set; } = Array.Empty<PlateauOnlineSuggestionArea>();
}

/// <summary>Result payload emitted by the <c>plateau.onlineCatalog</c> job.</summary>
[TsExport]
public sealed class PlateauOnlineCatalogResponse
{
    public PlateauOnlineArea[] Areas { get; set; } = Array.Empty<PlateauOnlineArea>();
    public PlateauOnlineSuggestion? Suggestion { get; set; }
    public int AreaCount { get; set; }
    public int DatasetCount { get; set; }
    public bool DracoAvailable { get; set; }
    public string DracoMessage { get; set; } = string.Empty;
}

/// <summary>Payload of the <c>plateau.onlineGrids</c> request.</summary>
[TsExport]
public sealed class PlateauOnlineGridsRequest
{
    public string AreaCode { get; set; } = string.Empty;
    public string[] AreaCodes { get; set; } = Array.Empty<string>();
}

/// <summary>One selectable online PLATEAU grid cell for an area.</summary>
[TsExport]
public sealed class PlateauOnlineGrid
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Lod { get; set; } = string.Empty;
    public bool? Texture { get; set; }
    public object? Geometry { get; set; }
    public string[] AreaCodes { get; set; } = Array.Empty<string>();
    public string[] AreaLabels { get; set; } = Array.Empty<string>();
}

/// <summary>Result payload emitted by the <c>plateau.onlineGrids</c> job.</summary>
[TsExport]
public sealed class PlateauOnlineGridsResponse
{
    public string AreaCode { get; set; } = string.Empty;
    public string AreaLabel { get; set; } = string.Empty;
    public string[] AreaCodes { get; set; } = Array.Empty<string>();
    public string[] AreaLabels { get; set; } = Array.Empty<string>();
    public PlateauOnlineGrid[] Grids { get; set; } = Array.Empty<PlateauOnlineGrid>();
    public int GridCount { get; set; }
    public string DatasetLod { get; set; } = string.Empty;
    public bool? DatasetTexture { get; set; }
}

/// <summary>Selected grids grouped by PLATEAU Online area for multi-municipality imports.</summary>
[TsExport]
public sealed class PlateauOnlineAreaGridSelection
{
    public string AreaCode { get; set; } = string.Empty;
    public string[] GridIds { get; set; } = Array.Empty<string>();
}

/// <summary>Payload of the <c>plateau.onlineImport</c> request.</summary>
[TsExport]
public sealed class PlateauOnlineImportRequest
{
    public string AreaCode { get; set; } = string.Empty;
    public string[] GridIds { get; set; } = Array.Empty<string>();
    public string[] AreaCodes { get; set; } = Array.Empty<string>();
    public PlateauOnlineAreaGridSelection[] AreaSelections { get; set; } = Array.Empty<PlateauOnlineAreaGridSelection>();

    /// <summary>"solids" (default) builds 3D DirectShapes; "dxf" imports a lightweight 2D CAD basemap.</summary>
    public string Mode { get; set; } = "solids";

    /// <summary>Online DXF basemap categories: "buildings", "roads", "landuse".</summary>
    public string[] Categories { get; set; } = Array.Empty<string>();
}

/// <summary>Per-area result from a multi-area PLATEAU Online import.</summary>
[TsExport]
public sealed class PlateauOnlineImportAreaResult
{
    public string AreaCode { get; set; } = string.Empty;
    public string AreaLabel { get; set; } = string.Empty;
    public int ImportedElements { get; set; }
    public int Groups { get; set; }
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>One searchable municipality that has downloadable CityGML on geospatial.jp (CKAN).</summary>
[TsExport]
public sealed class PlateauCkanArea
{
    public string Code { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string CodeLabel { get; set; } = string.Empty;

    /// <summary>Romanized area name (e.g. "Sumida-ku") used to label downloaded folders.</summary>
    public string EnglishName { get; set; } = string.Empty;

    /// <summary>SOH-delimited, lowercased EN/JP/romaji/code token bag (same shape as <see cref="PlateauOnlineArea"/>).</summary>
    public string SearchTokens { get; set; } = string.Empty;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>Available fiscal years for this municipality, newest-first.</summary>
    public string[] Years { get; set; } = Array.Empty<string>();
    public string LatestYear { get; set; } = string.Empty;

    /// <summary>Maps each year to its exact CKAN dataset slug.</summary>
    public Dictionary<string, string> SlugByYear { get; set; } = new();
}

/// <summary>Result payload emitted by the <c>plateau.ckanCatalog</c> job.</summary>
[TsExport]
public sealed class PlateauCkanCatalogResponse
{
    public PlateauCkanArea[] Areas { get; set; } = Array.Empty<PlateauCkanArea>();
    public int AreaCount { get; set; }
}

/// <summary>Payload of the <c>plateau.ckanResources</c> request (one dataset slug).</summary>
[TsExport]
public sealed class PlateauCkanResourcesRequest
{
    public string Slug { get; set; } = string.Empty;
}

/// <summary>One downloadable CityGML resource (a feature/LOD ZIP) within a dataset.</summary>
[TsExport]
public sealed class PlateauCkanResource
{
    public string Id { get; set; } = string.Empty;
    public string FeatureKey { get; set; } = string.Empty;
    public string FeatureLabel { get; set; } = string.Empty;
    public string Lod { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;

    /// <summary>True for resources pre-checked in the UI (highest-LOD buildings).</summary>
    public bool DefaultSelected { get; set; }
}

/// <summary>Result payload of the <c>plateau.ckanResources</c> request.</summary>
[TsExport]
public sealed class PlateauCkanResourcesResponse
{
    public string Slug { get; set; } = string.Empty;
    public PlateauCkanResource[] Resources { get; set; } = Array.Empty<PlateauCkanResource>();
}

/// <summary>Payload of the <c>plateau.ckanDownload</c> request.</summary>
[TsExport]
public sealed class PlateauCkanDownloadRequest
{
    public string Code { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public string[] ResourceUrls { get; set; } = Array.Empty<string>();

    /// <summary>Optional root folder to save into; empty uses the default app-data downloads folder.</summary>
    public string DestinationFolder { get; set; } = string.Empty;

    /// <summary>Optional romanized area name appended to the package folder, e.g. "Sumida-ku".</summary>
    public string AreaName { get; set; } = string.Empty;

    /// <summary>When true, re-download and overwrite resources already present on disk.</summary>
    public bool Force { get; set; }
}

/// <summary>Result of <c>plateau.ckanDownloadCheck</c>: which selected resources already exist locally.</summary>
[TsExport]
public sealed class PlateauCkanDownloadCheckResponse
{
    /// <summary>URLs (from the request) already downloaded in the target folder.</summary>
    public string[] ExistingUrls { get; set; } = Array.Empty<string>();
}

/// <summary>Result payload emitted by the <c>plateau.ckanDownload</c> job.</summary>
[TsExport]
public sealed class PlateauCkanDownloadResponse
{
    public string FolderPath { get; set; } = string.Empty;
    public int FilesExtracted { get; set; }
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>Result payload emitted by the <c>plateau.onlineImport</c> job.</summary>
[TsExport]
public sealed class PlateauOnlineImportResponse
{
    public string AreaCode { get; set; } = string.Empty;
    public string AreaLabel { get; set; } = string.Empty;
    public int ImportedElements { get; set; }
    public int Groups { get; set; }
    public string Mode { get; set; } = "solids";
    public string Summary { get; set; } = string.Empty;
    public string[] Warnings { get; set; } = Array.Empty<string>();
    public PlateauOnlineImportAreaResult[] AreaResults { get; set; } = Array.Empty<PlateauOnlineImportAreaResult>();
}
