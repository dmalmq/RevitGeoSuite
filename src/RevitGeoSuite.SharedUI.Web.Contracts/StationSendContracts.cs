namespace RevitGeoSuite.SharedUI.Web.Contracts;

// Contracts for "Send to Kanizu": the GIS package, with the level and room indexes but no 3D
// Tiles, written as a new version to the model's station folder, and what changed since the
// previous version there.

[TsExport]
public sealed class StationSendStateRequest { }

[TsExport]
public sealed class StationSendStateResponse
{
    /// <summary>The station folder the model remembers, or empty before its first send.</summary>
    public string StationFolder { get; set; } = string.Empty;

    /// <summary>The floor plan profile the send uses: the model's first, as the Cesium export starts with.</summary>
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>The model's file name, for the page title.</summary>
    public string ModelName { get; set; } = string.Empty;
}

[TsExport]
public sealed class StationSendRunRequest
{
    /// <summary>The station folder; the model remembers it for the next send.</summary>
    public string StationFolder { get; set; } = string.Empty;
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

    public int ElementsAdded { get; set; }

    public int ElementsRemoved { get; set; }

    public int ElementsEdited { get; set; }

    public int ElementsChanged { get; set; }

    /// <summary>Layers with a change, by name.</summary>
    public StationSendLayerChange[] Layers { get; set; } = System.Array.Empty<StationSendLayerChange>();

    public double Seconds { get; set; }

    public string[] Warnings { get; set; } = System.Array.Empty<string>();
}
