using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.Plateau.Tiles;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportViewModel : INotifyPropertyChanged
{
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
    private bool showModelOverlay;
    private PlateauImportReferenceSourceOption? selectedReferenceSourceOption;

    public PlateauImportViewModel(
        CurrentProjectStateSummary currentState,
        GeoProjectInfo? info,
        PlateauImportState? importState,
        PlateauImportReferenceResolver referenceResolver,
        PlateauTileIndex tileIndex,
        PlateauFolderScanService folderScanService,
        ContextGeometryBuilder geometryBuilder,
        PlateauTileOverlayService? tileOverlayService = null)
    {
        this.currentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        this.info = info;
        this.importState = importState;
        this.referenceResolver = referenceResolver ?? throw new ArgumentNullException(nameof(referenceResolver));
        this.tileIndex = tileIndex ?? throw new ArgumentNullException(nameof(tileIndex));
        this.folderScanService = folderScanService ?? throw new ArgumentNullException(nameof(folderScanService));
        this.geometryBuilder = geometryBuilder ?? throw new ArgumentNullException(nameof(geometryBuilder));
        this.tileOverlayService = tileOverlayService ?? new PlateauTileOverlayService(new JapanMeshCalculator());
        actionMessage = BuildInitialActionMessage(importState);
        tilePreviewGeoJson = string.Empty;
        selectedFolderPath = GetInitialFolderPath(importState);
        statusMessage = string.Empty;
        modelOverlayStatusMessage = "Resolve the import reference to show the host-model overlay.";
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
        ReferenceSourceOptions = new ObservableCollection<PlateauImportReferenceSourceOption>(CreateReferenceSourceOptions());
        BuildLastImportRows();

        PlateauImportReferenceSource defaultSource = GetDefaultReferenceSource(currentState, importState);
        selectedReferenceSourceOption = ReferenceSourceOptions.First(option => option.Source == defaultSource);
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

    public ObservableCollection<PlateauImportReferenceSourceOption> ReferenceSourceOptions { get; }

    public string WindowTitle => "PLATEAU Context Import";

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

    public bool CanScanFolder => !string.IsNullOrWhiteSpace(SelectedFolderPath);

    public bool CanLoadPreview => referenceContext is not null
        && scanResult is not null
        && FeatureTypeOptions.Any(option => option.IsSelected)
        && TileOptions.Any(option => option.IsSelected);

    public bool CanImport => preparedPlan is not null && currentState.IsSupportedDocument && !currentState.IsReadOnly;

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

    public int PreparedSolidCount => preparedPlan?.Solids.Count ?? 0;

    public int SelectedCategoryCount => FeatureTypeOptions.Count(option => option.IsSelected);

    public int SelectedTileCount => TileOptions.Count(option => option.IsSelected);

    public PlateauImportState? ImportState => importState;

    public PlateauImportReferenceContext? CurrentReferenceContext => referenceContext;

    public ContextImportPlan? PreparedPlan => preparedPlan;

    public PlateauImportReferenceSource SelectedReferenceSource => SelectedReferenceSourceOption?.Source ?? PlateauImportReferenceSource.WorkingProjectBasePoint;

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

    public string ReferenceSourceTitle => referenceContext?.Title ?? SelectedReferenceSourceOption?.Title ?? "Reference unavailable";

    public string ReferenceSourceDescription => referenceContext?.Description ?? SelectedReferenceSourceOption?.Description ?? string.Empty;

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
        ActionMessage = string.Empty;

        if (!CanScanFolder)
        {
            StatusMessage = "Choose a PLATEAU folder before scanning.";
            return false;
        }

        try
        {
            scanResult = folderScanService.ScanFolder(SelectedFolderPath);
            PopulateSelections(scanResult);
            ReplaceCollection(ScanRows, BuildScanRows(scanResult));
            ReplaceCollection(DetectedSourceFiles, BuildDetectedSourceFiles(scanResult));
            ReplaceCollection(WarningMessages, scanResult.WarningMessages);
            ClearPreview(clearWarnings: false);
            if (scanResult.CityModels.Count == 0)
            {
                StatusMessage = scanResult.IsRecursivePackageScan
                    ? "The selected PLATEAU package root was scanned, but no supported PLATEAU features were found under udx."
                    : "The selected folder was scanned, but no supported PLATEAU features were found in the selected folder files.";
            }
            else
            {
                string scanMode = scanResult.IsRecursivePackageScan ? "package root" : "selected folder";
                StatusMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "Scanned {0} supported file(s) from the {1}. Choose categories and click tiles on the map preview, then load a preview.",
                    scanResult.SupportedFilePaths.Count,
                    scanMode);
            }

            RaiseScanProperties();
            return true;
        }
        catch (Exception ex)
        {
            ClearScanAndPreview();
            StatusMessage = ex.Message;
            return false;
        }
    }

    public bool TryLoadPreview()
    {
        ActionMessage = string.Empty;

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

        try
        {
            preparedPlan = geometryBuilder.BuildPlan(scanResult, referenceContext, selectedFeatureTypes, selectedTileIds);
            ReplaceCollection(PreviewRows, BuildPreviewRows(preparedPlan));
            ReplaceCollection(FeatureNames, BuildFeatureNames(preparedPlan));
            ReplaceCollection(WarningMessages, scanResult.WarningMessages.Concat(preparedPlan.WarningMessages).Distinct(StringComparer.Ordinal));
            StatusMessage = currentState.IsReadOnly
                ? string.Format(CultureInfo.InvariantCulture, "Preview loaded. {0} context shapes are ready, but this Revit project is read-only so import is disabled until the model is editable.", PreparedSolidCount)
                : string.Format(CultureInfo.InvariantCulture, "Preview loaded. {0} context shapes are ready to import using {1}.", PreparedSolidCount, referenceContext.Title);
            RaisePreviewProperties();
            return true;
        }
        catch (Exception ex)
        {
            ClearPreview(clearWarnings: false);
            StatusMessage = ex.Message;
            return false;
        }
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

        RefreshTilePreviewState();
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

        return scanResult is null
            ? "Choose a PLATEAU package root or source folder, scan it, click the tiles you want on the preview map, and then load a filtered preview before importing."
            : "Adjust the category filters and selected tiles, load a preview, and then import the lightweight context geometry.";
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

        return $"Last PLATEAU import restored: '{folderName}' via {FormatReferenceSource(importState.LastReferenceSource)} on {dateText}. Scan Folder to rebuild the preview before importing again.";
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

        return new[]
        {
            new DetailRow("Source Folder", plan.SourceFolderPath),
            new DetailRow("Selected Categories", categorySummary),
            new DetailRow("Selected Tiles", tileSummary),
            new DetailRow("Source Features", plan.SourceFeatureCount.ToString(CultureInfo.InvariantCulture)),
            new DetailRow("Importable Solids", plan.Solids.Count.ToString(CultureInfo.InvariantCulture)),
            new DetailRow("Import Reference", plan.ReferenceContext.Title),
            new DetailRow("Reference CRS", $"EPSG:{plan.ReferenceContext.ProjectCrs.EpsgCode}  {plan.ReferenceContext.ProjectCrs.NameSnapshot}"),
            new DetailRow("Reference Elevation", $"{plan.ReferenceContext.AnchorElevationMeters:F3} m")
        };
    }

    private static IReadOnlyCollection<string> BuildFeatureNames(ContextImportPlan plan)
    {
        List<string> names = plan.Solids
            .Select(solid => $"[{solid.TileId}] {solid.FeatureType.GetDisplayName()}: {solid.DisplayName}")
            .Take(40)
            .ToList();

        if (plan.Solids.Count > names.Count)
        {
            names.Add($"... and {plan.Solids.Count - names.Count} more");
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

        ReplaceSelections(FeatureTypeOptions, featureSelections);
        ReplaceSelections(TileOptions, tileSelections);
        RefreshTilePreviewState();
        RaisePropertyChanged(nameof(HasFeatureTypeOptions));
        RaisePropertyChanged(nameof(HasNoFeatureTypeOptions));
        RaisePropertyChanged(nameof(HasTileOptions));
        RaisePropertyChanged(nameof(HasNoTileOptions));
        RaisePropertyChanged(nameof(SelectedCategoryCount));
        RaisePropertyChanged(nameof(SelectedTileCount));
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

    private void RefreshTilePreviewState()
    {
        TilePreviewGeoJson = TileOptions.Count == 0
            ? string.Empty
            : tileOverlayService.CreateGeoJson(TileOptions.ToArray());
        RaisePropertyChanged(nameof(TilePreviewReferenceLatitude));
        RaisePropertyChanged(nameof(TilePreviewReferenceLongitude));
        RaisePropertyChanged(nameof(TilePreviewReferenceTitle));
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
        RaisePropertyChanged(nameof(SelectedCategoryCount));
        RaisePropertyChanged(nameof(SelectedTileCount));
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

        ReplaceCollection(FeatureTypeOptions, Array.Empty<PlateauFeatureSelectionItem>());
        ReplaceCollection(TileOptions, Array.Empty<PlateauTileSelectionItem>());
        ReplaceCollection(ScanRows, Array.Empty<DetailRow>());
        ReplaceCollection(DetectedSourceFiles, Array.Empty<string>());
        RefreshTilePreviewState();
        ClearPreview(clearWarnings: true);
        RaiseScanProperties();
    }

    private void ClearPreview(bool clearWarnings)
    {
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
        RaisePropertyChanged(nameof(HasSuggestedTiles));
        RaisePropertyChanged(nameof(HasNoSuggestedTiles));
    }

    private void RaiseScanProperties()
    {
        RaisePropertyChanged(nameof(CanLoadPreview));
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
        RaisePropertyChanged(nameof(HasWarningMessages));
        RaisePropertyChanged(nameof(HasNoWarningMessages));
    }

    private void RaisePreviewProperties()
    {
        RaisePropertyChanged(nameof(CanImport));
        RaisePropertyChanged(nameof(PreparedSolidCount));
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

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}












