using RevitGeoSuite.FloorPlanExport.Core.Showcase;

namespace RevitGeoSuite.FloorPlanExport.Showcase;

/// <summary>Options controlling <see cref="ShowcaseCompanionBuilder"/> execution.</summary>
public sealed class ShowcaseCompanionBuildOptions
{
    public static ShowcaseCompanionBuildOptions Default { get; } = new();

    /// <summary>
    /// Optional Revit template (<c>.rte</c>) used when creating a fresh companion <c>.rvt</c>.
    /// When omitted, Revit creates a fresh metric project document (<c>UnitSystem.Metric</c>).
    /// </summary>
    public string? TemplateFilePath { get; set; }

    /// <summary>
    /// Optional output <c>.rvt</c> path (e.g. <c>&lt;Station&gt;_GIS_Showcase.rvt</c>).
    /// When provided, the newly built companion document is saved to this path.
    /// </summary>
    public string? OutputFilePath { get; set; }

    /// <summary>Whether <see cref="OutputFilePath"/> overwrites an existing companion <c>.rvt</c>.</summary>
    public bool OverwriteExistingFile { get; set; } = true;

    /// <summary>When <c>true</c> and <see cref="OutputFilePath"/> is saved, closes the created companion document after saving.</summary>
    public bool CloseDocumentAfterSave { get; set; }

    /// <summary>
    /// Optional explicit CRS-to-Revit-internal transform. When <c>null</c>, derived automatically from the target
    /// document's active <c>ProjectLocation</c>.
    /// </summary>
    public ShowcaseCoordinateTransform? CoordinateTransformOverride { get; set; }

    /// <summary>F3 extrusion heights, clearances, and non-occluding level-band settings.</summary>
    public ShowcaseGeometryPlanOptions GeometryOptions { get; set; } = new();

    /// <summary>F4 POI marker, <c>point_facility_network</c> attachment, 3D network ribbon/tube, and <c>net_junction</c> settings.</summary>
    public ShowcasePoiAndNetworkPlanOptions PoiAndNetworkOptions { get; set; } = new();

    /// <summary>
    /// Optional pre-built <c>KNZ_FRM_PoiMarker.rfa</c> path. When provided (or already loaded in the companion
    /// document) and <see cref="UsePoiMarkerFamilyInstances"/> is <c>true</c>, <c>point_facility</c> markers are
    /// placed as instanced <c>FamilyInstance</c>s; otherwise instanced <c>DirectShapeType</c> prototypes are used.
    /// </summary>
    public string? PoiMarkerFamilyPath { get; set; }

    /// <summary>
    /// Optional Generic Model <c>.rft</c> template path used by <see cref="ShowcasePoiMarkerFamilyBuilder"/> to
    /// programmatically construct and load <c>KNZ_FRM_PoiMarker</c> when <see cref="PoiMarkerFamilyPath"/> is not supplied.
    /// </summary>
    public string? PoiMarkerFamilyTemplatePath { get; set; }

    /// <summary>
    /// When <c>true</c> (default), prefers instanced <c>KNZ_FRM_PoiMarker</c> <c>FamilyInstance</c>s whenever a
    /// family symbol or <c>.rfa</c>/<c>.rft</c> path is available, falling back automatically to instanced
    /// <c>DirectShapeType</c> prototypes when no family template is supplied.
    /// </summary>
    public bool UsePoiMarkerFamilyInstances { get; set; } = true;
}
