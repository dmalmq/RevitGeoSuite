using System;

namespace RevitGeoSuite.SharedUI.Web.Contracts;

/// <summary>A geographic point in degrees (JGD2011 latitude/longitude).</summary>
[TsExport]
public sealed class GeoreferencePoint
{
    public double Lat { get; set; }

    public double Lon { get; set; }
}

/// <summary>Payload of the <c>georeference.getCrsOrigin</c> request.</summary>
[TsExport]
public sealed class GeoreferenceCrsOriginRequest
{
    public string CrsCode { get; set; } = string.Empty;
}

/// <summary>Payload of the <c>georeference.getGridCandidates</c> request.</summary>
[TsExport]
public sealed class GeoreferenceGridCandidatesRequest
{
    public double Lat { get; set; }

    public double Lon { get; set; }

    public string CrsCode { get; set; } = string.Empty;
}

/// <summary>A single PLATEAU mesh-grid candidate around the selected area.</summary>
[TsExport]
public sealed class GeoreferenceGridCandidate
{
    public string TileId { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public bool IsSeedCandidate { get; set; }
}

/// <summary>Response of the <c>georeference.getGridCandidates</c> request.</summary>
[TsExport]
public sealed class GeoreferenceGridCandidatesResponse
{
    public GeoreferenceGridCandidate[] Candidates { get; set; } = Array.Empty<GeoreferenceGridCandidate>();

    /// <summary>FeatureCollection (featureId/tileId/isSelected/isSuggested) ready for the map overlay.</summary>
    public string OverlayGeoJson { get; set; } = string.Empty;

    public double CenterLat { get; set; }

    public double CenterLon { get; set; }
}

/// <summary>Payload of the <c>georeference.resolveGridBasePoint</c> request.</summary>
[TsExport]
public sealed class GeoreferenceResolveBasePointRequest
{
    public string[] SelectedMeshCodes { get; set; } = Array.Empty<string>();

    public string CrsCode { get; set; } = string.Empty;
}

/// <summary>Response describing the Project Base Point resolved from the selected grids (south-west corner).</summary>
[TsExport]
public sealed class GeoreferenceBasePointResponse
{
    public string[] SelectedMeshCodes { get; set; } = Array.Empty<string>();

    public double Lat { get; set; }

    public double Lon { get; set; }

    public double Easting { get; set; }

    public double Northing { get; set; }
}

/// <summary>Payload of the <c>georeference.getCurrentState</c> request. Empty — the handler reads the active document on the server side.</summary>
[TsExport]
public sealed class GeoreferenceCurrentStateRequest
{
}

/// <summary>Payload of the <c>georeference.apply</c> request.</summary>
[TsExport]
public sealed class GeoreferenceApplyRequest
{
    public string CrsCode { get; set; } = string.Empty;

    public string[] SelectedMeshCodes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// When true, apply uses <see cref="Workflow.PlacementApplyMode.MetadataOnly"/>: the existing
    /// Revit <c>ProjectPosition</c> is left untouched and only the GeoSuite shared metadata
    /// (<c>GeoProjectInfo</c> + <c>WorkingProjectBasePoint</c>) is persisted. This is the
    /// "Confirm existing setup" path.
    /// </summary>
    public bool ConfirmExistingSetup { get; set; }
}

/// <summary>Response of the <c>georeference.apply</c> request.</summary>
[TsExport]
public sealed class GeoreferenceApplyResponse
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;
}

/// <summary>Snapshot of a base point (Survey Point or Project Base Point) read from the active document.</summary>
[TsExport]
public sealed class GeoreferenceBasePointSnapshot
{
    public string Name { get; set; } = string.Empty;

    public double XFeet { get; set; }

    public double YFeet { get; set; }

    public double ZFeet { get; set; }

    public double? SharedEastWestFeet { get; set; }

    public double? SharedNorthSouthFeet { get; set; }

    public double? SharedElevationFeet { get; set; }

    public double? EstimatedLatitudeDegrees { get; set; }

    public double? EstimatedLongitudeDegrees { get; set; }
}

/// <summary>Snapshot of <c>ProjectPosition</c> at the Revit internal origin.</summary>
[TsExport]
public sealed class GeoreferenceProjectPositionSnapshot
{
    public double EastWestFeet { get; set; }

    public double NorthSouthFeet { get; set; }

    public double ElevationFeet { get; set; }

    public double AngleDegrees { get; set; }
}

/// <summary>
/// Response of the <c>georeference.getCurrentState</c> request. Mirrors
/// <c>CurrentProjectStateSummary</c> so the web shell can detect existing coordinate setups
/// (Revit shared coordinates, non-zero <c>ProjectPosition</c>, or already-stored GeoProjectInfo)
/// and offer a "confirm existing" workflow instead of overwriting meaningful values.
/// </summary>
[TsExport]
public sealed class GeoreferenceCurrentStateResponse
{
    public string DocumentTitle { get; set; } = string.Empty;

    public bool IsSupportedDocument { get; set; } = true;

    public bool IsReadOnly { get; set; }

    public string StatusMessage { get; set; } = string.Empty;

    /// <summary>True if any <c>ProjectPosition</c> offset/angle is non-default or stored GeoProjectInfo exists.</summary>
    public bool ExistingSetupDetected { get; set; }

    public string ExistingSetupMessage { get; set; } = string.Empty;

    /// <summary>True if both base points have a readable shared position (the WPF "confirm existing" gate).</summary>
    public bool HasDetectedExistingPointSetup { get; set; }

    public bool HasStoredGeoInfo { get; set; }

    public int? StoredCrsEpsgCode { get; set; }

    public double? StoredOriginLatitude { get; set; }

    public double? StoredOriginLongitude { get; set; }

    public double? StoredTrueNorthAngleDegrees { get; set; }

    public double? SiteLatitudeDegrees { get; set; }

    public double? SiteLongitudeDegrees { get; set; }

    public GeoreferenceProjectPositionSnapshot ProjectPosition { get; set; } = new GeoreferenceProjectPositionSnapshot();

    public GeoreferenceBasePointSnapshot SurveyPoint { get; set; } = new GeoreferenceBasePointSnapshot { Name = "Survey Point" };

    public GeoreferenceBasePointSnapshot ProjectBasePoint { get; set; } = new GeoreferenceBasePointSnapshot { Name = "Project Base Point" };
}

/// <summary>Payload of the <c>georeference.getAvailableCrs</c> request.</summary>
[TsExport]
public sealed class AvailableCrsRequest
{
}

/// <summary>One CRS entry in a region group.</summary>
[TsExport]
public sealed class CrsOptionEntry
{
    public int EpsgCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AreaSummary { get; set; } = string.Empty;
}

/// <summary>A group of CRS entries for a geographic region.</summary>
[TsExport]
public sealed class CrsOptionGroup
{
    public string Region { get; set; } = string.Empty;
    public CrsOptionEntry[] Entries { get; set; } = Array.Empty<CrsOptionEntry>();
}

/// <summary>Response of the <c>georeference.getAvailableCrs</c> request.</summary>
[TsExport]
public sealed class AvailableCrsResponse
{
    public CrsOptionGroup[] Groups { get; set; } = Array.Empty<CrsOptionGroup>();
}

/// <summary>Payload of the <c>georeference.revert</c> request.</summary>
[TsExport]
public sealed class GeoreferenceRevertRequest
{
}

/// <summary>Result of the <c>georeference.revert</c> request.</summary>
[TsExport]
public sealed class GeoreferenceRevertResponse
{
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
}

/// <summary>Result of the <c>georeference.hasUndoSnapshot</c> request.</summary>
[TsExport]
public sealed class GeoreferenceHasUndoResponse
{
    public bool HasSnapshot { get; set; }
    public string Summary { get; set; } = string.Empty;
}
