using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.Plateau.Tiles;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.SharedUI.Localization;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportViewModel : INotifyPropertyChanged
{
    private static readonly string[] DefaultKibanLayers =
    {
        PlateauContextOutlinesDxfWriter.GsiSidewalksLayer,
        PlateauContextOutlinesDxfWriter.GsiRailwaysLayer,
        KibanGmlParser.WaterLayer,
        KibanGmlParser.LandUseLayer
    };

    private readonly CurrentProjectStateSummary currentState;
    private readonly GeoProjectInfo? info;
    private readonly PlateauImportReferenceResolver referenceResolver;
    private readonly PlateauTileIndex tileIndex;
    private readonly PlateauFolderScanService folderScanService;
    private readonly ContextGeometryBuilder geometryBuilder;
    private readonly PlateauTileOverlayService tileOverlayService;
    private PlateauImportState? importState;
    private PlateauImportReferenceContext? referenceContext;
    private PlateauFolderScanResult? scanResult;
    private ContextImportPlan? preparedPlan;
    private string actionMessage;
    private string tilePreviewGeoJson;
    private string selectedFolderPath;
    private string statusMessage;
    private string modelOverlayStatusMessage;
    private string scanProgressStatusText;
    private bool showModelOverlay;
    private bool isScanning;
    private bool isPreparingPreview;
    private bool isExportingShapefile;
    private int previewRequestVersion;
    private bool isScanProgressIndeterminate;
    private double scanProgressPercent;
    private int scanProgressCurrent;
    private int scanProgressTotal;
    private bool isExportProgressIndeterminate;
    private string exportProgressStatusText = string.Empty;
    private bool exportFormatShapefile = true;
    private bool exportFormatDxf = true;
    private bool exportIncludePlateauContext = true;
    private bool exportIncludeKibanData = true;
    private bool exportIncludeRevitModel = true;
    private PlateauImportReferenceSourceOption? selectedReferenceSourceOption;
    private PlateauGeometryImportModeOption? selectedGeometryImportModeOption;
    private string kibanFolderPath;
    private bool isScanningKiban;
    private IReadOnlyList<KibanParsedFeature>? kibanParsedFeatures;
    private IReadOnlyList<KibanParsedPolygonFeature>? kibanParsedPolygonFeatures;
    private readonly ICoordinateTransformer? kibanCoordinateTransformer;
    private readonly KibanGmlParser kibanGmlParser;

    public PlateauImportViewModel(
        CurrentProjectStateSummary currentState,
        GeoProjectInfo? info,
        PlateauImportState? importState,
        PlateauImportReferenceResolver referenceResolver,
        PlateauTileIndex tileIndex,
        PlateauFolderScanService folderScanService,
        ContextGeometryBuilder geometryBuilder,
        PlateauTileOverlayService? tileOverlayService = null,
        ICoordinateTransformer? kibanCoordinateTransformer = null,
        bool isExportMode = false)
    {
        this.currentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        this.info = info;
        this.importState = importState;
        this.referenceResolver = referenceResolver ?? throw new ArgumentNullException(nameof(referenceResolver));
        this.tileIndex = tileIndex ?? throw new ArgumentNullException(nameof(tileIndex));
        this.folderScanService = folderScanService ?? throw new ArgumentNullException(nameof(folderScanService));
        this.geometryBuilder = geometryBuilder ?? throw new ArgumentNullException(nameof(geometryBuilder));
        this.tileOverlayService = tileOverlayService ?? new PlateauTileOverlayService(new JapanMeshCalculator());
        this.kibanCoordinateTransformer = kibanCoordinateTransformer;
        IsExportMode = isExportMode;
        kibanGmlParser = new KibanGmlParser();
        kibanFolderPath = isExportMode ? PlateauScanSessionCache.LastKibanFolderPath : string.Empty;
        actionMessage = BuildInitialActionMessage(importState);
        tilePreviewGeoJson = string.Empty;
        selectedFolderPath = GetInitialFolderPath(importState);
        statusMessage = string.Empty;
        modelOverlayStatusMessage = "Resolve the import reference to show the host-model overlay.";
        scanProgressStatusText = string.Empty;
        showModelOverlay = true;
        CurrentStateRows = new ObservableCollection<DetailRow>();
        LastImportRows = new ObservableCollection<DetailRow>();
        ScanRows = new ObservableCollection<DetailRow>();
        PreviewRows = new ObservableCollection<DetailRow>();
        SuggestedTiles = new ObservableCollection<PlateauTileCandidate>();
        DetectedSourceFiles = new ObservableCollection<string>();
        FeatureNames = new ObservableCollection<string>();
        WarningMessages = new ObservableCollection<string>();
        FeatureTypeOptions = new ObservableCollection<PlateauFeatureSelectionItem>();
        TileOptions = new ObservableCollection<PlateauTileSelectionItem>();
        KibanFeatureOptions = new ObservableCollection<KibanFeatureSelectionItem>();
        OptionalKibanLandUseOptions = new ObservableCollection<KibanOptionalLandUseSelectionItem>(CreateOptionalKibanLandUseOptions());
        foreach (KibanOptionalLandUseSelectionItem option in OptionalKibanLandUseOptions)
        {
            option.PropertyChanged += OnOptionalKibanLandUseChanged;
        }
        LandUseClassOptions = new ObservableCollection<PlateauLandUseClassSelectionItem>();
        ReferenceSourceOptions = new ObservableCollection<PlateauImportReferenceSourceOption>(CreateReferenceSourceOptions());
        GeometryImportModeOptions = new ObservableCollection<PlateauGeometryImportModeOption>(CreateGeometryImportModeOptions());
        BuildLastImportRows();

        PlateauImportReferenceSource defaultSource = GetDefaultReferenceSource(currentState, importState);
        PlateauGeometryImportMode defaultGeometryImportMode = GetDefaultGeometryImportMode(importState);
        selectedReferenceSourceOption = ReferenceSourceOptions.First(option => option.Source == defaultSource);
        selectedGeometryImportModeOption = GeometryImportModeOptions.First(option => option.Mode == defaultGeometryImportMode);
        RefreshReferenceContext(clearPreview: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DetailRow> CurrentStateRows { get; }

    public ObservableCollection<DetailRow> LastImportRows { get; }

    public ObservableCollection<DetailRow> ScanRows { get; }

    public ObservableCollection<DetailRow> PreviewRows { get; }

    public ObservableCollection<PlateauTileCandidate> SuggestedTiles { get; }

    public ObservableCollection<string> DetectedSourceFiles { get; }

    public ObservableCollection<string> FeatureNames { get; }

    public ObservableCollection<string> WarningMessages { get; }

    public ObservableCollection<PlateauFeatureSelectionItem> FeatureTypeOptions { get; }

    public ObservableCollection<PlateauTileSelectionItem> TileOptions { get; }

    public ObservableCollection<KibanFeatureSelectionItem> KibanFeatureOptions { get; }

    public ObservableCollection<KibanOptionalLandUseSelectionItem> OptionalKibanLandUseOptions { get; }

    public ObservableCollection<PlateauLandUseClassSelectionItem> LandUseClassOptions { get; }

    public bool HasLandUseClassOptions => LandUseClassOptions.Count > 0;

    public string KibanFolderPath
    {
        get => kibanFolderPath;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(kibanFolderPath, normalized, StringComparison.Ordinal))
            {
                return;
            }

            kibanFolderPath = normalized;
            PlateauScanSessionCache.RememberKibanFolderPath(normalized);
            ClearKibanScan();
            RaisePropertyChanged(nameof(KibanFolderPath));
            RaisePropertyChanged(nameof(CanScanKibanFolder));
        }
    }

    public bool IsScanningKiban
    {
        get => isScanningKiban;
        private set
        {
            if (isScanningKiban == value) return;
            isScanningKiban = value;
            RaisePropertyChanged(nameof(IsScanningKiban));
            RaisePropertyChanged(nameof(CanScanKibanFolder));
            RaisePropertyChanged(nameof(CanExportOutlines));
        }
    }

    public bool CanScanKibanFolder => !string.IsNullOrWhiteSpace(KibanFolderPath) && !IsScanningKiban && !IsExportingShapefile;

    public bool HasKibanFeatureOptions => KibanFeatureOptions.Count > 0;

    public bool HasNoKibanFeatureOptions => !HasKibanFeatureOptions;

    public ObservableCollection<PlateauImportReferenceSourceOption> ReferenceSourceOptions { get; }

    public ObservableCollection<PlateauGeometryImportModeOption> GeometryImportModeOptions { get; }

    public string WindowTitle => IsExportMode ? "PLATEAU Context Export" : "PLATEAU Context Import";

    public string DocumentTitle => string.IsNullOrWhiteSpace(currentState.DocumentTitle) ? "Active Revit Project" : currentState.DocumentTitle;

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (string.Equals(statusMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            statusMessage = value ?? string.Empty;
            RaisePropertyChanged(nameof(StatusMessage));
            RaisePropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string ActionMessage
    {
        get => actionMessage;
        private set
        {
            if (string.Equals(actionMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            actionMessage = value ?? string.Empty;
            RaisePropertyChanged(nameof(ActionMessage));
            RaisePropertyChanged(nameof(HasActionMessage));
        }
    }

    public bool HasActionMessage => !string.IsNullOrWhiteSpace(ActionMessage);

    public string SelectedFolderPath
    {
        get => selectedFolderPath;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(selectedFolderPath, normalized, StringComparison.Ordinal))
            {
                return;
            }

            selectedFolderPath = normalized;
            ClearScanAndPreview();
            RaisePropertyChanged(nameof(SelectedFolderPath));
            RaisePropertyChanged(nameof(CanScanFolder));
        }
    }

    public bool CanScanFolder => !string.IsNullOrWhiteSpace(SelectedFolderPath) && !IsScanning && !IsPreparingPreview && !IsExportingShapefile;

    public bool IsScanning
    {
        get => isScanning;
        set
        {
            if (isScanning == value) return;
            isScanning = value;
            RaisePropertyChanged(nameof(IsScanning));
            RaisePropertyChanged(nameof(CanScanFolder));
            RaisePropertyChanged(nameof(CanLoadPreview));
            RaisePropertyChanged(nameof(CanExportOutlines));
            RaisePropertyChanged(nameof(CanImport));
        }
    }

    public bool IsPreparingPreview
    {
        get => isPreparingPreview;
        private set
        {
            if (isPreparingPreview == value)
            {
                return;
            }

            isPreparingPreview = value;
            RaisePropertyChanged(nameof(IsPreparingPreview));
            RaisePropertyChanged(nameof(CanScanFolder));
            RaisePropertyChanged(nameof(CanLoadPreview));
            RaisePropertyChanged(nameof(CanExportOutlines));
            RaisePropertyChanged(nameof(CanImport));
        }
    }

    public bool IsExportingShapefile
    {
        get => isExportingShapefile;
        private set
        {
            if (isExportingShapefile == value)
            {
                return;
            }

            isExportingShapefile = value;
            RaisePropertyChanged(nameof(IsExportingShapefile));
            RaisePropertyChanged(nameof(CanScanFolder));
            RaisePropertyChanged(nameof(CanScanKibanFolder));
            RaisePropertyChanged(nameof(CanLoadPreview));
            RaisePropertyChanged(nameof(CanExportOutlines));
            RaisePropertyChanged(nameof(CanImport));
        }
    }

    public bool CanLoadPreview => !IsScanning
        && !IsPreparingPreview
        && !IsExportingShapefile
        && referenceContext is not null
        && scanResult is not null
        && FeatureTypeOptions.Any(option => option.IsSelected)
        && TileOptions.Any(option => option.IsSelected);

    public bool CanExportOutlines => !IsScanningKiban && !IsExportingShapefile && CanLoadPreview;

    public bool IsExportMode { get; }

    public bool ShowImportAction => !IsExportMode;

    public bool ShowExportSurface => IsExportMode;

    public bool ExportFormatShapefile
    {
        get => exportFormatShapefile;
        set
        {
            if (exportFormatShapefile == value)
            {
                return;
            }

            exportFormatShapefile = value;
            RaisePropertyChanged(nameof(ExportFormatShapefile));
            RaisePropertyChanged(nameof(CanExportSelected));
        }
    }

    public bool ExportFormatDxf
    {
        get => exportFormatDxf;
        set
        {
            if (exportFormatDxf == value)
            {
                return;
            }

            exportFormatDxf = value;
            RaisePropertyChanged(nameof(ExportFormatDxf));
            RaisePropertyChanged(nameof(CanExportSelected));
        }
    }

    public bool ExportIncludePlateauContext
    {
        get => exportIncludePlateauContext;
        set
        {
            if (exportIncludePlateauContext == value)
            {
                return;
            }

            exportIncludePlateauContext = value;
            RaisePropertyChanged(nameof(ExportIncludePlateauContext));
            RaisePropertyChanged(nameof(CanExportSelected));
        }
    }

    public bool ExportIncludeKibanData
    {
        get => exportIncludeKibanData;
        set
        {
            if (exportIncludeKibanData == value)
            {
                return;
            }

            exportIncludeKibanData = value;
            RaisePropertyChanged(nameof(ExportIncludeKibanData));
            RaisePropertyChanged(nameof(CanExportSelected));
        }
    }

    public bool ExportIncludeRevitModel
    {
        get => exportIncludeRevitModel;
        set
        {
            if (exportIncludeRevitModel == value)
            {
                return;
            }

            exportIncludeRevitModel = value;
            RaisePropertyChanged(nameof(ExportIncludeRevitModel));
            RaisePropertyChanged(nameof(CanExportSelected));
        }
    }

    public bool CanExportSelected => IsExportMode
        && CanExportOutlines
        && (ExportFormatShapefile || ExportFormatDxf)
        && (ExportIncludePlateauContext || ExportIncludeKibanData || ExportIncludeRevitModel);

    public bool CanImport => !IsScanning
        && !IsPreparingPreview
        && !IsExportingShapefile
        && preparedPlan is not null
        && currentState.IsSupportedDocument
        && !currentState.IsReadOnly;

    public double ScanProgressPercent
    {
        get => scanProgressPercent;
        private set
        {
            if (Math.Abs(scanProgressPercent - value) < 0.001d)
            {
                return;
            }

            scanProgressPercent = value;
            RaisePropertyChanged(nameof(ScanProgressPercent));
        }
    }

    public int ScanProgressCurrent
    {
        get => scanProgressCurrent;
        private set
        {
            if (scanProgressCurrent == value)
            {
                return;
            }

            scanProgressCurrent = value;
            RaisePropertyChanged(nameof(ScanProgressCurrent));
        }
    }

    public int ScanProgressTotal
    {
        get => scanProgressTotal;
        private set
        {
            if (scanProgressTotal == value)
            {
                return;
            }

            scanProgressTotal = value;
            RaisePropertyChanged(nameof(ScanProgressTotal));
        }
    }

    public bool IsScanProgressIndeterminate
    {
        get => isScanProgressIndeterminate;
        private set
        {
            if (isScanProgressIndeterminate == value)
            {
                return;
            }

            isScanProgressIndeterminate = value;
            RaisePropertyChanged(nameof(IsScanProgressIndeterminate));
            RaisePropertyChanged(nameof(HasDeterminateScanProgress));
        }
    }

    public bool HasDeterminateScanProgress => !IsScanProgressIndeterminate && ScanProgressTotal > 0;

    public string ScanProgressStatusText
    {
        get => scanProgressStatusText;
        private set
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(scanProgressStatusText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            scanProgressStatusText = normalized;
            RaisePropertyChanged(nameof(ScanProgressStatusText));
            RaisePropertyChanged(nameof(HasScanProgressStatusText));
        }
    }

    public bool HasScanProgressStatusText => !string.IsNullOrWhiteSpace(ScanProgressStatusText);

    public bool IsExportProgressIndeterminate
    {
        get => isExportProgressIndeterminate;
        private set
        {
            if (isExportProgressIndeterminate == value)
            {
                return;
            }

            isExportProgressIndeterminate = value;
            RaisePropertyChanged(nameof(IsExportProgressIndeterminate));
        }
    }

    public string ExportProgressStatusText
    {
        get => exportProgressStatusText;
        private set
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(exportProgressStatusText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            exportProgressStatusText = normalized;
            RaisePropertyChanged(nameof(ExportProgressStatusText));
            RaisePropertyChanged(nameof(HasExportProgressStatusText));
        }
    }

    public bool HasExportProgressStatusText => !string.IsNullOrWhiteSpace(ExportProgressStatusText);

    public string TilePreviewGeoJson
    {
        get => tilePreviewGeoJson;
        private set
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(tilePreviewGeoJson, normalized, StringComparison.Ordinal))
            {
                return;
            }

            tilePreviewGeoJson = normalized;
            RaisePropertyChanged(nameof(TilePreviewGeoJson));
            RaisePropertyChanged(nameof(HasTilePreview));
            RaisePropertyChanged(nameof(HasNoTilePreview));
        }
    }

    public bool HasTilePreview => !string.IsNullOrWhiteSpace(TilePreviewGeoJson);

    public bool HasNoTilePreview => !HasTilePreview;

    public bool ShowModelOverlay
    {
        get => showModelOverlay;
        set
        {
            if (showModelOverlay == value)
            {
                return;
            }

            showModelOverlay = value;
            if (!showModelOverlay)
            {
                ModelOverlayStatusMessage = "Host model overlay hidden.";
            }

            RaisePropertyChanged(nameof(ShowModelOverlay));
        }
    }

    public string ModelOverlayStatusMessage
    {
        get => modelOverlayStatusMessage;
        private set
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(modelOverlayStatusMessage, normalized, StringComparison.Ordinal))
            {
                return;
            }

            modelOverlayStatusMessage = normalized;
            RaisePropertyChanged(nameof(ModelOverlayStatusMessage));
        }
    }

    public double? TilePreviewReferenceLatitude => referenceContext?.AnchorLatitude;

    public double? TilePreviewReferenceLongitude => referenceContext?.AnchorLongitude;

    public string TilePreviewReferenceTitle => referenceContext?.Title ?? "Reference Context";

    public string TilePreviewStatusText => BuildTilePreviewStatusText();

    public int PreparedShapeCount => preparedPlan?.Shapes.Count ?? 0;

    public int PreparedSurfaceCount => preparedPlan?.PreparedSurfaceCount ?? 0;

    public int PreparedTriangleCount => preparedPlan?.PreparedTriangleCount ?? 0;

    public int SelectedCategoryCount => FeatureTypeOptions.Count(option => option.IsSelected);

    public int SelectedTileCount => TileOptions.Count(option => option.IsSelected);

    public int TotalTileCount => TileOptions.Count;

    public string TileSelectionHeaderText => string.Format(
        CultureInfo.CurrentCulture,
        UiLocalizer.Instance.Get("Plateau.Map.TileCountFormat"),
        SelectedTileCount,
        TotalTileCount);

    public PlateauImportState? ImportState => importState;

    public PlateauImportReferenceContext? CurrentReferenceContext => referenceContext;

    public ContextImportPlan? PreparedPlan => preparedPlan;

    public PlateauImportReferenceSource SelectedReferenceSource => SelectedReferenceSourceOption?.Source ?? PlateauImportReferenceSource.WorkingProjectBasePoint;

    public PlateauGeometryImportMode SelectedGeometryImportMode => SelectedGeometryImportModeOption?.Mode ?? PlateauGeometryImportMode.LightweightExtrusion;

    public PlateauImportReferenceSourceOption? SelectedReferenceSourceOption
    {
        get => selectedReferenceSourceOption;
        set
        {
            if (selectedReferenceSourceOption == value || value is null)
            {
                return;
            }

            selectedReferenceSourceOption = value;
            RefreshReferenceContext(clearPreview: true);
            RaisePropertyChanged(nameof(SelectedReferenceSourceOption));
            RaisePropertyChanged(nameof(SelectedReferenceSource));
            RaisePropertyChanged(nameof(ReferenceSourceDescription));
        }
    }

    public PlateauGeometryImportModeOption? SelectedGeometryImportModeOption
    {
        get => selectedGeometryImportModeOption;
        set
        {
            if (selectedGeometryImportModeOption == value || value is null)
            {
                return;
            }

            selectedGeometryImportModeOption = value;
            ClearPreview(clearWarnings: false);
            StatusMessage = BuildBaseStatusMessage();
            RaisePropertyChanged(nameof(SelectedGeometryImportModeOption));
            RaisePropertyChanged(nameof(SelectedGeometryImportMode));
            RaisePropertyChanged(nameof(GeometryImportModeTitle));
            RaisePropertyChanged(nameof(GeometryImportModeDescription));
        }
    }

    public string ReferenceSourceTitle => referenceContext?.Title ?? SelectedReferenceSourceOption?.Title ?? "Reference unavailable";

    public string ReferenceSourceDescription => referenceContext?.Description ?? SelectedReferenceSourceOption?.Description ?? string.Empty;

    public string GeometryImportModeTitle => SelectedGeometryImportModeOption?.Title ?? SelectedGeometryImportMode.GetDisplayName();

    public string GeometryImportModeDescription => SelectedGeometryImportModeOption?.Description ?? string.Empty;

    public bool HasSuggestedTiles => SuggestedTiles.Count > 0;

    public bool HasNoSuggestedTiles => !HasSuggestedTiles;

    public bool HasLastImportRows => LastImportRows.Count > 0;

    public bool HasNoLastImportRows => !HasLastImportRows;

    public bool HasScanRows => ScanRows.Count > 0;

    public bool HasNoScanRows => !HasScanRows;

    public bool HasDetectedSourceFiles => DetectedSourceFiles.Count > 0;

    public bool HasNoDetectedSourceFiles => !HasDetectedSourceFiles;

    public bool HasPreviewRows => PreviewRows.Count > 0;

    public bool HasNoPreviewRows => !HasPreviewRows;

    public bool HasFeatureNames => FeatureNames.Count > 0;

    public bool HasNoFeatureNames => !HasFeatureNames;

    public bool HasFeatureTypeOptions => FeatureTypeOptions.Count > 0;

    public bool HasNoFeatureTypeOptions => !HasFeatureTypeOptions;

    public bool HasTileOptions => TileOptions.Count > 0;

    public bool HasNoTileOptions => !HasTileOptions;

    public bool HasWarningMessages => WarningMessages.Count > 0;

    public bool HasNoWarningMessages => !HasWarningMessages;

    public bool TryScanFolder()
    {
        if (!TryStartFolderScan(out string folderPath))
        {
            return false;
        }

        try
        {
            PlateauFolderScanResult result = folderScanService.ScanFolder(folderPath, ReportScanProgress);
            return ApplyScanResult(result);
        }
        catch (Exception ex)
        {
            HandleScanFailure(ex);
            return false;
        }
        finally
        {
            FinishFolderScan();
        }
    }

    public bool TryStartFolderScan(out string folderPath)
    {
        ActionMessage = string.Empty;
        folderPath = SelectedFolderPath;

        if (IsPreparingPreview)
        {
            StatusMessage = "Wait for the current preview to finish loading before starting a new scan.";
            ResetScanProgress();
            return false;
        }

        if (IsExportingShapefile)
        {
            StatusMessage = "Wait for the current shapefile export to finish before starting a new scan.";
            ResetScanProgress();
            return false;
        }

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            StatusMessage = "Choose a PLATEAU folder before scanning.";
            ResetScanProgress();
            return false;
        }

        IsScanning = true;
        ReportScanProgress(new PlateauScanProgress(PlateauScanPhase.Enumerating, 0, 0, string.Empty));
        return true;
    }

    public PlateauFolderScanResult ScanFolder(string folderPath, Action<PlateauScanProgress>? reportProgress = null)
    {
        return folderScanService.ScanFolder(folderPath, reportProgress);
    }

    public void ReportScanProgress(PlateauScanProgress progress)
    {
        if (progress is null)
        {
            return;
        }

        ScanProgressCurrent = progress.Current;
        ScanProgressTotal = progress.Total;
        ScanProgressPercent = progress.Percent;
        IsScanProgressIndeterminate = progress.Phase == PlateauScanPhase.Enumerating || progress.Total == 0;
        ScanProgressStatusText = BuildScanProgressStatusText(progress);
    }

    internal void ReportExportProgress(PlateauExportProgress progress)
    {
        IsExportProgressIndeterminate = true;
        ExportProgressStatusText = string.IsNullOrWhiteSpace(progress.Detail)
            ? progress.Stage
            : string.Format(CultureInfo.InvariantCulture, "{0} — {1}", progress.Stage, progress.Detail);
    }

    public bool ApplyScanResult(PlateauFolderScanResult result)
    {
        scanResult = result ?? throw new ArgumentNullException(nameof(result));
        PopulateSelections(scanResult);
        ReplaceCollection(ScanRows, BuildScanRows(scanResult));
        ReplaceCollection(DetectedSourceFiles, BuildDetectedSourceFiles(scanResult));
        ReplaceCollection(WarningMessages, scanResult.WarningMessages);
        ClearPreview(clearWarnings: false);
        if (scanResult.CityModels.Count == 0)
        {
            StatusMessage = scanResult.IsRecursivePackageScan
                ? scanResult.IsFromCache
                    ? "Reused cached scan for the selected PLATEAU package root, but no supported PLATEAU features were found under udx."
                    : "The selected PLATEAU package root was scanned, but no supported PLATEAU features were found under udx."
                : scanResult.IsFromCache
                    ? "Reused cached scan for the selected folder, but no supported PLATEAU features were found in the selected folder files."
                    : "The selected folder was scanned, but no supported PLATEAU features were found in the selected folder files.";
        }
        else
        {
            string scanMode = scanResult.IsRecursivePackageScan ? "package root" : "selected folder";
            string scanVerb = scanResult.IsFromCache ? "Reused cached scan with" : "Scanned";
            StatusMessage = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} supported file(s) from the {2}. Choose categories and click tiles on the map preview, then load a preview.",
                scanVerb,
                scanResult.SupportedFilePaths.Count,
                scanMode);
        }

        RaiseScanProperties();
        return true;
    }

    public void HandleScanFailure(Exception ex)
    {
        ClearScanAndPreview();
        StatusMessage = ex.Message;
    }

    public void FinishFolderScan()
    {
        IsScanning = false;
        ResetScanProgress();
    }

    public bool TryScanKibanFolder()
    {
        if (!TryStartKibanFolderScan(out KibanScanRequest? request) || request is null)
        {
            return false;
        }

        try
        {
            return ApplyKibanScanResult(ScanKibanFolder(request));
        }
        catch (Exception ex)
        {
            HandleKibanScanFailure(ex);
            return false;
        }
        finally
        {
            FinishKibanFolderScan();
        }
    }

    internal bool TryStartKibanFolderScan(out KibanScanRequest? request)
    {
        request = null;
        string folderPath = KibanFolderPath;
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            StatusMessage = "Choose a GSI Kiban folder before scanning.";
            return false;
        }

        if (IsScanningKiban)
        {
            return false;
        }

        if (IsExportingShapefile)
        {
            StatusMessage = "Wait for the current shapefile export to finish before scanning GSI Kiban data.";
            return false;
        }

        string[] plateauSecondaryMeshCodes = TileOptions.Count == 0
            ? Array.Empty<string>()
            : TileOptions
                .Select(tile => tile.TileId.Length >= 6 ? tile.TileId.Substring(0, 6) : string.Empty)
                .Where(code => code.Length == 6)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        IsScanningKiban = true;
        request = new KibanScanRequest(folderPath, plateauSecondaryMeshCodes, GetSelectedOptionalKibanLandUseTokens());
        return true;
    }

    internal KibanScanResult ScanKibanFolder(KibanScanRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        HashSet<string>? plateauSecondaryMeshCodes = request.PlateauSecondaryMeshCodes.Count == 0
            ? null
            : new HashSet<string>(request.PlateauSecondaryMeshCodes, StringComparer.Ordinal);

        PlateauScanSessionCache.RememberKibanFolderPath(request.FolderPath);
        string[] xmlFiles = Directory
            .EnumerateFiles(request.FolderPath, "FG-GML-*.xml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        List<string> relevantFiles = new List<string>(xmlFiles.Length);
        int skippedCount = 0;
        foreach (string filePath in xmlFiles)
        {
            if (plateauSecondaryMeshCodes is not null)
            {
                string? fileMeshCode = KibanGmlParser.ExtractKibanMeshCode(filePath);
                if (fileMeshCode is null || !plateauSecondaryMeshCodes.Contains(fileMeshCode))
                {
                    skippedCount++;
                    continue;
                }
            }

            relevantFiles.Add(filePath);
        }

        string cacheKey = PlateauScanSessionCache.BuildKibanKey(
            request.FolderPath,
            request.PlateauSecondaryMeshCodes,
            request.AdditionalGreenLandUseTokens,
            relevantFiles);
        if (PlateauScanSessionCache.TryGetKiban(cacheKey, out KibanScanResult? cachedResult) && cachedResult is not null)
        {
            return new KibanScanResult(
                cachedResult.Features,
                cachedResult.PolygonFeatures,
                cachedResult.ParsedFileCount,
                skippedCount,
                isFromCache: true);
        }

        List<KibanParsedFeature> allFeatures = new List<KibanParsedFeature>();
        List<KibanParsedPolygonFeature> allPolygonFeatures = new List<KibanParsedPolygonFeature>();
        int fileCount = 0;
        foreach (string filePath in relevantFiles)
        {
            try
            {
                KibanParseResult parseResult = kibanGmlParser.ParseFile(filePath, request.AdditionalGreenLandUseTokens);
                allFeatures.AddRange(parseResult.Lines);
                allPolygonFeatures.AddRange(parseResult.Polygons);
                fileCount++;
            }
            catch
            {
            }
        }

        KibanScanResult result = new KibanScanResult(allFeatures, allPolygonFeatures, fileCount, skippedCount);
        PlateauScanSessionCache.StoreKiban(cacheKey, result);
        return result;
    }

    internal bool ApplyKibanScanResult(KibanScanResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));

        kibanParsedFeatures = result.Features;
        kibanParsedPolygonFeatures = result.PolygonFeatures;
        PopulateKibanSelections(result.Features, result.PolygonFeatures);

        int totalFeatureCount = result.Features.Count + result.PolygonFeatures.Count;
        if (totalFeatureCount == 0)
        {
            string skipInfo = result.SkippedFileCount > 0
                ? string.Format(CultureInfo.InvariantCulture, " ({0} file(s) skipped because they were outside the current PLATEAU tile set)", result.SkippedFileCount)
                : string.Empty;
            string scanVerb = result.IsFromCache ? "Reused cached GSI Kiban scan for" : "Scanned";
            StatusMessage = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} GSI Kiban file(s) but found no sidewalk, railway, water, or land-use features.{2}",
                scanVerb,
                result.ParsedFileCount,
                skipInfo);
        }
        else
        {
            int waterCount = result.PolygonFeatures.Count(f => string.Equals(f.Layer, KibanGmlParser.WaterLayer, StringComparison.Ordinal));
            int landUseCount = result.PolygonFeatures.Count(f => string.Equals(f.Layer, KibanGmlParser.LandUseLayer, StringComparison.Ordinal));
            string skipInfo = result.SkippedFileCount > 0
                ? string.Format(CultureInfo.InvariantCulture, " {0} file(s) outside the current PLATEAU tile set were skipped.", result.SkippedFileCount)
                : string.Empty;
            string scanVerb = result.IsFromCache ? "Reused cached GSI Kiban scan for" : "Scanned";
            StatusMessage = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} GSI Kiban file(s) and found {2} sidewalk/railway, {3} water, and {4} land-use feature(s).{5}",
                scanVerb,
                result.ParsedFileCount,
                result.Features.Count,
                waterCount,
                landUseCount,
                skipInfo);
        }

        return true;
    }

    internal void HandleKibanScanFailure(Exception ex)
    {
        StatusMessage = string.Format(CultureInfo.InvariantCulture, "GSI Kiban scan failed: {0}", ex.Message);
    }

    internal void FinishKibanFolderScan()
    {
        IsScanningKiban = false;
    }

    private void PopulateKibanSelections(
        IReadOnlyList<KibanParsedFeature> features,
        IReadOnlyList<KibanParsedPolygonFeature> polygonFeatures)
    {
        int sidewalkCount = features.Count(f => string.Equals(f.Layer, PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparison.Ordinal));
        int railwayCount = features.Count(f => string.Equals(f.Layer, PlateauContextOutlinesDxfWriter.GsiRailwaysLayer, StringComparison.Ordinal));
        int waterCount = polygonFeatures.Count(f => string.Equals(f.Layer, KibanGmlParser.WaterLayer, StringComparison.Ordinal));
        int landUseCount = polygonFeatures.Count(f => string.Equals(f.Layer, KibanGmlParser.LandUseLayer, StringComparison.Ordinal));

        List<KibanFeatureSelectionItem> selections = new List<KibanFeatureSelectionItem>();
        if (sidewalkCount > 0)
        {
            selections.Add(new KibanFeatureSelectionItem
            {
                LayerName = PlateauContextOutlinesDxfWriter.GsiSidewalksLayer,
                Title = string.Format(CultureInfo.InvariantCulture, "Sidewalks ({0})", sidewalkCount),
                FeatureCount = sidewalkCount,
                IsSelected = true
            });
        }

        if (railwayCount > 0)
        {
            selections.Add(new KibanFeatureSelectionItem
            {
                LayerName = PlateauContextOutlinesDxfWriter.GsiRailwaysLayer,
                Title = string.Format(CultureInfo.InvariantCulture, "Railways ({0})", railwayCount),
                FeatureCount = railwayCount,
                IsSelected = true
            });
        }

        if (waterCount > 0)
        {
            selections.Add(new KibanFeatureSelectionItem
            {
                LayerName = KibanGmlParser.WaterLayer,
                Title = string.Format(CultureInfo.InvariantCulture, "Water ({0})", waterCount),
                FeatureCount = waterCount,
                IsSelected = true
            });
        }

        if (landUseCount > 0)
        {
            selections.Add(new KibanFeatureSelectionItem
            {
                LayerName = KibanGmlParser.LandUseLayer,
                Title = string.Format(CultureInfo.InvariantCulture, "Land use ({0})", landUseCount),
                FeatureCount = landUseCount,
                IsSelected = true
            });
        }

        ReplaceSelections(KibanFeatureOptions, selections);
        RaisePropertyChanged(nameof(HasKibanFeatureOptions));
        RaisePropertyChanged(nameof(HasNoKibanFeatureOptions));
    }

    private void ClearKibanScan()
    {
        kibanParsedFeatures = null;
        kibanParsedPolygonFeatures = null;
        ReplaceCollection(KibanFeatureOptions, Array.Empty<KibanFeatureSelectionItem>());
        RaisePropertyChanged(nameof(HasKibanFeatureOptions));
        RaisePropertyChanged(nameof(HasNoKibanFeatureOptions));
    }

    public bool TryLoadPreview()
    {
        if (!TryStartPreviewLoad(out PreviewBuildRequest? request) || request is null)
        {
            return false;
        }

        try
        {
            PreviewBuildResult result = BuildPreviewResult(request);
            return ApplyPreviewResult(result);
        }
        catch (Exception ex)
        {
            HandlePreviewFailure(ex);
            return false;
        }
        finally
        {
            FinishPreviewLoad();
        }
    }

    internal bool TryStartPreviewLoad(out PreviewBuildRequest? request)
    {
        ActionMessage = string.Empty;
        request = null;

        if (IsScanning)
        {
            StatusMessage = "Wait for the current folder scan to finish before loading a preview.";
            return false;
        }

        if (IsPreparingPreview)
        {
            StatusMessage = "Preview generation is already running.";
            return false;
        }

        if (IsExportingShapefile)
        {
            StatusMessage = "Wait for the current shapefile export to finish before loading a preview.";
            return false;
        }

        if (referenceContext is null)
        {
            StatusMessage = BuildBaseStatusMessage();
            return false;
        }

        if (scanResult is null)
        {
            StatusMessage = "Scan a PLATEAU folder before loading a preview.";
            return false;
        }

        PlateauFeatureType[] selectedFeatureTypes = FeatureTypeOptions.Where(option => option.IsSelected).Select(option => option.FeatureType).ToArray();
        string[] selectedTileIds = TileOptions.Where(option => option.IsSelected).Select(option => option.TileId).ToArray();
        if (selectedFeatureTypes.Length == 0 || selectedTileIds.Length == 0)
        {
            StatusMessage = "Select at least one category and one tile before loading a preview.";
            return false;
        }

        previewRequestVersion = unchecked(previewRequestVersion + 1);
        IsPreparingPreview = true;
        StatusMessage = string.Format(
            CultureInfo.InvariantCulture,
            "Loading preview using {0} in {1} mode...",
            referenceContext.Title,
            SelectedGeometryImportMode.GetDisplayName());
        request = new PreviewBuildRequest(
            previewRequestVersion,
            scanResult,
            referenceContext,
            selectedFeatureTypes,
            selectedTileIds,
            SelectedGeometryImportMode);
        return true;
    }

    internal PreviewBuildResult BuildPreviewResult(PreviewBuildRequest request)
    {
        ContextImportPlan plan = geometryBuilder.BuildPlan(
            request.ScanResult,
            request.ReferenceContext,
            request.SelectedFeatureTypes,
            request.SelectedTileIds,
            request.GeometryImportMode);
        IReadOnlyCollection<DetailRow> previewRows = BuildPreviewRows(plan);
        IReadOnlyCollection<string> featureNames = BuildFeatureNames(plan);
        IReadOnlyCollection<string> warnings = request.ScanResult.WarningMessages
            .Concat(plan.WarningMessages)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string importMode = request.GeometryImportMode.GetDisplayName();
        string status = currentState.IsReadOnly
            ? string.Format(CultureInfo.InvariantCulture, "Preview loaded. {0} context shapes are ready in {1} mode, but this Revit project is read-only so import is disabled until the model is editable.", plan.Shapes.Count, importMode)
            : string.Format(CultureInfo.InvariantCulture, "Preview loaded. {0} context shapes are ready to import using {1} in {2} mode.", plan.Shapes.Count, request.ReferenceContext.Title, importMode);
        return new PreviewBuildResult(request.RequestVersion, plan, previewRows, featureNames, warnings, status);
    }

    internal bool ApplyPreviewResult(PreviewBuildResult result)
    {
        if (result.RequestVersion != previewRequestVersion)
        {
            return false;
        }

        preparedPlan = result.Plan;
        ReplaceCollection(PreviewRows, result.PreviewRows);
        ReplaceCollection(FeatureNames, result.FeatureNames);
        ReplaceCollection(WarningMessages, result.WarningMessages);
        StatusMessage = result.StatusMessage;
        RaisePreviewProperties();
        return true;
    }

    internal void HandlePreviewFailure(Exception ex)
    {
        ClearPreview(clearWarnings: false);
        StatusMessage = ex.Message;
    }

    internal void FinishPreviewLoad()
    {
        IsPreparingPreview = false;
    }

    internal bool TryStartShapefileExport(out ShapefileExportRequest? request)
    {
        request = null;
        if (IsExportingShapefile)
        {
            return false;
        }

        if (IsScanning || IsPreparingPreview || IsScanningKiban)
        {
            StatusMessage = "Wait for the current scan or preview operation to finish before exporting shapefiles.";
            return false;
        }

        if (!CanExportOutlines)
        {
            StatusMessage = "Scan a PLATEAU folder and select at least one category and tile before exporting shapefiles.";
            return false;
        }

        if (!TryCreateShapefileExportRequest(out request) || request is null)
        {
            StatusMessage = "Scan a PLATEAU folder and select at least one category and tile before exporting shapefiles.";
            return false;
        }

        IsExportingShapefile = true;
        StatusMessage = "Exporting shapefiles...";
        IsExportProgressIndeterminate = true;
        ExportProgressStatusText = "Preparing export...";
        return true;
    }

    internal void FinishShapefileExport()
    {
        IsExportingShapefile = false;
        IsExportProgressIndeterminate = false;
        ExportProgressStatusText = string.Empty;
    }

    internal void MarkShapefileExportSucceeded(PlateauContextShapefileWriter.WriteResult result, int warningCount, bool kibanFolderInvolved)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));

        if (kibanFolderInvolved)
        {
            int cityGmlFeatureCount = result.FeatureCount - result.LineFeatureCount - result.KibanWaterFeatureCount - result.KibanLandUseFeatureCount;
            StatusMessage = warningCount == 0
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "Exported {0} polygon feature(s), {1} sidewalk line(s), {2} railway line(s), {3} water polygon(s), and {4} land-use polygon(s).",
                    cityGmlFeatureCount,
                    result.SidewalkFeatureCount,
                    result.RailwayFeatureCount,
                    result.KibanWaterFeatureCount,
                    result.KibanLandUseFeatureCount)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Exported shapefiles with {0} warning(s): {1} sidewalk line(s), {2} railway line(s), {3} water polygon(s), and {4} land-use polygon(s).",
                    warningCount,
                    result.SidewalkFeatureCount,
                    result.RailwayFeatureCount,
                    result.KibanWaterFeatureCount,
                    result.KibanLandUseFeatureCount);
            return;
        }

        StatusMessage = warningCount == 0
            ? string.Format(
                CultureInfo.InvariantCulture,
                "Exported {0} polygon feature(s).",
                result.FeatureCount)
            : string.Format(
                CultureInfo.InvariantCulture,
                "Exported shapefiles with {0} warning(s). Review the exported files before using them downstream.",
                warningCount);
    }

    internal void MarkShapefileExportEmpty()
    {
        StatusMessage = "No shapefile geometry was produced for the current scan, filters, and tile selection.";
    }

    internal void HandleShapefileExportFailure(Exception ex)
    {
        StatusMessage = string.Format(CultureInfo.InvariantCulture, "Shapefile export failed: {0}", ex.Message);
    }

    private bool TryCreateShapefileExportRequest(out ShapefileExportRequest? request)
    {
        request = null;
        if (scanResult is null || referenceContext is null)
        {
            return false;
        }

        PlateauFeatureType[] selectedFeatureTypes = FeatureTypeOptions
            .Where(option => option.IsSelected)
            .Select(option => option.FeatureType)
            .ToArray();
        string[] selectedTileIds = TileOptions
            .Where(option => option.IsSelected)
            .Select(option => option.TileId)
            .Where(tileId => !string.IsNullOrWhiteSpace(tileId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (selectedFeatureTypes.Length == 0 || selectedTileIds.Length == 0)
        {
            return false;
        }

        string normalizedKibanFolderPath = KibanFolderPath.Trim();
        bool hasKibanFolder = !string.IsNullOrWhiteSpace(normalizedKibanFolderPath);
        bool hasKibanLayerOptions = KibanFeatureOptions.Count > 0;
        string[] selectedKibanLayerNames = hasKibanLayerOptions
            ? KibanFeatureOptions
                .Where(option => option.IsSelected)
                .Select(option => option.LayerName)
                .Where(layer => !string.IsNullOrWhiteSpace(layer))
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : hasKibanFolder
                ? DefaultKibanLayers.ToArray()
                : Array.Empty<string>();

        request = new ShapefileExportRequest(
            scanResult,
            referenceContext,
            selectedFeatureTypes,
            selectedTileIds,
            normalizedKibanFolderPath,
            kibanParsedFeatures,
            kibanParsedPolygonFeatures,
            selectedKibanLayerNames,
            hasKibanLayerOptions,
            GetSelectedOptionalKibanLandUseTokens());
        return true;
    }

    /// <summary>
    /// Builds a lightweight context export package in shared/projected metres. DXF
    /// callers can still shift it by <see cref="PlateauOutlineDxfExportPackage.OriginOffsetMetres"/>;
    /// shapefile callers use the shared/projected metre coordinates directly.
    /// </summary>
    public PlateauOutlineDxfExportPackage BuildOutlineDxfExportPackage(out IReadOnlyList<string> warnings)
    {
        if (!TryCreateShapefileExportRequest(out ShapefileExportRequest? request) || request is null)
        {
            warnings = Array.Empty<string>();
            return CreateEmptyOutlineDxfExportPackage();
        }

        return BuildOutlineDxfExportPackage(request, out warnings);
    }

    internal PlateauOutlineDxfExportPackage BuildOutlineDxfExportPackage(
        ShapefileExportRequest request,
        out IReadOnlyList<string> warnings)
    {
        return BuildOutlineDxfExportPackage(request, out warnings, progress: null, revitModelFeatures: null);
    }

    internal PlateauOutlineDxfExportPackage BuildOutlineDxfExportPackage(
        ShapefileExportRequest request,
        out IReadOnlyList<string> warnings,
        Action<PlateauExportProgress>? progress)
    {
        return BuildOutlineDxfExportPackage(request, out warnings, progress, revitModelFeatures: null);
    }

    internal PlateauOutlineDxfExportPackage BuildOutlineDxfExportPackage(
        ShapefileExportRequest request,
        out IReadOnlyList<string> warnings,
        Action<PlateauExportProgress>? progress,
        IReadOnlyList<RevitModelFootprintFeature>? revitModelFeatures)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        progress?.Invoke(new PlateauExportProgress("Building polygon outlines"));
        if (!TryBuildOutlinePlan(request, out ContextImportPlan? plan, out IReadOnlyList<string> planWarnings) || plan is null)
        {
            warnings = Array.Empty<string>();
            return CreateEmptyOutlineDxfExportPackage();
        }

        PlateauDxfExportFrame dxfFrame = PlateauDxfExportFrame.Create(plan.ReferenceContext, currentState);
        List<string> exportWarnings = new List<string>(planWarnings);
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> rawOutlines = BuildOutlineFeatures(plan, dxfFrame);
        progress?.Invoke(new PlateauExportProgress("Dissolving road areas"));
        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = PlateauRoadOutlineCleaner.DissolveRoads(rawOutlines, exportWarnings);
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines = rawOutlines
            .Where(outline => !string.Equals(outline.Layer, "PLATEAU_ROADS", StringComparison.Ordinal))
            .ToArray();

        IReadOnlyList<KibanLineExportFeature> kibanLineFeatures = BuildKibanLineFeatures(request, exportWarnings, progress);

        // Split sidewalks off the line stream — they're now exported as one-sided strip polygons.
        KibanLineExportFeature[] sidewalkLines = kibanLineFeatures
            .Where(line => string.Equals(line.Layer, PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparison.Ordinal))
            .ToArray();
        KibanLineExportFeature[] nonSidewalkLines = kibanLineFeatures
            .Where(line => !string.Equals(line.Layer, PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparison.Ordinal))
            .ToArray();

        IReadOnlyList<KibanPolygonExportFeature> kibanPolygonFeatures = BuildKibanPolygonFeatures(request, exportWarnings, progress);
        IReadOnlyList<KibanPolygonExportFeature> sidewalkStrips = BuildSidewalkStripPolygons(request, sidewalkLines, roadAreas, exportWarnings, progress);
        if (sidewalkStrips.Count > 0)
        {
            kibanPolygonFeatures = kibanPolygonFeatures.Concat(sidewalkStrips).ToArray();
        }

        warnings = exportWarnings.ToArray();
        return new PlateauOutlineDxfExportPackage(
            outlines,
            roadAreas,
            Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
            nonSidewalkLines,
            kibanPolygonFeatures,
            revitModelFeatures ?? (IReadOnlyList<RevitModelFootprintFeature>)Array.Empty<RevitModelFootprintFeature>(),
            plan.ReferenceContext.ProjectCrs,
            dxfFrame.ProjectBasePointSharedMetres,
            dxfFrame.SurveyPointSharedMetres);
    }

    private static PlateauOutlineDxfExportPackage CreateEmptyOutlineDxfExportPackage()
    {
        return new PlateauOutlineDxfExportPackage(
            Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
            Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
            Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
            Array.Empty<KibanLineExportFeature>(),
            Array.Empty<KibanPolygonExportFeature>(),
            new CrsReference(),
            Vector3d.Zero,
            Vector3d.Zero);
    }

    internal PlateauContextShapefileWriter.WriteResult WriteShapefilesStreaming(
        string shapefilePath,
        ShapefileExportRequest request,
        bool includePlateauContext,
        bool includeKibanData,
        bool includeRevitModel,
        IReadOnlyList<RevitModelFootprintFeature> revitModelFeatures,
        Action<PlateauExportProgress>? progress = null)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (revitModelFeatures is null) throw new ArgumentNullException(nameof(revitModelFeatures));

        List<string> exportWarnings = new List<string>(request.ScanResult.WarningMessages);
        using PlateauContextShapefileWriter.StreamingWriteSession session = PlateauContextShapefileWriter.OpenStreaming(
            shapefilePath,
            request.ReferenceContext.ProjectCrs,
            exportWarnings);

        Dictionary<string, List<PlateauContextOutlinesDxfWriter.AreaFeature>> roadContextBySecondaryMesh =
            new Dictionary<string, List<PlateauContextOutlinesDxfWriter.AreaFeature>>(StringComparer.Ordinal);
        bool needsSidewalkRoadContext = includeKibanData
            && request.SelectedKibanLayerNames.Contains(PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparer.Ordinal);

        if (includePlateauContext || needsSidewalkRoadContext)
        {
            StreamPlateauShapefileBatches(
                request,
                includePlateauContext,
                needsSidewalkRoadContext,
                session,
                roadContextBySecondaryMesh,
                exportWarnings,
                progress);
        }

        if (includeKibanData)
        {
            StreamKibanShapefileBatches(
                request,
                session,
                roadContextBySecondaryMesh,
                exportWarnings,
                progress);
        }

        if (includeRevitModel && revitModelFeatures.Count > 0)
        {
            progress?.Invoke(new PlateauExportProgress(
                "Writing Revit model shapefiles",
                string.Format(CultureInfo.InvariantCulture, "{0} feature(s)", revitModelFeatures.Count)));
            session.WriteRevitModelFeatures(revitModelFeatures);
        }

        return session.Complete();
    }

    private void StreamPlateauShapefileBatches(
        ShapefileExportRequest request,
        bool writePlateauContext,
        bool collectRoadContext,
        PlateauContextShapefileWriter.StreamingWriteSession session,
        IDictionary<string, List<PlateauContextOutlinesDxfWriter.AreaFeature>> roadContextBySecondaryMesh,
        List<string> exportWarnings,
        Action<PlateauExportProgress>? progress)
    {
        string[] selectedTileIds = request.SelectedTileIds
            .Where(tileId => !string.IsNullOrWhiteSpace(tileId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tileId => tileId, StringComparer.Ordinal)
            .ToArray();
        HashSet<PlateauFeatureType> selectedTypes = new HashSet<PlateauFeatureType>(request.SelectedFeatureTypes);
        HashSet<PlateauFeatureType> buildTypes = new HashSet<PlateauFeatureType>(selectedTypes);
        if (collectRoadContext)
        {
            buildTypes.Add(PlateauFeatureType.Road);
        }

        bool writeRoads = writePlateauContext && selectedTypes.Contains(PlateauFeatureType.Road);
        PlateauDxfExportFrame dxfFrame = PlateauDxfExportFrame.Create(request.ReferenceContext, currentState);

        foreach (string tileId in selectedTileIds)
        {
            PlateauFolderScanResult batchScan = BuildFilteredScanForTiles(
                request.ScanResult,
                buildTypes,
                new[] { tileId });
            if (batchScan.CityModels.Count == 0)
            {
                continue;
            }

            progress?.Invoke(new PlateauExportProgress("Building PLATEAU shapefile batch", tileId));
            ContextImportPlan plan = geometryBuilder.BuildPlan(
                batchScan,
                request.ReferenceContext,
                buildTypes,
                new[] { tileId },
                PlateauGeometryImportMode.LightweightExtrusion);
            exportWarnings.AddRange(plan.WarningMessages);

            IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> rawOutlines = BuildOutlineFeatures(plan, dxfFrame);
            IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = PlateauRoadOutlineCleaner.DissolveRoads(rawOutlines, exportWarnings);
            if (collectRoadContext && roadAreas.Count > 0)
            {
                AddRoadContext(roadContextBySecondaryMesh, new[] { tileId }, roadAreas);
            }

            if (!writePlateauContext)
            {
                continue;
            }

            progress?.Invoke(new PlateauExportProgress("Writing PLATEAU shapefile batch", tileId));
            if (writeRoads)
            {
                session.WritePlateauRoadAreas(roadAreas);
            }

            session.WritePlateauOutlines(rawOutlines
                .Where(outline => !string.Equals(outline.Layer, "PLATEAU_ROADS", StringComparison.Ordinal))
                .ToArray());
        }
    }

    private void StreamKibanShapefileBatches(
        ShapefileExportRequest request,
        PlateauContextShapefileWriter.StreamingWriteSession session,
        IReadOnlyDictionary<string, List<PlateauContextOutlinesDxfWriter.AreaFeature>> roadContextBySecondaryMesh,
        List<string> warnings,
        Action<PlateauExportProgress>? progress)
    {
        ISet<string> selectedLayers = new HashSet<string>(request.SelectedKibanLayerNames, StringComparer.Ordinal);
        if (selectedLayers.Count == 0)
        {
            if (request.HasKibanFolder && request.HasKibanLayerOptions)
            {
                warnings.Add("GSI Kiban features skipped: no GSI layers are selected.");
            }

            return;
        }

        if (kibanCoordinateTransformer is null)
        {
            warnings.Add("GSI Kiban features skipped: coordinate transformer is not available.");
            return;
        }

        bool needsLineFeatures = selectedLayers.Contains(PlateauContextOutlinesDxfWriter.GsiSidewalksLayer)
            || selectedLayers.Contains(PlateauContextOutlinesDxfWriter.GsiRailwaysLayer);
        bool needsPolygonFeatures = selectedLayers.Contains(KibanGmlParser.WaterLayer)
            || selectedLayers.Contains(KibanGmlParser.LandUseLayer);

        IReadOnlyList<KibanParsedFeature>? sourceLineFeatures = request.KibanParsedFeatures;
        IReadOnlyList<KibanParsedPolygonFeature>? sourcePolygonFeatures = request.KibanParsedPolygonFeatures;
        if (request.HasKibanFolder
            && ((needsLineFeatures && (sourceLineFeatures is null || sourceLineFeatures.Count == 0))
                || (needsPolygonFeatures && (sourcePolygonFeatures is null || sourcePolygonFeatures.Count == 0))))
        {
            progress?.Invoke(new PlateauExportProgress("Scanning GSI Kiban folder"));
            KibanScanResult scanResult = ScanKibanFolder(new KibanScanRequest(
                request.KibanFolderPath,
                BuildSecondaryMeshCodes(request.SelectedTileIds),
                request.AdditionalGreenLandUseTokens));
            sourceLineFeatures = scanResult.Features;
            sourcePolygonFeatures = scanResult.PolygonFeatures;
            if (scanResult.SkippedFileCount > 0 && sourceLineFeatures.Count == 0 && sourcePolygonFeatures.Count == 0)
            {
                warnings.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "GSI Kiban folder was scanned during export, but no selected Kiban features were found. {0} file(s) outside the selected CityGML mesh set were skipped.",
                    scanResult.SkippedFileCount));
            }
        }

        string[] secondaryMeshCodes = BuildSecondaryMeshCodes(request.SelectedTileIds).ToArray();
        bool wroteLineFeature = false;
        bool wrotePolygonFeature = false;
        foreach (string secondaryMeshCode in secondaryMeshCodes)
        {
            string[] selectedTileIdsForMesh = BuildSelectedTileIdsForSecondaryMesh(request.SelectedTileIds, secondaryMeshCode);
            if (selectedTileIdsForMesh.Length == 0)
            {
                continue;
            }

            if (needsLineFeatures && sourceLineFeatures is not null && sourceLineFeatures.Count > 0)
            {
                KibanParsedFeature[] lineBatch = sourceLineFeatures
                    .Where(feature => selectedLayers.Contains(feature.Layer)
                        && string.Equals(feature.MeshCode, secondaryMeshCode, StringComparison.Ordinal))
                    .ToArray();
                if (lineBatch.Length > 0)
                {
                    progress?.Invoke(new PlateauExportProgress(
                        "Projecting GSI line batch",
                        string.Format(CultureInfo.InvariantCulture, "{0}: {1} feature(s)", secondaryMeshCode, lineBatch.Length)));
                    IReadOnlyList<KibanLineExportFeature> projectedLines = KibanGeometryConverter.ConvertToLines(
                        lineBatch,
                        selectedTileIdsForMesh,
                        request.ReferenceContext.ProjectCrs,
                        kibanCoordinateTransformer,
                        warnings);
                    KibanLineExportFeature[] railwayLines = projectedLines
                        .Where(feature => string.Equals(feature.Layer, PlateauContextOutlinesDxfWriter.GsiRailwaysLayer, StringComparison.Ordinal))
                        .ToArray();
                    if (railwayLines.Length > 0)
                    {
                        session.WriteKibanLines(railwayLines);
                        wroteLineFeature = true;
                    }

                    KibanLineExportFeature[] sidewalkLines = projectedLines
                        .Where(feature => string.Equals(feature.Layer, PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparison.Ordinal))
                        .ToArray();
                    if (sidewalkLines.Length > 0)
                    {
                        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadContext = roadContextBySecondaryMesh.TryGetValue(secondaryMeshCode, out List<PlateauContextOutlinesDxfWriter.AreaFeature>? roadAreas)
                            ? roadAreas
                            : Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>();
                        progress?.Invoke(new PlateauExportProgress(
                            "Building GSI sidewalk strips",
                            string.Format(CultureInfo.InvariantCulture, "{0}: {1} sidewalk line(s)", secondaryMeshCode, sidewalkLines.Length)));
                        IReadOnlyList<KibanPolygonExportFeature> sidewalkStrips = SidewalkStripBuilder.Build(
                            sidewalkLines,
                            roadContext,
                            selectedTileIdsForMesh,
                            request.ReferenceContext.ProjectCrs,
                            kibanCoordinateTransformer,
                            SidewalkStripOptions.Default,
                            warnings);
                        if (sidewalkStrips.Count > 0)
                        {
                            session.WriteKibanPolygons(sidewalkStrips);
                            wrotePolygonFeature = true;
                        }
                    }
                }
            }

            if (needsPolygonFeatures && sourcePolygonFeatures is not null && sourcePolygonFeatures.Count > 0)
            {
                KibanParsedPolygonFeature[] polygonBatch = sourcePolygonFeatures
                    .Where(feature => selectedLayers.Contains(feature.Layer)
                        && string.Equals(feature.MeshCode, secondaryMeshCode, StringComparison.Ordinal))
                    .ToArray();
                if (polygonBatch.Length > 0)
                {
                    progress?.Invoke(new PlateauExportProgress(
                        "Clipping GSI polygon batch",
                        string.Format(CultureInfo.InvariantCulture, "{0}: {1} polygon(s)", secondaryMeshCode, polygonBatch.Length)));
                    IReadOnlyList<KibanPolygonExportFeature> polygons = KibanGeometryConverter.ConvertToPolygons(
                        polygonBatch,
                        selectedTileIdsForMesh,
                        request.ReferenceContext.ProjectCrs,
                        kibanCoordinateTransformer,
                        warnings);
                    if (polygons.Count > 0)
                    {
                        session.WriteKibanPolygons(polygons);
                        wrotePolygonFeature = true;
                    }
                }
            }
        }

        if (request.HasKibanFolder && needsLineFeatures && !wroteLineFeature)
        {
            warnings.Add("GSI Kiban data was scanned, but no sidewalk or railway lines intersected the selected CityGML tile(s).");
        }

        if (request.HasKibanFolder && needsPolygonFeatures && !wrotePolygonFeature)
        {
            warnings.Add("GSI Kiban polygon data was scanned, but no polygons intersected the selected CityGML tile(s).");
        }
    }

    private static PlateauFolderScanResult BuildFilteredScanForTiles(
        PlateauFolderScanResult source,
        ISet<PlateauFeatureType> selectedTypes,
        IReadOnlyCollection<string> selectedTileIds)
    {
        HashSet<string> selectedTiles = new HashSet<string>(selectedTileIds, StringComparer.Ordinal);
        List<PlateauCityModel> cityModels = new List<PlateauCityModel>();
        foreach (PlateauCityModel cityModel in source.CityModels)
        {
            PlateauContextFeature[] features = cityModel.Features
                .Where(feature => selectedTypes.Contains(feature.FeatureType)
                    && IsTileSelectedForExport(ResolveTileIdForExport(feature, cityModel), selectedTiles))
                .ToArray();
            if (features.Length == 0)
            {
                continue;
            }

            cityModels.Add(new PlateauCityModel
            {
                SourcePath = cityModel.SourcePath,
                SrsName = cityModel.SrsName,
                EpsgCode = cityModel.EpsgCode,
                FileTileId = cityModel.FileTileId,
                Features = features
            });
        }

        return new PlateauFolderScanResult
        {
            FolderPath = source.FolderPath,
            SearchRootPath = source.SearchRootPath,
            IsRecursivePackageScan = source.IsRecursivePackageScan,
            SupportedFilePaths = cityModels.Select(model => model.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            CityModels = cityModels,
            WarningMessages = Array.Empty<string>()
        };
    }

    private static void AddRoadContext(
        IDictionary<string, List<PlateauContextOutlinesDxfWriter.AreaFeature>> roadContextBySecondaryMesh,
        IReadOnlyCollection<string> selectedTileIds,
        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas)
    {
        foreach (string secondaryMeshCode in BuildSecondaryMeshCodes(selectedTileIds))
        {
            if (!roadContextBySecondaryMesh.TryGetValue(secondaryMeshCode, out List<PlateauContextOutlinesDxfWriter.AreaFeature>? existing))
            {
                existing = new List<PlateauContextOutlinesDxfWriter.AreaFeature>();
                roadContextBySecondaryMesh.Add(secondaryMeshCode, existing);
            }

            existing.AddRange(roadAreas);
        }
    }

    private static string[] BuildSelectedTileIdsForSecondaryMesh(
        IReadOnlyCollection<string> selectedTileIds,
        string secondaryMeshCode)
    {
        return selectedTileIds
            .Where(tileId => !string.IsNullOrWhiteSpace(tileId)
                && tileId.Trim().StartsWith(secondaryMeshCode, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveTileIdForExport(PlateauContextFeature feature, PlateauCityModel model)
    {
        if (!string.IsNullOrWhiteSpace(feature.TileId))
        {
            return feature.TileId;
        }

        if (!string.IsNullOrWhiteSpace(model.FileTileId))
        {
            return model.FileTileId!;
        }

        string fileName = Path.GetFileNameWithoutExtension(model.SourcePath) ?? string.Empty;
        return string.IsNullOrWhiteSpace(fileName) ? "unassigned" : fileName;
    }

    private static bool IsTileSelectedForExport(string tileId, ISet<string> selectedTileIds)
    {
        if (selectedTileIds.Contains(tileId))
        {
            return true;
        }

        if (tileId.Length == 6)
        {
            return selectedTileIds.Any(selectedTileId =>
                selectedTileId.Length > tileId.Length
                && selectedTileId.StartsWith(tileId, StringComparison.Ordinal));
        }

        return false;
    }

    private IReadOnlyList<KibanLineExportFeature> BuildKibanLineFeatures(ShapefileExportRequest request, List<string> warnings, Action<PlateauExportProgress>? progress = null)
    {
        bool hasKibanFolder = request.HasKibanFolder;
        IReadOnlyList<KibanParsedFeature>? sourceFeatures = request.KibanParsedFeatures;
        if ((sourceFeatures is null || sourceFeatures.Count == 0) && hasKibanFolder)
        {
            KibanScanResult scanResult = ScanKibanFolder(new KibanScanRequest(
                request.KibanFolderPath,
                BuildSecondaryMeshCodes(request.SelectedTileIds),
                request.AdditionalGreenLandUseTokens));
            sourceFeatures = scanResult.Features;
            if (sourceFeatures.Count == 0)
            {
                string skipInfo = scanResult.SkippedFileCount > 0
                    ? string.Format(CultureInfo.InvariantCulture, " {0} file(s) outside the selected CityGML mesh set were skipped.", scanResult.SkippedFileCount)
                    : string.Empty;
                warnings.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "GSI Kiban folder was scanned during export, but no sidewalk or railway features were found for the selected CityGML mesh set.{0}",
                    skipInfo));
                return Array.Empty<KibanLineExportFeature>();
            }
        }

        if (sourceFeatures is null || sourceFeatures.Count == 0)
        {
            return Array.Empty<KibanLineExportFeature>();
        }

        if (kibanCoordinateTransformer is null)
        {
            warnings.Add("GSI Kiban features skipped: coordinate transformer is not available.");
            return Array.Empty<KibanLineExportFeature>();
        }

        ISet<string> selectedLayers = new HashSet<string>(
            request.SelectedKibanLayerNames,
            StringComparer.Ordinal);

        if (selectedLayers.Count == 0)
        {
            if (hasKibanFolder && request.HasKibanLayerOptions)
            {
                warnings.Add("GSI Kiban features skipped: no GSI line layers are selected.");
            }

            return Array.Empty<KibanLineExportFeature>();
        }

        ISet<string> selectedTileIds = new HashSet<string>(
            request.SelectedTileIds,
            StringComparer.Ordinal);

        List<KibanParsedFeature> filteredFeatures = sourceFeatures
            .Where(feature => selectedLayers.Contains(feature.Layer)
                && !string.IsNullOrEmpty(feature.MeshCode)
                && selectedTileIds.Any(tileId => tileId.StartsWith(feature.MeshCode, StringComparison.Ordinal)))
            .ToList();

        if (filteredFeatures.Count == 0)
        {
            if (hasKibanFolder)
            {
                warnings.Add("GSI Kiban data was available, but no sidewalk or railway features matched the selected CityGML mesh set and GSI layer selection.");
            }

            return Array.Empty<KibanLineExportFeature>();
        }

        try
        {
            progress?.Invoke(new PlateauExportProgress(
                "Projecting GSI lines",
                string.Format(CultureInfo.InvariantCulture, "{0} feature(s)", filteredFeatures.Count)));
            IReadOnlyList<KibanLineExportFeature> lineFeatures = KibanGeometryConverter.ConvertToLines(
                filteredFeatures,
                selectedTileIds.ToArray(),
                request.ReferenceContext.ProjectCrs,
                kibanCoordinateTransformer,
                warnings);
            if (lineFeatures.Count == 0 && hasKibanFolder)
            {
                warnings.Add("GSI Kiban data was scanned, but no sidewalk or railway lines intersected the selected CityGML tile(s).");
            }

            return lineFeatures;
        }
        catch (Exception ex)
        {
            warnings.Add(string.Format(CultureInfo.InvariantCulture, "GSI Kiban feature conversion failed: {0}", ex.Message));
            return Array.Empty<KibanLineExportFeature>();
        }
    }

    private IReadOnlyList<KibanPolygonExportFeature> BuildKibanPolygonFeatures(ShapefileExportRequest request, List<string> warnings, Action<PlateauExportProgress>? progress = null)
    {
        bool hasKibanFolder = request.HasKibanFolder;
        IReadOnlyList<KibanParsedPolygonFeature>? sourceFeatures = request.KibanParsedPolygonFeatures;
        if ((sourceFeatures is null || sourceFeatures.Count == 0) && hasKibanFolder)
        {
            KibanScanResult scanResult = ScanKibanFolder(new KibanScanRequest(
                request.KibanFolderPath,
                BuildSecondaryMeshCodes(request.SelectedTileIds),
                request.AdditionalGreenLandUseTokens));
            sourceFeatures = scanResult.PolygonFeatures;
            if (sourceFeatures.Count == 0)
            {
                return Array.Empty<KibanPolygonExportFeature>();
            }
        }

        if (sourceFeatures is null || sourceFeatures.Count == 0)
        {
            return Array.Empty<KibanPolygonExportFeature>();
        }

        if (kibanCoordinateTransformer is null)
        {
            warnings.Add("GSI Kiban polygons skipped: coordinate transformer is not available.");
            return Array.Empty<KibanPolygonExportFeature>();
        }

        ISet<string> selectedLayers = new HashSet<string>(
            request.SelectedKibanLayerNames,
            StringComparer.Ordinal);
        string[] selectedPolygonLayers = selectedLayers
            .Where(layer => string.Equals(layer, KibanGmlParser.WaterLayer, StringComparison.Ordinal)
                || string.Equals(layer, KibanGmlParser.LandUseLayer, StringComparison.Ordinal))
            .ToArray();

        if (selectedPolygonLayers.Length == 0)
        {
            return Array.Empty<KibanPolygonExportFeature>();
        }

        ISet<string> selectedTileIds = new HashSet<string>(
            request.SelectedTileIds,
            StringComparer.Ordinal);

        List<KibanParsedPolygonFeature> filteredFeatures = sourceFeatures
            .Where(feature => selectedLayers.Contains(feature.Layer)
                && !string.IsNullOrEmpty(feature.MeshCode)
                && selectedTileIds.Any(tileId => tileId.StartsWith(feature.MeshCode, StringComparison.Ordinal)))
            .ToList();

        if (filteredFeatures.Count == 0)
        {
            if (hasKibanFolder)
            {
                warnings.Add("GSI Kiban polygon data was available, but no polygons matched the selected CityGML mesh set and GSI layer selection.");
            }

            return Array.Empty<KibanPolygonExportFeature>();
        }

        try
        {
            progress?.Invoke(new PlateauExportProgress(
                "Clipping GSI polygons to tiles",
                string.Format(CultureInfo.InvariantCulture, "{0} polygon(s)", filteredFeatures.Count)));
            IReadOnlyList<KibanPolygonExportFeature> polygonFeatures = KibanGeometryConverter.ConvertToPolygons(
                filteredFeatures,
                selectedTileIds.ToArray(),
                request.ReferenceContext.ProjectCrs,
                kibanCoordinateTransformer,
                warnings);
            if (polygonFeatures.Count == 0 && hasKibanFolder)
            {
                warnings.Add("GSI Kiban polygon data was scanned, but no polygons intersected the selected CityGML tile(s).");
            }

            return polygonFeatures;
        }
        catch (Exception ex)
        {
            warnings.Add(string.Format(CultureInfo.InvariantCulture, "GSI Kiban polygon conversion failed: {0}", ex.Message));
            return Array.Empty<KibanPolygonExportFeature>();
        }
    }

    private IReadOnlyList<KibanPolygonExportFeature> BuildSidewalkStripPolygons(
        ShapefileExportRequest request,
        IReadOnlyList<KibanLineExportFeature> sidewalkLines,
        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas,
        List<string> warnings,
        Action<PlateauExportProgress>? progress = null)
    {
        if (sidewalkLines.Count == 0)
        {
            return Array.Empty<KibanPolygonExportFeature>();
        }

        if (!request.SelectedKibanLayerNames.Contains(PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparer.Ordinal))
        {
            return Array.Empty<KibanPolygonExportFeature>();
        }

        if (kibanCoordinateTransformer is null)
        {
            warnings.Add("GSI sidewalk-strip polygons skipped: coordinate transformer is not available.");
            return Array.Empty<KibanPolygonExportFeature>();
        }

        try
        {
            progress?.Invoke(new PlateauExportProgress(
                "Building GSI sidewalk strips",
                string.Format(CultureInfo.InvariantCulture, "{0} sidewalk line(s)", sidewalkLines.Count)));
            return SidewalkStripBuilder.Build(
                sidewalkLines,
                roadAreas,
                request.SelectedTileIds,
                request.ReferenceContext.ProjectCrs,
                kibanCoordinateTransformer,
                SidewalkStripOptions.Default,
                warnings);
        }
        catch (Exception ex)
        {
            warnings.Add(string.Format(CultureInfo.InvariantCulture, "GSI sidewalk-strip polygon generation failed: {0}", ex.Message));
            return Array.Empty<KibanPolygonExportFeature>();
        }
    }

    /// <summary>
    /// Builds outline features from the current scan + selections in shared/projected
    /// metres. Always runs the geometry builder in lightweight mode so the footprint ring
    /// is available, regardless of which geometry mode the user has selected for Revit
    /// import. Returns an empty list if scan, selection, or reference context isn't ready.
    /// </summary>
    public IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> BuildOutlineFeatures(out IReadOnlyList<string> warnings)
    {
        if (!TryBuildOutlinePlan(out ContextImportPlan? plan, out IReadOnlyList<string> planWarnings) || plan is null)
        {
            warnings = Array.Empty<string>();
            return Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>();
        }

        PlateauDxfExportFrame dxfFrame = PlateauDxfExportFrame.Create(plan.ReferenceContext, currentState);
        warnings = planWarnings.ToArray();
        return BuildOutlineFeatures(plan, dxfFrame);
    }

    private bool TryBuildOutlinePlan(out ContextImportPlan? plan, out IReadOnlyList<string> warnings)
    {
        warnings = Array.Empty<string>();
        plan = null;
        if (scanResult is null || referenceContext is null)
        {
            return false;
        }

        PlateauFeatureType[] selectedFeatureTypes = FeatureTypeOptions.Where(option => option.IsSelected).Select(option => option.FeatureType).ToArray();
        string[] selectedTileIds = TileOptions.Where(option => option.IsSelected).Select(option => option.TileId).ToArray();
        if (selectedFeatureTypes.Length == 0 || selectedTileIds.Length == 0)
        {
            return false;
        }

        plan = geometryBuilder.BuildPlan(
            scanResult,
            referenceContext,
            selectedFeatureTypes,
            selectedTileIds,
            PlateauGeometryImportMode.LightweightExtrusion);

        warnings = plan.WarningMessages.ToArray();
        return true;
    }

    private bool TryBuildOutlinePlan(ShapefileExportRequest request, out ContextImportPlan? plan, out IReadOnlyList<string> warnings)
    {
        warnings = Array.Empty<string>();
        plan = null;
        if (request.SelectedFeatureTypes.Count == 0 || request.SelectedTileIds.Count == 0)
        {
            return false;
        }

        plan = geometryBuilder.BuildPlan(
            request.ScanResult,
            request.ReferenceContext,
            request.SelectedFeatureTypes,
            request.SelectedTileIds,
            PlateauGeometryImportMode.LightweightExtrusion);

        warnings = plan.WarningMessages.ToArray();
        return true;
    }

    private static IReadOnlyCollection<string> BuildSecondaryMeshCodes(IEnumerable<string> tileIds)
    {
        if (tileIds is null)
        {
            return Array.Empty<string>();
        }

        return tileIds
            .Where(tileId => !string.IsNullOrWhiteSpace(tileId))
            .Select(tileId => tileId.Trim())
            .Where(tileId => tileId.Length >= 6)
            .Select(tileId => tileId.Substring(0, 6))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> BuildOutlineFeatures(
        ContextImportPlan plan,
        PlateauDxfExportFrame dxfFrame)
    {
        HashSet<string>? acceptedLandUseClassNames = null;
        if (LandUseClassOptions.Count > 0)
        {
            acceptedLandUseClassNames = new HashSet<string>(
                LandUseClassOptions.Where(option => option.IsSelected).Select(option => option.ClassName ?? string.Empty),
                StringComparer.Ordinal);
        }

        List<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines = new List<PlateauContextOutlinesDxfWriter.OutlineFeature>(plan.Shapes.Count);
        foreach (ContextShapePlan shape in plan.Shapes)
        {
            if (!PlateauContextOutlinesDxfWriter.LayerByFeatureType.TryGetValue(shape.FeatureType, out string? layer))
            {
                continue;
            }

            if (shape.FeatureType == PlateauFeatureType.LandUse
                && acceptedLandUseClassNames is not null
                && !acceptedLandUseClassNames.Contains(shape.ClassName ?? string.Empty))
            {
                continue;
            }

            (double X, double Y)[] vertices = new (double, double)[shape.FootprintPointsFeet.Count];
            int index = 0;
            foreach ((double xFeet, double yFeet) in shape.FootprintPointsFeet)
            {
                (double eastingMetres, double northingMetres) = dxfFrame.ToSharedPlanMetres(xFeet, yFeet);
                vertices[index++] = (eastingMetres, northingMetres);
            }

            outlines.Add(new PlateauContextOutlinesDxfWriter.OutlineFeature(
                layer,
                vertices,
                shape.SourceFeatureId,
                shape.ClassCode,
                shape.ClassName));
        }

        return outlines;
    }

    public void MarkImportSucceeded(PlateauImportResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        importState = result.UpdatedState;
        BuildLastImportRows();
        ReplaceCollection(WarningMessages, result.WarningMessages);
        ActionMessage = result.SummaryMessage;
        StatusMessage = result.WarningMessages.Count == 0
            ? "PLATEAU context geometry was imported successfully. The module-specific import state was saved separately from GeoProjectInfo."
            : string.Format(CultureInfo.InvariantCulture, "PLATEAU context geometry was imported successfully with {0} warning(s). Review the warning list below before continuing.", result.WarningMessages.Count);
        RaisePropertyChanged(nameof(ImportState));
        RaisePropertyChanged(nameof(HasWarningMessages));
        RaisePropertyChanged(nameof(HasNoWarningMessages));
    }

    private void RefreshReferenceContext(bool clearPreview)
    {
        referenceContext = referenceResolver.Resolve(currentState, info, SelectedReferenceSource);
        ReplaceCollection(CurrentStateRows, BuildCurrentStateRows(referenceContext));
        ReplaceCollection(
            SuggestedTiles,
            referenceContext is null
                ? Array.Empty<PlateauTileCandidate>()
                : tileIndex.GetCandidateTiles(referenceContext.AnchorLatitude, referenceContext.AnchorLongitude));

        if (clearPreview)
        {
            ClearPreview(clearWarnings: false);
        }

        RefreshTilePreviewState(raiseReferenceCoordinates: true);
        StatusMessage = BuildBaseStatusMessage();
        RaiseReferenceProperties();
    }

    private string BuildBaseStatusMessage()
    {
        if (!currentState.IsSupportedDocument)
        {
            return string.IsNullOrWhiteSpace(currentState.StatusMessage)
                ? "PLATEAU import is not available for this Revit document."
                : currentState.StatusMessage;
        }

        if (info?.ProjectCrs is null || info.Origin is null)
        {
            return "Shared geo metadata is missing or incomplete. Run Georeference Setup before importing PLATEAU context.";
        }

        if (referenceContext is null)
        {
            return SelectedReferenceSource == PlateauImportReferenceSource.WorkingProjectBasePoint
                ? "No readable Project Base Point reference is available yet. Save a working Project Base Point in Georeference Setup or switch to Canonical Origin."
                : "The selected import reference could not be resolved from the current shared geo state.";
        }

        if (currentState.IsReadOnly)
        {
            return "Preview is available, but importing PLATEAU context requires an editable Revit project.";
        }

        string modeText = SelectedGeometryImportMode == PlateauGeometryImportMode.DetailedDirectShape
            ? "detailed context geometry"
            : "lightweight context geometry";
        return scanResult is null
            ? "Choose a PLATEAU package root or source folder, scan it, click the tiles you want on the preview map, and then load a filtered preview before importing."
            : $"Adjust the category filters and selected tiles, load a preview, and then import the {modeText}.";
    }

    private IReadOnlyCollection<DetailRow> BuildCurrentStateRows(PlateauImportReferenceContext? resolvedReference)
    {
        return new[]
        {
            new DetailRow("Document", DocumentTitle),
            new DetailRow("Supported Document", currentState.IsSupportedDocument ? "Yes" : "No"),
            new DetailRow("Read-Only", currentState.IsReadOnly ? "Yes" : "No"),
            new DetailRow("Stored Geo Metadata", currentState.HasStoredGeoInfo ? "Yes" : "No"),
            new DetailRow("Stored CRS", info?.ProjectCrs is null ? "Not stored" : $"EPSG:{info.ProjectCrs.EpsgCode}  {info.ProjectCrs.NameSnapshot}"),
            new DetailRow("Canonical Origin", info?.Origin is null ? "Not stored" : $"{info.Origin.Latitude:F6}, {info.Origin.Longitude:F6}, elev {info.Origin.ElevationMeters:F3} m"),
            new DetailRow("Selected Reference", SelectedReferenceSourceOption?.Title ?? "Not selected"),
            new DetailRow("Resolved Context", resolvedReference?.Title ?? "Unavailable"),
            new DetailRow("Reference Location", resolvedReference is null ? "Unavailable" : $"{resolvedReference.AnchorLatitude:F6}, {resolvedReference.AnchorLongitude:F6}"),
            new DetailRow("Reference Projected", resolvedReference is null ? "Unavailable" : $"E {resolvedReference.AnchorProjectedCoordinate.Easting:F3} m, N {resolvedReference.AnchorProjectedCoordinate.Northing:F3} m"),
            new DetailRow("Reference Elevation", resolvedReference is null ? "Unavailable" : $"{resolvedReference.AnchorElevationMeters:F3} m"),
            new DetailRow("Local Anchor", resolvedReference is null ? "Unavailable" : $"X {resolvedReference.AnchorXFeet:F3} ft, Y {resolvedReference.AnchorYFeet:F3} ft, Z {resolvedReference.AnchorZFeet:F3} ft"),
            new DetailRow("Working Project Base Point", currentState.StoredWorkingProjectBasePoint?.IsValid == true ? "Saved" : "Not saved"),
            new DetailRow("Revit Project Base Point Estimate", currentState.ProjectBasePoint.HasEstimatedLocation || currentState.ProjectBasePoint.HasSharedPosition ? "Available" : "Not available")
        };
    }

    private static string BuildInitialActionMessage(PlateauImportState? importState)
    {
        if (importState is null)
        {
            return string.Empty;
        }

        string folderName = string.IsNullOrWhiteSpace(importState.LastImportedFolderPath)
            ? "previous folder"
            : Path.GetFileName(importState.LastImportedFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string dateText = importState.LastImportDateUtc.HasValue
            ? importState.LastImportDateUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "an earlier session";

        return $"Last PLATEAU import restored: '{folderName}' via {FormatReferenceSource(importState.LastReferenceSource)} in {importState.LastGeometryImportMode.GetDisplayName().ToLowerInvariant()} mode on {dateText}. Scan Folder to rebuild the preview before importing again.";
    }

    private static string BuildScanProgressStatusText(PlateauScanProgress progress)
    {
        if (progress.Phase == PlateauScanPhase.Enumerating)
        {
            return "Finding supported CityGML and XML files in the selected PLATEAU folder...";
        }

        if (progress.Phase == PlateauScanPhase.Completed)
        {
            return progress.Total == 0
                ? "No supported CityGML or XML files were found in the selected PLATEAU folder."
                : string.Format(CultureInfo.InvariantCulture, "Finished scanning {0} supported file(s).", progress.Total);
        }

        if (progress.Total == 0)
        {
            return "No supported CityGML or XML files were found in the selected PLATEAU folder.";
        }

        string fileName = string.IsNullOrWhiteSpace(progress.CurrentFileName)
            ? "current file"
            : progress.CurrentFileName;
        return string.Format(
            CultureInfo.InvariantCulture,
            "Scanning {0} of {1}: {2}",
            Math.Min(progress.Current, progress.Total),
            progress.Total,
            fileName);
    }

    private void BuildLastImportRows()
    {
        if (importState is null)
        {
            ReplaceCollection(LastImportRows, Array.Empty<DetailRow>());
            RaisePropertyChanged(nameof(HasLastImportRows));
            RaisePropertyChanged(nameof(HasNoLastImportRows));
            return;
        }

        string tileSummary = importState.LastSelectedTileIds.Count == 0
            ? "Not recorded"
            : string.Join(", ", importState.LastSelectedTileIds);
        string categorySummary = importState.LastSelectedFeatureTypes.Count == 0
            ? "Not recorded"
            : string.Join(", ", importState.LastSelectedFeatureTypes);

        ReplaceCollection(LastImportRows, new[]
        {
            new DetailRow("Last Folder", string.IsNullOrWhiteSpace(importState.LastImportedFolderPath) ? "Not recorded" : importState.LastImportedFolderPath),
            new DetailRow("Last Import Date", importState.LastImportDateUtc.HasValue ? importState.LastImportDateUtc.Value.ToString("u", CultureInfo.InvariantCulture) : "Not recorded"),
            new DetailRow("Last Reference", FormatReferenceSource(importState.LastReferenceSource)),
            new DetailRow("Last Geometry Mode", importState.LastGeometryImportMode.GetDisplayName()),
            new DetailRow("Last Imported Elements", importState.LastImportedFeatureCount.ToString(CultureInfo.InvariantCulture)),
            new DetailRow("Last Created Groups", importState.LastImportedGroupCount.ToString(CultureInfo.InvariantCulture)),
            new DetailRow("Last Categories", categorySummary),
            new DetailRow("Last Tiles", tileSummary),
            new DetailRow("Last Summary", string.IsNullOrWhiteSpace(importState.LastImportSummary) ? "Not recorded" : importState.LastImportSummary)
        });
        RaisePropertyChanged(nameof(HasLastImportRows));
        RaisePropertyChanged(nameof(HasNoLastImportRows));
    }

    private IReadOnlyCollection<DetailRow> BuildScanRows(PlateauFolderScanResult scan)
    {
        IReadOnlyCollection<PlateauContextFeature> features = scan.CityModels.SelectMany(model => model.Features).ToArray();
        string tiles = string.Join(", ", features.Select(feature => feature.TileId).Distinct(StringComparer.Ordinal).OrderBy(tileId => tileId, StringComparer.Ordinal));
        string categories = string.Join(", ", features.Select(feature => feature.FeatureType.GetPluralDisplayName()).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal));
        string scanMode = scan.IsRecursivePackageScan ? "PLATEAU package root (udx)" : "Selected folder";

        return new[]
        {
            new DetailRow("Selected Folder", scan.FolderPath),
            new DetailRow("Scan Root", scan.SearchRootPath),
            new DetailRow("Scan Mode", scanMode),
            new DetailRow("Supported Files", scan.SupportedFilePaths.Count.ToString(CultureInfo.InvariantCulture)),
            new DetailRow("Parsed Files", scan.CityModels.Count.ToString(CultureInfo.InvariantCulture)),
            new DetailRow("Detected Features", features.Count.ToString(CultureInfo.InvariantCulture)),
            new DetailRow("Detected Tiles", string.IsNullOrWhiteSpace(tiles) ? "None" : tiles),
            new DetailRow("Detected Categories", string.IsNullOrWhiteSpace(categories) ? "None" : categories),
            new DetailRow("Warnings", scan.WarningMessages.Count.ToString(CultureInfo.InvariantCulture))
        };
    }

    private static IReadOnlyCollection<string> BuildDetectedSourceFiles(PlateauFolderScanResult scan)
    {
        return BuildRelativePathList(scan.FolderPath, scan.SupportedFilePaths, 60);
    }

    private static IReadOnlyCollection<DetailRow> BuildPreviewRows(ContextImportPlan plan)
    {
        string categorySummary = plan.SelectedFeatureTypes.Count == 0
            ? "None"
            : string.Join(", ", plan.SelectedFeatureTypes.Select(type => type.GetPluralDisplayName()));
        string tileSummary = plan.SelectedTileIds.Count == 0
            ? "None"
            : string.Join(", ", plan.SelectedTileIds);
        List<DetailRow> rows = new List<DetailRow>
        {
            new DetailRow("Source Folder", plan.SourceFolderPath),
            new DetailRow("Geometry Mode", plan.GeometryImportMode.GetDisplayName()),
            new DetailRow("Selected Categories", categorySummary),
            new DetailRow("Selected Tiles", tileSummary),
            new DetailRow("Source Features", plan.SourceFeatureCount.ToString(CultureInfo.InvariantCulture)),
            new DetailRow("Importable Shapes", plan.Shapes.Count.ToString(CultureInfo.InvariantCulture)),
            new DetailRow("Import Reference", plan.ReferenceContext.Title),
            new DetailRow("Reference CRS", $"EPSG:{plan.ReferenceContext.ProjectCrs.EpsgCode}  {plan.ReferenceContext.ProjectCrs.NameSnapshot}"),
            new DetailRow("Reference Elevation", $"{plan.ReferenceContext.AnchorElevationMeters:F3} m")
        };

        if (plan.GeometryImportMode == PlateauGeometryImportMode.DetailedDirectShape)
        {
            rows.Add(new DetailRow("Prepared Surfaces", plan.PreparedSurfaceCount.ToString(CultureInfo.InvariantCulture)));
            rows.Add(new DetailRow("Prepared Triangles", plan.PreparedTriangleCount.ToString(CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    private static IReadOnlyCollection<string> BuildFeatureNames(ContextImportPlan plan)
    {
        List<string> names = plan.Shapes
            .Select(shape => $"[{shape.TileId}] {shape.FeatureType.GetDisplayName()}: {shape.DisplayName}")
            .Take(40)
            .ToList();

        if (plan.Shapes.Count > names.Count)
        {
            names.Add($"... and {plan.Shapes.Count - names.Count} more");
        }

        return names;
    }

    private void PopulateSelections(PlateauFolderScanResult scan)
    {
        HashSet<string> suggestedTileIds = new HashSet<string>(SuggestedTiles.Select(tile => tile.TileId), StringComparer.Ordinal);
        HashSet<string> selectedTypeNames = new HashSet<string>(
            importState?.LastSelectedFeatureTypes ?? new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> selectedTileIds = new HashSet<string>(
            importState?.LastSelectedTileIds ?? new List<string>(),
            StringComparer.Ordinal);

        PlateauContextFeature[] features = scan.CityModels.SelectMany(model => model.Features).ToArray();

        List<PlateauFeatureSelectionItem> featureSelections = features
            .GroupBy(feature => feature.FeatureType)
            .OrderBy(group => group.Key)
            .Select(group => CreateFeatureSelection(
                group.Key,
                group.Count(),
                group.Select(feature => feature.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                scan.FolderPath,
                selectedTypeNames))
            .ToList();
        List<PlateauTileSelectionItem> tileSelections = features
            .GroupBy(feature => feature.TileId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => CreateTileSelection(
                group.Key,
                group.Count(),
                group.Select(feature => feature.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                scan.FolderPath,
                suggestedTileIds.Contains(group.Key),
                selectedTileIds))
            .ToList();

        List<PlateauLandUseClassSelectionItem> landUseSelections = features
            .Where(feature => feature.FeatureType == PlateauFeatureType.LandUse)
            .GroupBy(feature => feature.ClassName ?? string.Empty, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new PlateauLandUseClassSelectionItem
            {
                ClassCode = group.First().ClassCode ?? string.Empty,
                ClassName = group.Key,
                Title = string.IsNullOrEmpty(group.Key)
                    ? string.Format(CultureInfo.InvariantCulture, "(unclassified) ({0})", group.Count())
                    : string.Format(CultureInfo.InvariantCulture, "{0} ({1})", group.Key, group.Count()),
                FeatureCount = group.Count(),
                IsSelected = IsGreenPlateauLandUseClassName(group.Key)
            })
            .ToList();

        ReplaceSelections(FeatureTypeOptions, featureSelections);
        ReplaceSelections(TileOptions, tileSelections);
        ReplaceSelections(LandUseClassOptions, landUseSelections);
        RefreshTilePreviewState(raiseReferenceCoordinates: true);
        RaisePropertyChanged(nameof(HasFeatureTypeOptions));
        RaisePropertyChanged(nameof(HasNoFeatureTypeOptions));
        RaisePropertyChanged(nameof(HasTileOptions));
        RaisePropertyChanged(nameof(HasNoTileOptions));
        RaisePropertyChanged(nameof(HasLandUseClassOptions));
        RaisePropertyChanged(nameof(SelectedCategoryCount));
        RaisePropertyChanged(nameof(SelectedTileCount));
        RaisePropertyChanged(nameof(TotalTileCount));
        RaisePropertyChanged(nameof(TileSelectionHeaderText));
    }

    private static PlateauFeatureSelectionItem CreateFeatureSelection(
        PlateauFeatureType featureType,
        int featureCount,
        IReadOnlyCollection<string> sourcePaths,
        string baseFolder,
        ISet<string> selectedTypeNames)
    {
        bool selectByDefault = selectedTypeNames.Count == 0 || selectedTypeNames.Contains(featureType.ToString());
        return new PlateauFeatureSelectionItem
        {
            FeatureType = featureType,
            Title = featureType.GetPluralDisplayName(),
            Description = string.Format(
                CultureInfo.InvariantCulture,
                "{0} file(s), {1} parsed {2} found in the scanned PLATEAU source set.",
                sourcePaths.Count,
                featureCount,
                featureType.GetPluralDisplayName().ToLowerInvariant()),
            FeatureCount = featureCount,
            SourceFileCount = sourcePaths.Count,
            SourceFilesSummary = BuildRelativeFileSummary(baseFolder, sourcePaths, 6),
            IsSelected = selectByDefault
        };
    }

    private static PlateauTileSelectionItem CreateTileSelection(
        string tileId,
        int featureCount,
        IReadOnlyCollection<string> sourcePaths,
        string baseFolder,
        bool isSuggested,
        ISet<string> selectedTileIds)
    {
        bool selectByDefault = selectedTileIds.Count > 0 && selectedTileIds.Contains(tileId);
        return new PlateauTileSelectionItem
        {
            TileId = tileId,
            FeatureCount = featureCount,
            SourceFileCount = sourcePaths.Count,
            SourceFilesSummary = BuildRelativeFileSummary(baseFolder, sourcePaths, 4),
            IsSuggested = isSuggested,
            IsSelected = selectByDefault
        };
    }


    public bool ToggleTileSelection(string tileId)
    {
        if (string.IsNullOrWhiteSpace(tileId))
        {
            return false;
        }

        PlateauTileSelectionItem? option = TileOptions.FirstOrDefault(item => string.Equals(item.TileId, tileId, StringComparison.Ordinal));
        if (option is null)
        {
            return false;
        }

        option.IsSelected = !option.IsSelected;
        return true;
    }

    public int SelectTilesByIds(IEnumerable<string> tileIds)
    {
        if (tileIds is null)
        {
            return 0;
        }

        HashSet<string> ids = new HashSet<string>(tileIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return 0;
        }

        int added = 0;
        foreach (PlateauTileSelectionItem option in TileOptions)
        {
            if (ids.Contains(option.TileId) && !option.IsSelected)
            {
                option.IsSelected = true;
                added++;
            }
        }

        return added;
    }

    public int ClearAllTileSelections()
    {
        int cleared = 0;
        foreach (PlateauTileSelectionItem option in TileOptions)
        {
            if (option.IsSelected)
            {
                option.IsSelected = false;
                cleared++;
            }
        }

        return cleared;
    }

    private void RefreshTilePreviewState(bool raiseReferenceCoordinates = false)
    {
        TilePreviewGeoJson = TileOptions.Count == 0
            ? string.Empty
            : tileOverlayService.CreateGeoJson(TileOptions.ToArray());
        if (raiseReferenceCoordinates)
        {
            RaisePropertyChanged(nameof(TilePreviewReferenceLatitude));
            RaisePropertyChanged(nameof(TilePreviewReferenceLongitude));
            RaisePropertyChanged(nameof(TilePreviewReferenceTitle));
        }

        RaisePropertyChanged(nameof(TilePreviewStatusText));
    }

    public void SetModelOverlayStatus(string message)
    {
        ModelOverlayStatusMessage = message;
    }

    private string BuildTilePreviewStatusText()
    {
        if (referenceContext is null)
        {
            return "Resolve a readable project reference to show the PLATEAU tile context.";
        }

        if (scanResult is null || TileOptions.Count == 0)
        {
            return $"Reference marker: {TilePreviewReferenceTitle}. Scan a PLATEAU package to preview detected tiles.";
        }

        if (SelectedTileCount == 0)
        {
            return $"Detected {TileOptions.Count} tiles. Click the grid cells you want to import. Marker: {TilePreviewReferenceTitle}.";
        }

        return $"Selected {SelectedTileCount} of {TileOptions.Count} detected tiles. Click a grid cell to toggle it. Marker: {TilePreviewReferenceTitle}.";
    }
    private void ReplaceSelections<T>(ObservableCollection<T> target, IEnumerable<T> values)
        where T : SelectableOptionBase
    {
        foreach (SelectableOptionBase existing in target)
        {
            existing.PropertyChanged -= OnSelectionChanged;
        }

        target.Clear();
        foreach (T value in values)
        {
            value.PropertyChanged += OnSelectionChanged;
            target.Add(value);
        }
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(SelectableOptionBase.IsSelected), StringComparison.Ordinal))
        {
            return;
        }

        ClearPreview(clearWarnings: false);
        RefreshTilePreviewState();
        RaisePropertyChanged(nameof(CanLoadPreview));
        RaisePropertyChanged(nameof(CanExportOutlines));
        RaisePropertyChanged(nameof(SelectedCategoryCount));
        RaisePropertyChanged(nameof(SelectedTileCount));
        RaisePropertyChanged(nameof(TileSelectionHeaderText));
    }

    private void ClearScanAndPreview()
    {
        scanResult = null;
        foreach (SelectableOptionBase existing in FeatureTypeOptions)
        {
            existing.PropertyChanged -= OnSelectionChanged;
        }

        foreach (SelectableOptionBase existing in TileOptions)
        {
            existing.PropertyChanged -= OnSelectionChanged;
        }

        foreach (SelectableOptionBase existing in LandUseClassOptions)
        {
            existing.PropertyChanged -= OnSelectionChanged;
        }

        ReplaceCollection(FeatureTypeOptions, Array.Empty<PlateauFeatureSelectionItem>());
        ReplaceCollection(TileOptions, Array.Empty<PlateauTileSelectionItem>());
        ReplaceCollection(LandUseClassOptions, Array.Empty<PlateauLandUseClassSelectionItem>());
        ReplaceCollection(ScanRows, Array.Empty<DetailRow>());
        ReplaceCollection(DetectedSourceFiles, Array.Empty<string>());
        ResetScanProgress();
        RefreshTilePreviewState(raiseReferenceCoordinates: true);
        ClearPreview(clearWarnings: true);
        RaisePropertyChanged(nameof(HasLandUseClassOptions));
        RaiseScanProperties();
    }

    private void ClearPreview(bool clearWarnings)
    {
        previewRequestVersion = unchecked(previewRequestVersion + 1);
        preparedPlan = null;
        ReplaceCollection(PreviewRows, Array.Empty<DetailRow>());
        ReplaceCollection(FeatureNames, Array.Empty<string>());
        if (clearWarnings)
        {
            ReplaceCollection(WarningMessages, Array.Empty<string>());
        }

        RaisePreviewProperties();
    }

    private static string GetInitialFolderPath(PlateauImportState? importState)
    {
        if (importState is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(importState.LastImportedFolderPath))
        {
            return importState.LastImportedFolderPath;
        }

        return string.IsNullOrWhiteSpace(importState.LastImportedFilePath)
            ? string.Empty
            : Path.GetDirectoryName(importState.LastImportedFilePath) ?? string.Empty;
    }

    private static PlateauImportReferenceSource GetDefaultReferenceSource(CurrentProjectStateSummary currentState, PlateauImportState? importState)
    {
        if (importState is not null)
        {
            return importState.LastReferenceSource;
        }

        return currentState.StoredWorkingProjectBasePoint?.IsValid == true || currentState.ProjectBasePoint.HasEstimatedLocation || currentState.ProjectBasePoint.HasSharedPosition
            ? PlateauImportReferenceSource.WorkingProjectBasePoint
            : PlateauImportReferenceSource.CanonicalOrigin;
    }

    private static PlateauGeometryImportMode GetDefaultGeometryImportMode(PlateauImportState? importState)
    {
        return importState?.LastGeometryImportMode ?? PlateauGeometryImportMode.LightweightExtrusion;
    }

    private static IReadOnlyList<KibanOptionalLandUseSelectionItem> CreateOptionalKibanLandUseOptions()
    {
        return new[]
        {
            new KibanOptionalLandUseSelectionItem
            {
                Token = "公園",
                Title = "公園 (Park)",
                IsSelected = false
            },
            new KibanOptionalLandUseSelectionItem
            {
                Token = "園地",
                Title = "園地 (Garden grounds)",
                IsSelected = false
            },
            new KibanOptionalLandUseSelectionItem
            {
                Token = "荒地",
                Title = "荒地 (Wasteland)",
                IsSelected = false
            }
        };
    }

    private void OnOptionalKibanLandUseChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(SelectableOptionBase.IsSelected), StringComparison.Ordinal))
        {
            return;
        }

        ClearPreview(clearWarnings: false);
        RaisePropertyChanged(nameof(CanLoadPreview));
        RaisePropertyChanged(nameof(CanExportOutlines));
    }

    internal IReadOnlyList<string> GetSelectedOptionalKibanLandUseTokens()
    {
        return OptionalKibanLandUseOptions
            .Where(option => option.IsSelected && !string.IsNullOrEmpty(option.Token))
            .Select(option => option.Token)
            .ToArray();
    }

    private static readonly string[] GreenPlateauLandUseTokens =
    {
        "緑地",
        "緑道",
        "樹林",
        "森林",
        "山林",
        "田",
        "畑",
        "農林漁業"
    };

    internal static bool IsGreenPlateauLandUseClassName(string className)
    {
        if (string.IsNullOrEmpty(className))
        {
            return false;
        }

        foreach (string token in GreenPlateauLandUseTokens)
        {
            if (className.IndexOf(token, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyCollection<PlateauImportReferenceSourceOption> CreateReferenceSourceOptions()
    {
        return new[]
        {
            new PlateauImportReferenceSourceOption
            {
                Source = PlateauImportReferenceSource.WorkingProjectBasePoint,
                Title = "Project Base Point",
                Description = "Uses the current Revit Project Base Point estimate when available, otherwise falls back to the saved Working Project Base Point from georeference module state. This is the preferred local reference for PLATEAU context import."
            },
            new PlateauImportReferenceSourceOption
            {
                Source = PlateauImportReferenceSource.CanonicalOrigin,
                Title = "Canonical Origin",
                Description = "Uses the shared canonical origin from GeoProjectInfo. This is the stable fallback when a Project Base Point reference is not available or not desired."
            }
        };
    }

    private static IReadOnlyCollection<PlateauGeometryImportModeOption> CreateGeometryImportModeOptions()
    {
        return new[]
        {
            new PlateauGeometryImportModeOption
            {
                Mode = PlateauGeometryImportMode.LightweightExtrusion,
                Title = "Lightweight Geometry",
                Description = "Builds fast context extrusions from the selected feature footprint and height. Use this when you want a lighter Revit reference model."
            },
            new PlateauGeometryImportModeOption
            {
                Mode = PlateauGeometryImportMode.DetailedDirectShape,
                Title = "Detailed Geometry",
                Description = "Imports the highest-LOD CityGML surfaces as faceted DirectShape geometry so roof forms and non-extruded shapes are preserved. This mode creates heavier Revit geometry."
            },
            new PlateauGeometryImportModeOption
            {
                Mode = PlateauGeometryImportMode.LightweightMassOnRelief,
                Title = "Lightweight Mass on Relief",
                Description = "Extrudes building footprints from the elevation sampled at the parsed CityGML Relief (TIN) surface. Falls back to the building's own min-Z if the package contains no Relief features."
            }
        };
    }

    private static string FormatReferenceSource(PlateauImportReferenceSource referenceSource)
    {
        return referenceSource == PlateauImportReferenceSource.WorkingProjectBasePoint
            ? "Project Base Point"
            : "Canonical Origin";
    }

    private void RaiseReferenceProperties()
    {
        RaisePropertyChanged(nameof(ReferenceSourceTitle));
        RaisePropertyChanged(nameof(ReferenceSourceDescription));
        RaisePropertyChanged(nameof(CanLoadPreview));
        RaisePropertyChanged(nameof(CanExportOutlines));
        RaisePropertyChanged(nameof(HasSuggestedTiles));
        RaisePropertyChanged(nameof(HasNoSuggestedTiles));
    }

    private void RaiseScanProperties()
    {
        RaisePropertyChanged(nameof(CanLoadPreview));
        RaisePropertyChanged(nameof(CanExportOutlines));
        RaisePropertyChanged(nameof(HasScanRows));
        RaisePropertyChanged(nameof(HasNoScanRows));
        RaisePropertyChanged(nameof(HasDetectedSourceFiles));
        RaisePropertyChanged(nameof(HasNoDetectedSourceFiles));
        RaisePropertyChanged(nameof(HasFeatureTypeOptions));
        RaisePropertyChanged(nameof(HasNoFeatureTypeOptions));
        RaisePropertyChanged(nameof(HasTileOptions));
        RaisePropertyChanged(nameof(HasNoTileOptions));
        RaisePropertyChanged(nameof(SelectedCategoryCount));
        RaisePropertyChanged(nameof(SelectedTileCount));
        RaisePropertyChanged(nameof(TotalTileCount));
        RaisePropertyChanged(nameof(TileSelectionHeaderText));
        RaisePropertyChanged(nameof(HasWarningMessages));
        RaisePropertyChanged(nameof(HasNoWarningMessages));
        RaisePropertyChanged(nameof(HasDeterminateScanProgress));
        RaisePropertyChanged(nameof(HasScanProgressStatusText));
    }

    private void ResetScanProgress()
    {
        ScanProgressCurrent = 0;
        ScanProgressTotal = 0;
        ScanProgressPercent = 0d;
        IsScanProgressIndeterminate = false;
        ScanProgressStatusText = string.Empty;
    }

    private void RaisePreviewProperties()
    {
        RaisePropertyChanged(nameof(CanImport));
        RaisePropertyChanged(nameof(PreparedShapeCount));
        RaisePropertyChanged(nameof(PreparedSurfaceCount));
        RaisePropertyChanged(nameof(PreparedTriangleCount));
        RaisePropertyChanged(nameof(HasPreviewRows));
        RaisePropertyChanged(nameof(HasNoPreviewRows));
        RaisePropertyChanged(nameof(HasFeatureNames));
        RaisePropertyChanged(nameof(HasNoFeatureNames));
        RaisePropertyChanged(nameof(HasWarningMessages));
        RaisePropertyChanged(nameof(HasNoWarningMessages));
    }

    private static IReadOnlyCollection<string> BuildRelativePathList(string baseFolder, IReadOnlyCollection<string> sourcePaths, int maxDisplayedPaths)
    {
        List<string> relativePaths = sourcePaths
            .Select(path => ToRelativeDisplayPath(baseFolder, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(maxDisplayedPaths)
            .ToList();

        if (sourcePaths.Count > relativePaths.Count)
        {
            relativePaths.Add($"... and {sourcePaths.Count - relativePaths.Count} more file(s)");
        }

        return relativePaths;
    }

    private static string BuildRelativeFileSummary(string baseFolder, IReadOnlyCollection<string> sourcePaths, int maxDisplayedPaths)
    {
        if (sourcePaths.Count == 0)
        {
            return string.Empty;
        }

        List<string> relativePaths = sourcePaths
            .Select(path => ToRelativeDisplayPath(baseFolder, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(maxDisplayedPaths)
            .ToList();
        if (sourcePaths.Count > relativePaths.Count)
        {
            relativePaths.Add($"and {sourcePaths.Count - relativePaths.Count} more");
        }

        return string.Join(", ", relativePaths);
    }

    private static string ToRelativeDisplayPath(string baseFolder, string path)
    {
        try
        {
            string normalizedBaseFolder = EnsureTrailingSeparator(Path.GetFullPath(baseFolder));
            string normalizedPath = Path.GetFullPath(path);
            Uri baseUri = new Uri(normalizedBaseFolder, UriKind.Absolute);
            Uri pathUri = new Uri(normalizedPath, UriKind.Absolute);
            string relativePath = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
            return relativePath.Replace('/', Path.DirectorySeparatorChar);
        }
        catch
        {
            return path;
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
        {
            target.Add(value);
        }
    }

    internal sealed class PreviewBuildRequest
    {
        public PreviewBuildRequest(
            int requestVersion,
            PlateauFolderScanResult scanResult,
            PlateauImportReferenceContext referenceContext,
            IReadOnlyCollection<PlateauFeatureType> selectedFeatureTypes,
            IReadOnlyCollection<string> selectedTileIds,
            PlateauGeometryImportMode geometryImportMode)
        {
            RequestVersion = requestVersion;
            ScanResult = scanResult;
            ReferenceContext = referenceContext;
            SelectedFeatureTypes = selectedFeatureTypes;
            SelectedTileIds = selectedTileIds;
            GeometryImportMode = geometryImportMode;
        }

        public int RequestVersion { get; }

        public PlateauFolderScanResult ScanResult { get; }

        public PlateauImportReferenceContext ReferenceContext { get; }

        public IReadOnlyCollection<PlateauFeatureType> SelectedFeatureTypes { get; }

        public IReadOnlyCollection<string> SelectedTileIds { get; }

        public PlateauGeometryImportMode GeometryImportMode { get; }
    }

    internal sealed class KibanScanRequest
    {
        public KibanScanRequest(string folderPath, IReadOnlyCollection<string> plateauSecondaryMeshCodes)
            : this(folderPath, plateauSecondaryMeshCodes, additionalGreenLandUseTokens: null)
        {
        }

        public KibanScanRequest(
            string folderPath,
            IReadOnlyCollection<string> plateauSecondaryMeshCodes,
            IReadOnlyCollection<string>? additionalGreenLandUseTokens)
        {
            FolderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
            PlateauSecondaryMeshCodes = plateauSecondaryMeshCodes ?? throw new ArgumentNullException(nameof(plateauSecondaryMeshCodes));
            AdditionalGreenLandUseTokens = additionalGreenLandUseTokens;
        }

        public string FolderPath { get; }

        public IReadOnlyCollection<string> PlateauSecondaryMeshCodes { get; }

        public IReadOnlyCollection<string>? AdditionalGreenLandUseTokens { get; }
    }

    internal sealed class KibanScanResult
    {
        public KibanScanResult(
            IReadOnlyList<KibanParsedFeature> features,
            IReadOnlyList<KibanParsedPolygonFeature> polygonFeatures,
            int parsedFileCount,
            int skippedFileCount,
            bool isFromCache = false)
        {
            Features = features ?? throw new ArgumentNullException(nameof(features));
            PolygonFeatures = polygonFeatures ?? throw new ArgumentNullException(nameof(polygonFeatures));
            ParsedFileCount = parsedFileCount;
            SkippedFileCount = skippedFileCount;
            IsFromCache = isFromCache;
        }

        public IReadOnlyList<KibanParsedFeature> Features { get; }

        public IReadOnlyList<KibanParsedPolygonFeature> PolygonFeatures { get; }

        public int ParsedFileCount { get; }

        public int SkippedFileCount { get; }

        public bool IsFromCache { get; }
    }

    internal sealed class ShapefileExportRequest
    {
        public ShapefileExportRequest(
            PlateauFolderScanResult scanResult,
            PlateauImportReferenceContext referenceContext,
            IReadOnlyCollection<PlateauFeatureType> selectedFeatureTypes,
            IReadOnlyCollection<string> selectedTileIds,
            string kibanFolderPath,
            IReadOnlyList<KibanParsedFeature>? kibanParsedFeatures,
            IReadOnlyList<KibanParsedPolygonFeature>? kibanParsedPolygonFeatures,
            IReadOnlyCollection<string> selectedKibanLayerNames,
            bool hasKibanLayerOptions,
            IReadOnlyCollection<string>? additionalGreenLandUseTokens = null)
        {
            ScanResult = scanResult ?? throw new ArgumentNullException(nameof(scanResult));
            ReferenceContext = referenceContext ?? throw new ArgumentNullException(nameof(referenceContext));
            SelectedFeatureTypes = selectedFeatureTypes ?? throw new ArgumentNullException(nameof(selectedFeatureTypes));
            SelectedTileIds = selectedTileIds ?? throw new ArgumentNullException(nameof(selectedTileIds));
            KibanFolderPath = kibanFolderPath ?? string.Empty;
            KibanParsedFeatures = kibanParsedFeatures;
            KibanParsedPolygonFeatures = kibanParsedPolygonFeatures;
            SelectedKibanLayerNames = selectedKibanLayerNames ?? throw new ArgumentNullException(nameof(selectedKibanLayerNames));
            HasKibanLayerOptions = hasKibanLayerOptions;
            AdditionalGreenLandUseTokens = additionalGreenLandUseTokens;
        }

        public PlateauFolderScanResult ScanResult { get; }

        public PlateauImportReferenceContext ReferenceContext { get; }

        public IReadOnlyCollection<PlateauFeatureType> SelectedFeatureTypes { get; }

        public IReadOnlyCollection<string> SelectedTileIds { get; }

        public string KibanFolderPath { get; }

        public bool HasKibanFolder => !string.IsNullOrWhiteSpace(KibanFolderPath);

        public IReadOnlyList<KibanParsedFeature>? KibanParsedFeatures { get; }

        public IReadOnlyList<KibanParsedPolygonFeature>? KibanParsedPolygonFeatures { get; }

        public IReadOnlyCollection<string> SelectedKibanLayerNames { get; }

        public bool HasKibanLayerOptions { get; }

        public IReadOnlyCollection<string>? AdditionalGreenLandUseTokens { get; }
    }

    internal readonly struct PlateauExportProgress
    {
        public PlateauExportProgress(string stage, string? detail = null)
        {
            Stage = stage ?? string.Empty;
            Detail = detail;
        }

        public string Stage { get; }

        public string? Detail { get; }
    }

    internal sealed class PreviewBuildResult
    {
        public PreviewBuildResult(
            int requestVersion,
            ContextImportPlan plan,
            IReadOnlyCollection<DetailRow> previewRows,
            IReadOnlyCollection<string> featureNames,
            IReadOnlyCollection<string> warningMessages,
            string statusMessage)
        {
            RequestVersion = requestVersion;
            Plan = plan;
            PreviewRows = previewRows;
            FeatureNames = featureNames;
            WarningMessages = warningMessages;
            StatusMessage = statusMessage;
        }

        public int RequestVersion { get; }

        public ContextImportPlan Plan { get; }

        public IReadOnlyCollection<DetailRow> PreviewRows { get; }

        public IReadOnlyCollection<string> FeatureNames { get; }

        public IReadOnlyCollection<string> WarningMessages { get; }

        public string StatusMessage { get; }
    }

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

