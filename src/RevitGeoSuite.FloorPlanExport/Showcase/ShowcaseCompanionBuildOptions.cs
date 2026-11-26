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

    /// <summary>Extrusion heights, clearances, and non-occluding level-band settings.</summary>
    public ShowcaseGeometryPlanOptions GeometryOptions { get; set; } = new();
}
