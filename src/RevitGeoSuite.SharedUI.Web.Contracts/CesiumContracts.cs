namespace RevitGeoSuite.SharedUI.Web.Contracts;

// Contracts for the combined "Export to Cesium" flow: one action that runs the 3D Tiles
// export and the floor-plan GIS export into a single package folder (tiles/ + gis/ +
// cesium-package.json) and optionally pushes it to a running Cesium viewer.

[TsExport]
public sealed class CesiumExportStateRequest { }

[TsExport]
public sealed class CesiumExportStateResponse
{
    /// <summary>Saved floor-plan export profile names for the active project (global + project scope).</summary>
    public string[] FloorPlanProfiles { get; set; } = System.Array.Empty<string>();

    public string LastOutputFolder { get; set; } = string.Empty;

    public string ViewerUrl { get; set; } = string.Empty;

    public bool HasToken { get; set; }

    /// <summary>True when no floor-plan profile exists yet — the UI walks through first-run setup.</summary>
    public bool FirstRun { get; set; }
}

[TsExport]
public sealed class CesiumExportRunRequest
{
    public string OutputFolder { get; set; } = string.Empty;

    public string FloorPlanProfileName { get; set; } = string.Empty;

    /// <summary>Push the finished package to the configured Cesium viewer.</summary>
    public bool Push { get; set; }

    // 3D Tiles options, mirroring tiles3d.export.
    public string Scope { get; set; } = "whole";

    public string Lod { get; set; } = "fine";

    public bool PreciseCrs { get; set; }

    public double? GeoidOffset { get; set; }

    public string? SelectedViewUniqueId { get; set; }

    public string[] SelectedLinkUniqueIds { get; set; } = System.Array.Empty<string>();
}

[TsExport]
public sealed class CesiumExportRunResponse
{
    public string PackageRoot { get; set; } = string.Empty;

    public string ManifestPath { get; set; } = string.Empty;

    public string TilesetPath { get; set; } = string.Empty;

    public string[] GisArtifacts { get; set; } = System.Array.Empty<string>();

    public bool Pushed { get; set; }

    public string PushMessage { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string[] Warnings { get; set; } = System.Array.Empty<string>();
}

[TsExport]
public sealed class CesiumPushRequest
{
    /// <summary>Existing export folder (a 3D Tiles bundle or GIS output) to send as a package.</summary>
    public string Folder { get; set; } = string.Empty;
}

[TsExport]
public sealed class CesiumPushResponse
{
    public bool Pushed { get; set; }

    public string Message { get; set; } = string.Empty;
}

[TsExport]
public sealed class CesiumViewerSettingsGetRequest { }

[TsExport]
public sealed class CesiumViewerSettingsPayload
{
    public string ViewerUrl { get; set; } = string.Empty;

    /// <summary>Optional bearer token; empty string clears it. Never echoed back on get.</summary>
    public string? Token { get; set; }

    public bool HasToken { get; set; }
}
