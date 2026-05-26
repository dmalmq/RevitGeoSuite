using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Microsoft.Win32;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using RevitGeoSuite.SharedUI.Controls;

namespace RevitGeoSuite.PlateauImport.Online;

public partial class PlateauOnlineImportWindow : Window
{
    private readonly PlateauOnlineImportViewModel viewModel;
    private readonly Func<Action<PlateauOnlineImportContext>, PlateauTilesImporterResult> withTransaction;
    private readonly Func<EcefToProjectTransformer> ecefTransformerFactory;
    private readonly Func<IDracoMeshDecoder> dracoDecoderFactory;
    private readonly Vector3d projectBasePointMeters;
    private readonly Vector3d surveyPointMeters;
    private CancellationTokenSource? cancellation;
    private bool fitAreaOverlayToBounds;

    public PlateauOnlineImportWindow(
        PlateauOnlineImportViewModel viewModel,
        Func<EcefToProjectTransformer> ecefTransformerFactory,
        Func<IDracoMeshDecoder> dracoDecoderFactory,
        Func<Action<PlateauOnlineImportContext>, PlateauTilesImporterResult> withTransaction,
        Vector3d projectBasePointMeters,
        Vector3d surveyPointMeters)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.ecefTransformerFactory = ecefTransformerFactory ?? throw new ArgumentNullException(nameof(ecefTransformerFactory));
        this.dracoDecoderFactory = dracoDecoderFactory ?? throw new ArgumentNullException(nameof(dracoDecoderFactory));
        this.withTransaction = withTransaction ?? throw new ArgumentNullException(nameof(withTransaction));
        this.projectBasePointMeters = projectBasePointMeters;
        this.surveyPointMeters = surveyPointMeters;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnWindowLoaded;
        Closed += OnWindowClosed;
        AreaMap.MapPointSelected += OnMapPointSelected;
        AreaMap.OverlayFeatureClicked += OnAreaOverlayFeatureClicked;
    }

    public void SetOwner(IntPtr ownerHandle) => new WindowInteropHelper(this).Owner = ownerHandle;

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        await viewModel.LoadCatalogAsync();
        await AreaMap.SetViewAsync(35.6895, 139.6917, 12);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        AreaMap.MapPointSelected -= OnMapPointSelected;
        AreaMap.OverlayFeatureClicked -= OnAreaOverlayFeatureClicked;
        viewModel.CancelAreaPolygonLoading();
        cancellation?.Cancel();
    }

    private async void OnMapPointSelected(object? sender, MapPointSelectedEventArgs e)
    {
        await viewModel.TryAutoDetectAreaAsync(e.Latitude, e.Longitude);
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            await Dispatcher.InvokeAsync(() => OnViewModelPropertyChanged(sender, e));
            return;
        }

        if (string.Equals(e.PropertyName, nameof(PlateauOnlineImportViewModel.SelectedPrefecture), StringComparison.Ordinal))
        {
            fitAreaOverlayToBounds = true;
            return;
        }

        if (string.Equals(e.PropertyName, nameof(PlateauOnlineImportViewModel.AreasGeoJson), StringComparison.Ordinal))
        {
            await RefreshAreaOverlayAsync();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(PlateauOnlineImportViewModel.PendingMapFocus), StringComparison.Ordinal))
        {
            MapFocus? focus = viewModel.PendingMapFocus;
            if (focus is not null)
            {
                await AreaMap.SetViewAsync(focus.Latitude, focus.Longitude, focus.Zoom);
            }
        }
    }

    private async Task RefreshAreaOverlayAsync()
    {
        string? geoJson = viewModel.AreasGeoJson;
        if (string.IsNullOrWhiteSpace(geoJson))
        {
            await AreaMap.ClearFeatureSelectionOverlayAsync();
            return;
        }

        bool fitToBounds = fitAreaOverlayToBounds;
        fitAreaOverlayToBounds = false;
        await AreaMap.ShowFeatureSelectionOverlayAsync(
            geoJson!,
            fitToBounds,
            statusText: "PLATEAU coverage loaded. Click a municipality to select it.");
    }

    private void OnAreaOverlayFeatureClicked(object? sender, MapOverlayFeatureClickedEventArgs e)
    {
        viewModel.SelectArea(e.FeatureId);
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (viewModel is null) return;
        if (ModeFootprintDxfRadio.IsChecked == true) viewModel.GeometryMode = PlateauOnlineGeometryMode.OutlinesDxfExport;
        else viewModel.GeometryMode = PlateauOnlineGeometryMode.Lod2Untextured;
        ImportButton.Content = viewModel.GeometryMode == PlateauOnlineGeometryMode.OutlinesDxfExport
            ? "Download & Export DXF"
            : "Download & Import";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        cancellation?.Cancel();
        Close();
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedArea is null)
        {
            MessageBox.Show(this, "Pick a municipality first.", "PLATEAU Online Import", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        cancellation = new CancellationTokenSource();
        ImportButton.IsEnabled = false;
        try
        {
            EcefToProjectTransformer transformer = ecefTransformerFactory();
            IDracoMeshDecoder dracoDecoder = dracoDecoderFactory();
            PlateauTilesetModel? buildings = await viewModel.DownloadAsync(transformer, dracoDecoder, progress: null, cancellation.Token);
            if (buildings is null) return;

            if (viewModel.GeometryMode == PlateauOnlineGeometryMode.OutlinesDxfExport)
            {
                ExportFootprintDxf(buildings);
                return;
            }

            PlateauTilesImporterResult result = withTransaction(ctx =>
            {
                ctx.Buildings = buildings;
                ctx.Mode = viewModel.GeometryMode;
            });

            MessageBox.Show(this,
                $"Imported {result.ImportedElementCount} elements in {result.CreatedGroupCount} group(s). " +
                (result.Warnings.Count > 0 ? $"{result.Warnings.Count} warning(s) — see the panel below." : string.Empty),
                "PLATEAU Online Import",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            foreach (string w in result.Warnings) viewModel.Warnings.Add(w);
        }
        catch (OperationCanceledException)
        {
        }
        catch (DracoDecoderUnavailableException ex)
        {
            MessageBox.Show(this, ex.Message, "Draco decoder missing", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            viewModel.Warnings.Add(ex.Message);
            MessageBox.Show(this, ex.Message, "PLATEAU Online Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ImportButton.IsEnabled = true;
        }
    }

    private void ExportFootprintDxf(Core.Plateau.Tiles3D.PlateauTilesetModel buildings)
    {
        string defaultName = $"PLATEAU_{buildings.AreaCode ?? "area"}_outlines.dxf";
        SaveFileDialog dialog = new SaveFileDialog
        {
            FileName = defaultName,
            Filter = "AutoCAD DXF (*.dxf)|*.dxf|All files (*.*)|*.*",
            DefaultExt = ".dxf",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        PlateauFootprintDxfWriter.WriteResult writeResult;
        using (StreamWriter writer = new StreamWriter(dialog.FileName))
        {
            writeResult = PlateauFootprintDxfWriter.Write(writer, buildings, projectBasePointMeters, surveyPointMeters);
        }

        foreach (string w in writeResult.Warnings) viewModel.Warnings.Add(w);
        MessageBox.Show(this,
            $"Exported {writeResult.PolylineCount} building outline(s) to:\n{dialog.FileName}",
            "PLATEAU Online Import",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}

public sealed class PlateauOnlineImportContext
{
    public Core.Plateau.Tiles3D.PlateauTilesetModel? Buildings { get; set; }
    public PlateauOnlineGeometryMode Mode { get; set; }
}
