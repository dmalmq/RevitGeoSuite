using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.SharedUI.Controls;

namespace RevitGeoSuite.PlateauImport;

public partial class PlateauImportWindow : Window
{
    private readonly PlateauImportCoordinator importCoordinator;
    private readonly Document? document;
    private readonly IDocumentHandle? documentHandle;
    private readonly RevitModelFootprintOverlayService modelFootprintOverlayService;
    private string? modelOverlayCacheKey;
    private ModelFootprintOverlayResult? modelOverlayCacheResult;

    public PlateauImportWindow(
        PlateauImportViewModel viewModel,
        Document? document,
        IDocumentHandle? documentHandle,
        PlateauImportCoordinator importCoordinator,
        RevitModelFootprintOverlayService modelFootprintOverlayService)
    {
        InitializeComponent();
        ViewModel = viewModel;
        this.document = document;
        this.documentHandle = documentHandle;
        this.importCoordinator = importCoordinator;
        this.modelFootprintOverlayService = modelFootprintOverlayService;
        DataContext = viewModel;
        Loaded += OnWindowLoaded;
        Closed += OnWindowClosed;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        TilePreviewMap.OverlayFeatureClicked += OnTilePreviewOverlayFeatureClicked;
        TilePreviewMap.OverlayFeaturesRectangleSelected += OnTilePreviewOverlayFeaturesRectangleSelected;
        ModuleNavRail.ModuleRequested += OnModuleRequested;
    }

    public PlateauImportViewModel ViewModel { get; }

    public string? PendingModuleNavigationKey { get; private set; }

    public void SetOwner(IntPtr ownerHandle)
    {
        new WindowInteropHelper(this).Owner = ownerHandle;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshTilePreviewAsync(true, true);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        TilePreviewMap.OverlayFeatureClicked -= OnTilePreviewOverlayFeatureClicked;
        TilePreviewMap.OverlayFeaturesRectangleSelected -= OnTilePreviewOverlayFeaturesRectangleSelected;
        ModuleNavRail.ModuleRequested -= OnModuleRequested;
    }

    private void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        using Forms.FolderBrowserDialog dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select PLATEAU Import Folder",
            ShowNewFolderButton = false,
            SelectedPath = ViewModel.SelectedFolderPath
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            ViewModel.SelectedFolderPath = dialog.SelectedPath;
        }
    }

    private async void OnScanFolderClick(object sender, RoutedEventArgs e)
    {
        FolderPathTextBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        ViewModel.SelectedFolderPath = FolderPathTextBox.Text;
        if (!ViewModel.TryStartFolderScan(out string folderPath))
        {
            return;
        }

        try
        {
            await Task.Yield();
            PlateauFolderScanResult result = await Task.Run(() => ViewModel.ScanFolder(
                folderPath,
                progress => Dispatcher.Invoke(() => ViewModel.ReportScanProgress(progress))));
            bool success = ViewModel.ApplyScanResult(result);
            if (success)
            {
                await RefreshTilePreviewAsync(true, false);
            }
        }
        catch (Exception ex)
        {
            ViewModel.HandleScanFailure(ex);
        }
        finally
        {
            ViewModel.FinishFolderScan();
        }
    }

    private void OnBrowseKibanFolderClick(object sender, RoutedEventArgs e)
    {
        using Forms.FolderBrowserDialog dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select GSI Kiban Folder",
            ShowNewFolderButton = false,
            SelectedPath = ViewModel.KibanFolderPath
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            ViewModel.KibanFolderPath = dialog.SelectedPath;
        }
    }

    private async void OnScanKibanFolderClick(object sender, RoutedEventArgs e)
    {
        KibanFolderPathTextBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        ViewModel.KibanFolderPath = KibanFolderPathTextBox.Text;
        if (!ViewModel.TryStartKibanFolderScan(out PlateauImportViewModel.KibanScanRequest? request) || request is null)
        {
            return;
        }

        try
        {
            await Task.Yield();
            PlateauImportViewModel.KibanScanResult result = await Task.Run(() => ViewModel.ScanKibanFolder(request));
            ViewModel.ApplyKibanScanResult(result);
        }
        catch (Exception ex)
        {
            ViewModel.HandleKibanScanFailure(ex);
        }
        finally
        {
            ViewModel.FinishKibanFolderScan();
        }
    }

    private async void OnLoadPreviewClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryStartPreviewLoad(out PlateauImportViewModel.PreviewBuildRequest? request) || request is null)
        {
            return;
        }

        try
        {
            await Task.Yield();
            PlateauImportViewModel.PreviewBuildResult result = await Task.Run(() => ViewModel.BuildPreviewResult(request));
            ViewModel.ApplyPreviewResult(result);
        }
        catch (Exception ex)
        {
            ViewModel.HandlePreviewFailure(ex);
        }
        finally
        {
            ViewModel.FinishPreviewLoad();
        }
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (documentHandle is null || ViewModel.PreparedPlan is null || !ViewModel.CanImport)
        {
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            BuildImportConfirmationMessage(),
            "Confirm PLATEAU Context Import",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);

        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            PlateauImportResult result = importCoordinator.Import(
                documentHandle,
                ViewModel.PreparedPlan,
                ViewModel.SelectedReferenceSource,
                ViewModel.ImportState);
            ViewModel.MarkImportSucceeded(result);
            string details = result.WarningMessages.Count == 0
                ? "The import state was saved in module storage separately from GeoProjectInfo."
                : $"The import state was saved in module storage separately from GeoProjectInfo.\n\nWarnings recorded: {result.WarningMessages.Count}";
            MessageBox.Show(
                this,
                result.SummaryMessage + "\n\n" + details,
                "Import Succeeded",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnExportShapefileClick(object sender, RoutedEventArgs e)
    {
        bool isExportMode = ViewModel.IsExportMode;
        if (isExportMode ? !ViewModel.CanExportSelected : !ViewModel.CanExportOutlines)
        {
            return;
        }

        bool wantShapefile = !isExportMode || ViewModel.ExportFormatShapefile;
        bool wantDxf = isExportMode && ViewModel.ExportFormatDxf;
        bool includePlateau = !isExportMode || ViewModel.ExportIncludePlateauContext;
        bool includeKiban = !isExportMode || ViewModel.ExportIncludeKibanData;
        bool includeRevitModel = isExportMode && ViewModel.ExportIncludeRevitModel;

        string defaultName = BuildDefaultShapefileFileName();
        SaveFileDialog dialog = new SaveFileDialog
        {
            FileName = defaultName,
            Filter = "Output base name (*.shp/*.dxf)|*.shp;*.dxf|All files (*.*)|*.*",
            DefaultExt = ".shp",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!ViewModel.TryStartShapefileExport(out PlateauImportViewModel.ShapefileExportRequest? request) || request is null)
        {
            return;
        }

        IReadOnlyList<RevitModelFootprintFeature> revitFootprints = Array.Empty<RevitModelFootprintFeature>();
        List<string> revitWarnings = new List<string>();
        if (includeRevitModel)
        {
            ViewModel.ReportExportProgress(new PlateauImportViewModel.PlateauExportProgress("Extracting Revit footprints"));
            try
            {
                revitFootprints = new RevitModelFootprintExportService()
                    .ExtractFootprints(document, ViewModel.CurrentReferenceContext, revitWarnings);
            }
            catch (Exception ex)
            {
                revitWarnings.Add($"Revit footprint extraction failed: {ex.Message}");
            }
        }

        ExportRunResult result;
        try
        {
            await Task.Yield();
            string baseFileName = dialog.FileName;
            IReadOnlyList<RevitModelFootprintFeature> revitFeaturesForRun = revitFootprints;
            result = await Task.Run(() => RunExport(
                baseFileName,
                request,
                wantShapefile,
                wantDxf,
                includePlateau,
                includeKiban,
                includeRevitModel,
                revitFeaturesForRun,
                progress => Dispatcher.Invoke(() => ViewModel.ReportExportProgress(progress))));
        }
        catch (Exception ex)
        {
            ViewModel.HandleShapefileExportFailure(ex);
            MessageBox.Show(this, ex.Message, "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            ViewModel.FinishShapefileExport();
        }

        if (result.IsEmpty)
        {
            ViewModel.MarkShapefileExportEmpty();
            MessageBox.Show(
                this,
                BuildEmptyExportOutcomeMessage(result.KibanFolderInvolved),
                "Nothing to Export",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        int warningCount = (result.ShapefileResult?.Warnings.Count ?? 0)
            + (result.DxfResult?.Warnings.Count ?? 0)
            + result.OutlineWarnings.Count
            + revitWarnings.Count;
        if (result.ShapefileResult is not null)
        {
            ViewModel.MarkShapefileExportSucceeded(result.ShapefileResult, warningCount, result.KibanFolderInvolved);
        }

        MessageBox.Show(
            this,
            BuildExportSummaryMessage(dialog.FileName, result, warningCount),
            "Export Complete",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private ExportRunResult RunExport(
        string baseFileName,
        PlateauImportViewModel.ShapefileExportRequest request,
        bool wantShapefile,
        bool wantDxf,
        bool includePlateauContext,
        bool includeKibanData,
        bool includeRevitModel,
        IReadOnlyList<RevitModelFootprintFeature> revitFootprints,
        Action<PlateauImportViewModel.PlateauExportProgress> progress)
    {
        if (wantShapefile && !wantDxf)
        {
            PlateauContextShapefileWriter.WriteResult? streamingShapefileResult = null;
            try
            {
                streamingShapefileResult = ViewModel.WriteShapefilesStreaming(
                    baseFileName,
                    request,
                    includePlateauContext,
                    includeKibanData,
                    includeRevitModel,
                    includeRevitModel ? revitFootprints : Array.Empty<RevitModelFootprintFeature>(),
                    progress);
            }
            catch (InvalidOperationException)
            {
                streamingShapefileResult = null;
            }

            return new ExportRunResult(
                Array.Empty<string>(),
                streamingShapefileResult,
                dxfResult: null,
                isEmpty: streamingShapefileResult is null || streamingShapefileResult.FeatureCount == 0,
                request.HasKibanFolder);
        }

        PlateauOutlineDxfExportPackage exportPackage = ViewModel.BuildOutlineDxfExportPackage(
            request,
            out IReadOnlyList<string> outlineWarnings,
            progress,
            includeRevitModel ? revitFootprints : Array.Empty<RevitModelFootprintFeature>());

        bool plateauHasGeometry = exportPackage.Features.Count > 0
            || exportPackage.RoadAreas.Count > 0
            || exportPackage.KibanFeatures.Count > 0;
        bool kibanHasGeometry = exportPackage.KibanLineFeatures.Count > 0
            || exportPackage.KibanPolygonFeatures.Count > 0;
        bool revitHasGeometry = exportPackage.RevitModelFeatures.Count > 0;

        bool plateauForExport = includePlateauContext && plateauHasGeometry;
        bool kibanForExport = includeKibanData && kibanHasGeometry;
        bool revitForExport = includeRevitModel && revitHasGeometry;

        if (!plateauForExport && !kibanForExport && !revitForExport)
        {
            return new ExportRunResult(outlineWarnings, shapefileResult: null, dxfResult: null, isEmpty: true, request.HasKibanFolder);
        }

        PlateauContextShapefileWriter.WriteResult? shapefileResult = null;
        if (wantShapefile)
        {
            PlateauOutlineDxfExportPackage shapefilePackage = BuildFilteredPackage(
                exportPackage,
                includePlateauContext: plateauForExport,
                includeKibanData: kibanForExport,
                includeRevitModel: revitForExport);
            try
            {
                shapefileResult = PlateauContextShapefileWriter.Write(
                    baseFileName,
                    shapefilePackage,
                    stage => progress(new PlateauImportViewModel.PlateauExportProgress(stage)));
            }
            catch (InvalidOperationException)
            {
                shapefileResult = null;
            }
        }

        PlateauContextDxfExportService.WriteResult? dxfResult = null;
        if (wantDxf)
        {
            string dxfPath = System.IO.Path.ChangeExtension(baseFileName, ".dxf");
            dxfResult = new PlateauContextDxfExportService().Write(
                dxfPath,
                exportPackage,
                includePlateauContext: plateauForExport,
                includeRevitModel: revitForExport,
                onStage: stage => progress(new PlateauImportViewModel.PlateauExportProgress(stage)));
        }

        bool emitted = (shapefileResult is not null && shapefileResult.FeatureCount > 0)
            || (dxfResult is not null && (dxfResult.PolylineCount > 0 || dxfResult.AreaFillCount > 0));
        return new ExportRunResult(
            outlineWarnings,
            shapefileResult,
            dxfResult,
            isEmpty: !emitted,
            request.HasKibanFolder);
    }

    private static PlateauOutlineDxfExportPackage BuildFilteredPackage(
        PlateauOutlineDxfExportPackage source,
        bool includePlateauContext,
        bool includeKibanData,
        bool includeRevitModel)
    {
        return new PlateauOutlineDxfExportPackage(
            includePlateauContext ? source.Features : Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
            includePlateauContext ? source.RoadAreas : Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
            includePlateauContext ? source.KibanFeatures : Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
            includeKibanData ? source.KibanLineFeatures : Array.Empty<KibanLineExportFeature>(),
            includeKibanData ? source.KibanPolygonFeatures : Array.Empty<KibanPolygonExportFeature>(),
            includeRevitModel ? source.RevitModelFeatures : Array.Empty<RevitModelFootprintFeature>(),
            source.ProjectCrs,
            source.ProjectBasePointMarkerMetres,
            source.OriginOffsetMetres);
    }

    private static string BuildExportSummaryMessage(
        string baseFileName,
        ExportRunResult result,
        int warningCount)
    {
        List<string> lines = new List<string>();
        if (result.ShapefileResult is not null && result.ShapefileResult.FeatureCount > 0)
        {
            int cityGmlCount = result.ShapefileResult.FeatureCount
                - result.ShapefileResult.LineFeatureCount
                - result.ShapefileResult.KibanWaterFeatureCount
                - result.ShapefileResult.KibanLandUseFeatureCount
                - result.ShapefileResult.RevitModelFeatureCount;
            lines.Add($"Shapefile: {cityGmlCount} PLATEAU polygon(s), {result.ShapefileResult.SidewalkFeatureCount} sidewalk, {result.ShapefileResult.RailwayFeatureCount} railway, {result.ShapefileResult.KibanWaterFeatureCount} water, {result.ShapefileResult.KibanLandUseFeatureCount} land-use, {result.ShapefileResult.RevitBuildingFeatureCount} Revit building, {result.ShapefileResult.RevitWallFeatureCount} Revit wall.");
        }
        if (result.DxfResult is not null && result.DxfResult.PolylineCount > 0)
        {
            lines.Add($"DXF: {result.DxfResult.PolylineCount} polyline(s), {result.DxfResult.AreaFillCount} area fill(s) — anchored at the Revit Survey Point.");
        }
        if (lines.Count == 0)
        {
            lines.Add("No features were written.");
        }

        lines.Add(string.Empty);
        lines.Add("Base path:");
        lines.Add(baseFileName);

        if (warningCount > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"{warningCount} warning(s) recorded during export.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string BuildDefaultShapefileFileName()
    {
        string folder = string.IsNullOrWhiteSpace(ViewModel.SelectedFolderPath)
            ? "context"
            : Path.GetFileName(ViewModel.SelectedFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(folder)
            ? "PLATEAU_context_polygons.shp"
            : $"PLATEAU_{folder}_polygons.shp";
    }

    private static string BuildEmptyExportOutcomeMessage(bool kibanFolderInvolved)
    {
        string kibanStatus = kibanFolderInvolved
            ? "GSI Kiban export: No Kiban geometry produced."
            : "GSI Kiban export: Not requested.";
        return "No shapefiles were created.\n\n"
            + "CityGML polygon export: No polygon geometry produced.\n"
            + kibanStatus;
    }


    private sealed class ExportRunResult
    {
        public ExportRunResult(
            IReadOnlyList<string> outlineWarnings,
            PlateauContextShapefileWriter.WriteResult? shapefileResult,
            PlateauContextDxfExportService.WriteResult? dxfResult,
            bool isEmpty,
            bool kibanFolderInvolved)
        {
            OutlineWarnings = outlineWarnings ?? Array.Empty<string>();
            ShapefileResult = shapefileResult;
            DxfResult = dxfResult;
            IsEmpty = isEmpty;
            KibanFolderInvolved = kibanFolderInvolved;
        }

        public IReadOnlyList<string> OutlineWarnings { get; }

        public PlateauContextShapefileWriter.WriteResult? ShapefileResult { get; }

        public PlateauContextDxfExportService.WriteResult? DxfResult { get; }

        public bool IsEmpty { get; }

        public bool KibanFolderInvolved { get; }
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(PlateauImportViewModel.TilePreviewGeoJson), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(PlateauImportViewModel.TilePreviewStatusText), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(PlateauImportViewModel.SelectedTileCount), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(PlateauImportViewModel.SelectedCategoryCount), StringComparison.Ordinal))
        {
            await RefreshTilePreviewAsync(false, false);
            return;
        }

        if (string.Equals(e.PropertyName, nameof(PlateauImportViewModel.TilePreviewReferenceLatitude), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(PlateauImportViewModel.TilePreviewReferenceLongitude), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(PlateauImportViewModel.TilePreviewReferenceTitle), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(PlateauImportViewModel.SelectedReferenceSourceOption), StringComparison.Ordinal))
        {
            await RefreshTilePreviewAsync(true, true);
            return;
        }

        if (string.Equals(e.PropertyName, nameof(PlateauImportViewModel.ShowModelOverlay), StringComparison.Ordinal))
        {
            await RefreshTilePreviewAsync(false, false);
        }
    }

    private async void OnTilePreviewOverlayFeatureClicked(object? sender, MapOverlayFeatureClickedEventArgs e)
    {
        if (ViewModel.ToggleTileSelection(e.FeatureId))
        {
            await RefreshTilePreviewAsync(false, false);
        }
    }

    private async void OnTilePreviewOverlayFeaturesRectangleSelected(object? sender, MapOverlayFeaturesRectangleSelectedEventArgs e)
    {
        if (ViewModel.SelectTilesByIds(e.FeatureIds) > 0)
        {
            await RefreshTilePreviewAsync(false, false);
        }
    }

    private async void OnClearTileSelectionClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ClearAllTileSelections() > 0)
        {
            await RefreshTilePreviewAsync(false, false);
        }
    }

    private void OnModuleRequested(object? sender, ModuleNavigationRequestedEventArgs e)
    {
        if (HasPendingNavigationChanges())
        {
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                $"Switch to '{e.ModuleTitle}'?\n\nCurrent PLATEAU scan, tile selections, and preview state will be discarded.",
                "Switch Module",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel);

            if (confirmation != MessageBoxResult.OK)
            {
                return;
            }
        }

        PendingModuleNavigationKey = e.ModuleKey;
        Close();
    }

    private bool HasPendingNavigationChanges()
    {
        return ViewModel.HasScanRows
            || ViewModel.HasPreviewRows
            || ViewModel.CanImport
            || ViewModel.SelectedTileCount > 0
            || ViewModel.SelectedCategoryCount > 0;
    }

    private async Task RefreshTilePreviewAsync(bool fitToBounds, bool rebuildModelOverlay)
    {
        TraceTilePreviewRefresh(fitToBounds, rebuildModelOverlay);
        await TilePreviewMap.SetPointSelectionEnabledAsync(false);
        await TilePreviewMap.ClearMeshGridAsync();

        if (ViewModel.TilePreviewReferenceLatitude.HasValue && ViewModel.TilePreviewReferenceLongitude.HasValue)
        {
            await TilePreviewMap.SetMarkerAsync(
                ViewModel.TilePreviewReferenceLatitude.Value,
                ViewModel.TilePreviewReferenceLongitude.Value,
                ViewModel.TilePreviewReferenceTitle);
        }
        else
        {
            await TilePreviewMap.ClearMarkerAsync();
        }

        bool hasModelOverlay = await RefreshModelOverlayAsync(fitToBounds, rebuildModelOverlay);

        if (ViewModel.HasTilePreview)
        {
            await TilePreviewMap.ShowFeatureSelectionOverlayAsync(
                ViewModel.TilePreviewGeoJson,
                fitToBounds,
                ViewModel.TilePreviewReferenceLatitude,
                ViewModel.TilePreviewReferenceLongitude,
                ViewModel.TilePreviewStatusText);
            return;
        }

        await TilePreviewMap.ClearFeatureSelectionOverlayAsync();
        if (fitToBounds && !hasModelOverlay && ViewModel.TilePreviewReferenceLatitude.HasValue && ViewModel.TilePreviewReferenceLongitude.HasValue)
        {
            await TilePreviewMap.SetViewAsync(ViewModel.TilePreviewReferenceLatitude.Value, ViewModel.TilePreviewReferenceLongitude.Value, 15);
        }
    }

    private async Task<bool> RefreshModelOverlayAsync(bool fitToBounds, bool rebuildModelOverlay)
    {
        if (!ViewModel.ShowModelOverlay)
        {
            ViewModel.SetModelOverlayStatus("Host model overlay hidden.");
            Trace.WriteLine("[PlateauImportWindow] Model overlay hidden before refresh.");
            await TilePreviewMap.ClearModelFootprintOverlayAsync();
            return false;
        }

        string cacheKey = BuildModelOverlayCacheKey();
        if (rebuildModelOverlay || modelOverlayCacheResult is null || !string.Equals(modelOverlayCacheKey, cacheKey, StringComparison.Ordinal))
        {
            modelOverlayCacheResult = modelFootprintOverlayService.Build(document, ViewModel.CurrentReferenceContext);
            modelOverlayCacheKey = cacheKey;
        }

        ModelFootprintOverlayResult result = modelOverlayCacheResult;
        ViewModel.SetModelOverlayStatus(result.StatusMessage);
        Trace.WriteLine(
            $"[PlateauImportWindow] Model overlay result. crs={FormatReferenceEpsg()} hasOverlay={result.HasOverlay} includedElements={result.IncludedElementCount} status='{result.StatusMessage}'");
        if (!result.HasOverlay)
        {
            await TilePreviewMap.ClearModelFootprintOverlayAsync();
            return false;
        }

        await TilePreviewMap.ShowModelFootprintOverlayAsync(
            result.GeoJson,
            fitToBounds && !ViewModel.HasTilePreview,
            ViewModel.TilePreviewReferenceLatitude,
            ViewModel.TilePreviewReferenceLongitude);
        return true;
    }

    private string BuildModelOverlayCacheKey()
    {
        PlateauImportReferenceContext? context = ViewModel.CurrentReferenceContext;
        if (context is null)
        {
            return "no-context";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}|{1}|{2:F3}|{3:F3}|{4:F3}|{5:F3}|{6:F3}",
            context.ProjectCrs.EpsgCode,
            context.Title,
            context.AnchorProjectedCoordinate.Easting,
            context.AnchorProjectedCoordinate.Northing,
            context.AnchorXFeet,
            context.AnchorYFeet,
            context.AnchorZFeet);
    }

    private void TraceTilePreviewRefresh(bool fitToBounds, bool rebuildModelOverlay)
    {
        string referenceTitle = ViewModel.CurrentReferenceContext?.Title ?? "Unavailable";
        Trace.WriteLine(
            $"[PlateauImportWindow] Refreshing tile preview. crs={FormatReferenceEpsg()} reference='{referenceTitle}' fitToBounds={fitToBounds} rebuildModelOverlay={rebuildModelOverlay} hasTilePreview={ViewModel.HasTilePreview} showModelOverlay={ViewModel.ShowModelOverlay}");
    }

    private string FormatReferenceEpsg()
    {
        return ViewModel.CurrentReferenceContext?.ProjectCrs is null
            ? "unavailable"
            : $"EPSG:{ViewModel.CurrentReferenceContext.ProjectCrs.EpsgCode}";
    }

    private string BuildImportConfirmationMessage()
    {
        string folderName = string.IsNullOrWhiteSpace(ViewModel.SelectedFolderPath)
            ? "selected folder"
            : Path.GetFileName(ViewModel.SelectedFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string mode = ViewModel.SelectedGeometryImportMode.GetDisplayName();
        string modeDetail = ViewModel.SelectedGeometryImportMode == PlateauGeometryImportMode.DetailedDirectShape
            ? "Detailed mode preserves the highest-LOD CityGML surfaces as faceted DirectShape geometry and may create heavier Revit context shapes.\n\n"
            : string.Empty;
        return $"Import {ViewModel.PreparedShapeCount} PLATEAU context shapes from '{folderName}' into '{ViewModel.DocumentTitle}' using {mode}?\n\n{modeDetail}This replaces prior PLATEAU imports for the same tile and category scopes, then groups the new imported elements by tile and category for easier selection afterward.";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
