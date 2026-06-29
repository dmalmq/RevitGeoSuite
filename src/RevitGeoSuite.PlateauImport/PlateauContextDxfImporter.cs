using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Plateau.Tiles3D;

namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Lightweight PLATEAU import. Instead of building 3D DirectShape solids (see
/// <see cref="PlateauContextImporter"/>), it renders the same context outlines the DXF export
/// produces and brings them into Revit as a single 2D CAD import instance — a flat, layered
/// basemap for users who only want context, not full 3D massing.
/// <para>
/// The outline builder and DXF writer are reused from the export pipeline, but the geometry is
/// written in the model's <em>internal</em> coordinate frame (footprint feet → metres, no
/// shared/projected reprojection and no origin shift) so DXF (0,0,0) maps to the Revit internal
/// origin. Imported with <see cref="ImportPlacement.Origin"/> and <see cref="ImportUnit.Meter"/>,
/// the linework therefore lands exactly over the model — overlapping where the 3D import would
/// place the same footprints.
/// </para>
/// </summary>
public sealed class PlateauContextDxfImporter
{
    private const double FeetToMetres = 0.3048d;
    private const string RoadsLayer = "PLATEAU_ROADS";

    /// <summary>Outcome of writing the model-frame DXF (feature counts + any warnings).</summary>
    public sealed class DxfBuildResult
    {
        public DxfBuildResult(int outlineCount, int areaFillCount, int lineCount, IReadOnlyList<string> warnings)
        {
            OutlineCount = outlineCount;
            AreaFillCount = areaFillCount;
            LineCount = lineCount;
            Warnings = warnings ?? Array.Empty<string>();
        }

        public int OutlineCount { get; }

        public int AreaFillCount { get; }

        public int LineCount { get; }

        public IReadOnlyList<string> Warnings { get; }

        public int FeatureCount => OutlineCount + AreaFillCount + LineCount;
    }

    /// <summary>
    /// Writes the model-frame DXF for <paramref name="plan"/> to <paramref name="dxfPath"/>. This is
    /// pure file IO (no Revit API), so callers run it on a background job thread; only
    /// <see cref="ImportDxf"/> needs the Revit API thread.
    /// </summary>
    public DxfBuildResult WriteModelDxf(ContextImportPlan plan, string dxfPath)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (string.IsNullOrWhiteSpace(dxfPath)) throw new ArgumentException("A DXF path is required.", nameof(dxfPath));

        string? directory = Path.GetDirectoryName(dxfPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> rawOutlines =
            PlateauContextExportPipeline.BuildOutlineFeatures(plan, ToModelMetres, acceptedLandUseClassNames: null);

        List<string> warnings = new List<string>();
        // Roads come in as overlapping per-segment polygons; dissolve them into clean filled areas,
        // exactly like the export does, then drop the raw road outlines from the line stream.
        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas =
            PlateauRoadOutlineCleaner.DissolveRoads(rawOutlines, warnings);
        PlateauContextOutlinesDxfWriter.OutlineFeature[] outlines = rawOutlines
            .Where(outline => !string.Equals(outline.Layer, RoadsLayer, StringComparison.Ordinal))
            .ToArray();

        if (outlines.Length == 0 && roadAreas.Count == 0)
        {
            warnings.Add("The selected PLATEAU tiles did not produce any 2D outlines to import.");
            return new DxfBuildResult(0, 0, 0, warnings);
        }

        return WriteOutlineDxf(outlines, roadAreas, Array.Empty<PlateauContextOutlinesDxfWriter.LineFeature>(), plan.ReferenceContext, dxfPath, warnings);
    }

    /// <summary>
    /// Writes prebuilt model-frame DXF features. Coordinates must already be in Revit internal metres;
    /// DXF (0,0,0) maps to Revit's internal origin on import.
    /// </summary>
    public DxfBuildResult WriteOutlineDxf(
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines,
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.AreaFeature> areas,
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.LineFeature> lines,
        PlateauImportReferenceContext referenceContext,
        string dxfPath,
        IReadOnlyCollection<string>? initialWarnings = null,
        Vector3d? markerOverride = null)
    {
        if (outlines is null) throw new ArgumentNullException(nameof(outlines));
        if (areas is null) throw new ArgumentNullException(nameof(areas));
        if (lines is null) throw new ArgumentNullException(nameof(lines));
        if (referenceContext is null) throw new ArgumentNullException(nameof(referenceContext));
        if (string.IsNullOrWhiteSpace(dxfPath)) throw new ArgumentException("A DXF path is required.", nameof(dxfPath));

        string? directory = Path.GetDirectoryName(dxfPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        List<string> warnings = new List<string>(initialWarnings ?? Array.Empty<string>());
        if (outlines.Count == 0 && areas.Count == 0 && lines.Count == 0)
        {
            warnings.Add("The selected PLATEAU data did not produce any 2D basemap geometry to import.");
            return new DxfBuildResult(0, 0, 0, warnings);
        }

        Vector3d marker = markerOverride ?? new Vector3d(
            referenceContext.AnchorXFeet * FeetToMetres,
            referenceContext.AnchorYFeet * FeetToMetres,
            referenceContext.AnchorZFeet * FeetToMetres);

        PlateauContextOutlinesDxfWriter.WriteResult writeResult;
        using (StreamWriter writer = new StreamWriter(dxfPath, append: false, encoding: Encoding.ASCII))
        {
            writeResult = PlateauContextOutlinesDxfWriter.Write(
                writer,
                outlines,
                areas,
                lines,
                marker,
                Vector3d.Zero,
                PlateauDxfRoadFillMode.R12SolidTriangles);
        }

        warnings.AddRange(writeResult.Warnings);
        int writtenLines = lines.Count(line => line.VerticesMetres.Count >= 2);
        int writtenOutlines = Math.Max(0, writeResult.PolylineCount - writtenLines);
        return new DxfBuildResult(writtenOutlines, writeResult.FillCount, writtenLines, warnings);
    }

    private static (double EastingMetres, double NorthingMetres) ToModelMetres(double xFeet, double yFeet)
    {
        return (xFeet * FeetToMetres, yFeet * FeetToMetres);
    }

    /// <summary>
    /// Imports a DXF written by <see cref="WriteModelDxf"/> into <paramref name="document"/> as a
    /// single 2D CAD import instance placed at the internal origin. Must run on the Revit API thread.
    /// Opens a transaction when the caller has not already opened one.
    /// </summary>
    public ElementId ImportDxf(Document document, string dxfPath)
    {
        return ImportDxf(document, dxfPath, preferredView: null);
    }

    /// <summary>
    /// Imports a DXF into a preferred model view when supplied, falling back to the active/first
    /// available model import view. Must run on the Revit API thread.
    /// </summary>
    public ElementId ImportDxf(Document document, string dxfPath, View? preferredView)
    {
        return ImportOrLinkDxf(document, dxfPath, preferredView, link: false);
    }

    /// <summary>
    /// Links a DXF into a preferred model view when supplied, falling back to the active/first
    /// available model import view. Must run on the Revit API thread.
    /// </summary>
    public ElementId LinkDxf(Document document, string dxfPath)
    {
        return LinkDxf(document, dxfPath, preferredView: null);
    }

    /// <summary>
    /// Links a DXF into a preferred model view when supplied, falling back to the active/first
    /// available model import view. Must run on the Revit API thread.
    /// </summary>
    public ElementId LinkDxf(Document document, string dxfPath, View? preferredView)
    {
        return ImportOrLinkDxf(document, dxfPath, preferredView, link: true);
    }

    /// <summary>
    /// Links a DXF with explicit placement control. Use <see cref="ImportPlacement.Shared"/> for
    /// CRS-aware linking (coordinates in project CRS metres).
    /// </summary>
    public ElementId LinkDxf(Document document, string dxfPath, View? preferredView, ImportPlacement placement)
    {
        return ImportOrLinkDxf(document, dxfPath, preferredView, link: true, placement);
    }

    private static ElementId ImportOrLinkDxf(Document document, string dxfPath, View? preferredView, bool link, ImportPlacement placement = ImportPlacement.Origin)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(dxfPath)) throw new ArgumentException("A DXF path is required.", nameof(dxfPath));
        if (!File.Exists(dxfPath)) throw new FileNotFoundException("The generated basemap DXF was not found.", dxfPath);

        if (document.IsFamilyDocument)
        {
            throw new InvalidOperationException("DXF basemap import/link is not supported in family documents.");
        }

        if (document.IsReadOnly)
        {
            throw new InvalidOperationException("This Revit document is read-only. DXF basemap import/link requires an editable project.");
        }

        View view = ResolveImportView(document, preferredView)
            ?? throw new InvalidOperationException(
                "DXF basemap import/link needs an open 3D or plan view in the project. Open one and try again.");

        DWGImportOptions options = new DWGImportOptions
        {
            Unit = ImportUnit.Meter,
            Placement = placement,
            ThisViewOnly = false,
            ColorMode = ImportColorMode.Preserved,
            OrientToView = false,
        };

        if (!document.IsModifiable)
        {
            using Transaction transaction = new Transaction(document, link ? "Link 2D Basemap DXF" : "Import 2D Basemap DXF");
            transaction.Start();
            try
            {
                ElementId importId = ImportOrLinkDxfCore(document, dxfPath, options, view, link);
                transaction.Commit();
                return importId;
            }
            catch
            {
                if (transaction.GetStatus() == TransactionStatus.Started)
                {
                    transaction.RollBack();
                }

                throw;
            }
        }

        return ImportOrLinkDxfCore(document, dxfPath, options, view, link);
    }

    private static ElementId ImportOrLinkDxfCore(Document document, string dxfPath, DWGImportOptions options, View view, bool link)
    {
        if (link)
        {
            return LinkDxfCore(document, dxfPath, options, view);
        }

        if (!document.Import(dxfPath, options, view, out ElementId importId) || importId == ElementId.InvalidElementId)
        {
            throw new InvalidOperationException($"Revit could not import the generated basemap DXF. The generated file was kept at: {dxfPath}");
        }

        return importId;
    }

    private static ElementId LinkDxfCore(Document document, string dxfPath, DWGImportOptions options, View view)
    {
        ImportInstance importInstance = ImportInstance.Create(
            document,
            view,
            dxfPath,
            options,
            out LinkLoadResult linkLoadResult);

        LinkLoadResultType loadResult = linkLoadResult is null
            ? LinkLoadResultType.Uninitialized
            : linkLoadResult.LoadResult;

        if (importInstance is null
            || importInstance.Id == ElementId.InvalidElementId
            || !IsSuccessfulLinkResult(loadResult))
        {
            throw new InvalidOperationException(
                $"Revit could not link the generated basemap DXF ({FormatLinkLoadResult(linkLoadResult)}). The generated file was kept at: {dxfPath}");
        }

        return importInstance.Id;
    }

    private static bool IsSuccessfulLinkResult(LinkLoadResultType loadResult)
    {
        return LinkLoadResult.IsCodeSuccess(loadResult)
            || loadResult == LinkLoadResultType.UsedExisting;
    }

    private static string FormatLinkLoadResult(LinkLoadResult? linkLoadResult)
    {
        if (linkLoadResult is null)
        {
            return "load result: unavailable";
        }

        string linkTypeId = linkLoadResult.ElementId == ElementId.InvalidElementId
            ? "invalid"
            : linkLoadResult.ElementId.Value.ToString();

        return $"load result: {linkLoadResult.LoadResult}; link type id: {linkTypeId}";
    }

    private static View? ResolveImportView(Document document, View? preferredView)
    {
        if (preferredView is not null && IsModelImportView(preferredView))
        {
            return preferredView;
        }

        if (document.ActiveView is View active && IsModelImportView(active))
        {
            return active;
        }

        ViewPlan? plan = new FilteredElementCollector(document)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .FirstOrDefault(view => !view.IsTemplate);
        if (plan is not null)
        {
            return plan;
        }

        return new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(view => !view.IsTemplate);
    }

    private static bool IsModelImportView(View view)
    {
        if (view.IsTemplate)
        {
            return false;
        }

        switch (view.ViewType)
        {
            case ViewType.ThreeD:
            case ViewType.FloorPlan:
            case ViewType.CeilingPlan:
            case ViewType.EngineeringPlan:
            case ViewType.AreaPlan:
            case ViewType.Section:
            case ViewType.Elevation:
            case ViewType.Detail:
                return true;
            default:
                return false;
        }
    }
}
