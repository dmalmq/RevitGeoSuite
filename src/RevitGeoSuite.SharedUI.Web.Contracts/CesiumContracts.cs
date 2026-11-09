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

    /// <summary>The height datum chosen on the last export of this model: "TP" or "unknown".</summary>
    public string LastHeightDatum { get; set; } = "unknown";

    /// <summary>Whether the last export of this model wrote a new version into a station folder.</summary>
    public bool LastKeepVersions { get; set; }
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

    public bool PreciseCrs { get; set; } = true;

    public double? GeoidOffset { get; set; }

    /// <summary>
    /// What the model's elevations are measured from: "TP" (Tokyo Peil) or "unknown". Written to
    /// the manifest so Kanizu knows whether it may take floor heights from Revit.
    /// </summary>
    public string HeightDatum { get; set; } = "unknown";

    /// <summary>
    /// Treat <see cref="OutputFolder"/> as a station folder: write a new version under
    /// <c>revit/&lt;model&gt;/</c> instead of replacing the package in place. Kanizu reads these.
    /// </summary>
    public bool KeepVersions { get; set; }

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
