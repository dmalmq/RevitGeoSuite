using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.DB;
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
