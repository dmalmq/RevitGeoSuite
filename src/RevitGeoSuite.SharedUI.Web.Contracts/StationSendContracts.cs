namespace RevitGeoSuite.SharedUI.Web.Contracts;

// Contracts for "Send to Kanizu": the GIS package, with the level and room indexes but no 3D
// Tiles, written as a new version to the model's station folder, and what changed since the
// previous version there.

[TsExport]
public sealed class StationSendStateRequest
{
    /// <summary>A folder the user picked, to classify instead of the remembered one.</summary>
    public string? StationFolder { get; set; }
}

[TsExport]
public sealed class StationSendStateResponse
{
    /// <summary>The station folder the model remembers, or empty before its first send.</summary>
    public string StationFolder { get; set; } = string.Empty;

    /// <summary>The floor plan profile the send uses: the model's first, as the Cesium export starts with.</summary>
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>
    /// The active document's key. The run sends it back and is refused when another model is
    /// active by then, so one model's GIS never lands in another's station folder.
    /// </summary>
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>
    /// <c>missing</c> (no such folder), <c>new</c> (no Kanizu versions in it yet: the first send
    /// asks first) or <c>station</c>. Empty without a folder.
    /// </summary>
    public string FolderKind { get; set; } = string.Empty;

    /// <summary>
    /// True when this model's last version in the folder was written from another file, as after
    /// Save As: the ribbon then waits for a click instead of sending at once.
    /// </summary>
    public bool LastSentFromAnotherFile { get; set; }
}

[TsExport]
public sealed class StationSendRunRequest
{
    /// <summary>The station folder; the model remembers it after the send succeeds.</summary>
    public string StationFolder { get; set; } = string.Empty;

    /// <summary>The document key from <c>station.send.getState</c>.</summary>
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>The user agreed to make a folder without Kanizu versions the station folder.</summary>
    public bool ConfirmNewFolder { get; set; }
}

[TsExport]
public sealed class StationSendLayerChange
{
    public string Layer { get; set; } = string.Empty;

    public int Added { get; set; }

    public int Removed { get; set; }

    public int Edited { get; set; }
}

[TsExport]
public sealed class StationSendRunResponse
{
    /// <summary>The new version folder under <c>revit/</c>.</summary>
    public string VersionFolder { get; set; } = string.Empty;

    /// <summary>False on the model's first send to this station folder.</summary>
    public bool HasPrevious { get; set; }

    /// <summary>
    /// False when the version was written but comparing it with the previous one failed; the counts
    /// are then zero and mean nothing.
    /// </summary>
    public bool ChangesCounted { get; set; }

    /// <summary>The previous version was written with other GIS rules, so edits may come from the rules.</summary>
    public bool RulesDiffer { get; set; }

    public int ElementsAdded { get; set; }

    public int ElementsRemoved { get; set; }

    public int ElementsEdited { get; set; }

    public int ElementsChanged { get; set; }

    /// <summary>Layers with a change, by name.</summary>
    public StationSendLayerChange[] Layers { get; set; } = System.Array.Empty<StationSendLayerChange>();

    public double Seconds { get; set; }

    public string[] Warnings { get; set; } = System.Array.Empty<string>();
}
