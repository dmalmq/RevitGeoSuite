using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
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
    private readonly CityGmlParser cityGmlParser;
    private readonly ContextGeometryBuilder geometryBuilder;
    private PlateauImportState? importState;
    private PlateauImportReferenceContext? referenceContext;
    private ContextImportPlan? preparedPlan;
    private string actionMessage;
    private string selectedFilePath;
    private string statusMessage;
    private PlateauImportReferenceSourceOption? selectedReferenceSourceOption;

    public PlateauImportViewModel(
        CurrentProjectStateSummary currentState,
        GeoProjectInfo? info,
        PlateauImportState? importState,
        PlateauImportReferenceResolver referenceResolver,
        PlateauTileIndex tileIndex,
        CityGmlParser cityGmlParser,
        ContextGeometryBuilder geometryBuilder)
    {
        this.currentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        this.info = info;
        this.importState = importState;
        this.referenceResolver = referenceResolver ?? throw new ArgumentNullException(nameof(referenceResolver));
        this.tileIndex = tileIndex ?? throw new ArgumentNullException(nameof(tileIndex));
        this.cityGmlParser = cityGmlParser ?? throw new ArgumentNullException(nameof(cityGmlParser));
        this.geometryBuilder = geometryBuilder ?? throw new ArgumentNullException(nameof(geometryBuilder));
        actionMessage = string.Empty;
        selectedFilePath = importState?.LastImportedFilePath ?? string.Empty;
        statusMessage = string.Empty;
        CurrentStateRows = new ObservableCollection<DetailRow>();
        LastImportRows = new ObservableCollection<DetailRow>();
        FileRows = new ObservableCollection<DetailRow>();
        SuggestedTiles = new ObservableCollection<PlateauTileCandidate>();
        FeatureNames = new ObservableCollection<string>();
        ReferenceSourceOptions = new ObservableCollection<PlateauImportReferenceSourceOption>(CreateReferenceSourceOptions());
        BuildLastImportRows();

        PlateauImportReferenceSource defaultSource = GetDefaultReferenceSource(currentState, importState);
        selectedReferenceSourceOption = ReferenceSourceOptions.First(option => option.Source == defaultSource);
        RefreshReferenceContext(clearPreview: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DetailRow> CurrentStateRows { get; }

    public ObservableCollection<DetailRow> LastImportRows { get; }

    public ObservableCollection<DetailRow> FileRows { get; }

    public ObservableCollection<PlateauTileCandidate> SuggestedTiles { get; }

    public ObservableCollection<string> FeatureNames { get; }

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

    public string SelectedFilePath
    {
        get => selectedFilePath;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(selectedFilePath, normalized, StringComparison.Ordinal))
            {
                return;
            }

            selectedFilePath = normalized;
            ClearPreview();
            RaisePropertyChanged(nameof(SelectedFilePath));
            RaisePropertyChanged(nameof(CanLoadPreview));
        }
    }

    public bool CanLoadPreview => referenceContext is not null && !string.IsNullOrWhiteSpace(SelectedFilePath);

    public bool CanImport => preparedPlan is not null && currentState.IsSupportedDocument && !currentState.IsReadOnly;

    public int PreparedSolidCount => preparedPlan?.Solids.Count ?? 0;

    public PlateauImportState? ImportState => importState;

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

    public bool HasFileRows => FileRows.Count > 0;

    public bool HasNoFileRows => !HasFileRows;

    public bool HasFeatureNames => FeatureNames.Count > 0;

    public bool HasNoFeatureNames => !HasFeatureNames;

    public bool TryLoadPreview()
    {
        ActionMessage = string.Empty;

        if (referenceContext is null)
        {
            StatusMessage = BuildBaseStatusMessage();
            return false;
        }

        if (string.IsNullOrWhiteSpace(SelectedFilePath))
        {
            StatusMessage = "Choose a PLATEAU CityGML file before loading a preview.";
            return false;
        }

        try
        {
            PlateauCityModel cityModel = cityGmlParser.ParseFile(SelectedFilePath);
            preparedPlan = geometryBuilder.BuildPlan(cityModel, referenceContext);
            ReplaceCollection(FileRows, BuildFileRows(cityModel, preparedPlan));
            ReplaceCollection(FeatureNames, BuildFeatureNames(cityModel));
            StatusMessage = currentState.IsReadOnly
                ? $"Preview loaded. {PreparedSolidCount} context shapes are ready, but this Revit project is read-only so import is disabled until the model is editable."
                : $"Preview loaded. {PreparedSolidCount} context shapes are ready to import using {referenceContext.Title}.";
            RaisePreviewProperties();
            return true;
        }
        catch (Exception ex)
        {
            ClearPreview();
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
        ActionMessage = result.SummaryMessage;
        StatusMessage = "PLATEAU context geometry was imported successfully. The module-specific import state was saved separately from GeoProjectInfo.";
        RaisePropertyChanged(nameof(ImportState));
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
            ClearPreview();
        }

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
                ? "No Project Base Point reference is available yet. Save a working Project Base Point in Georeference Setup or switch to Canonical Origin."
                : "The selected import reference could not be resolved from the current shared geo state.";
        }

        return currentState.IsReadOnly
            ? "Preview is available, but importing PLATEAU context requires an editable Revit project."
            : "Choose a PLATEAU CityGML file, load a preview, and then import the lightweight context geometry.";
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
            new DetailRow("Local Anchor", resolvedReference is null ? "Unavailable" : $"X {resolvedReference.AnchorXFeet:F3} ft, Y {resolvedReference.AnchorYFeet:F3} ft, Z {resolvedReference.AnchorZFeet:F3} ft"),
            new DetailRow("Working Project Base Point", currentState.StoredWorkingProjectBasePoint?.IsValid == true ? "Saved" : "Not saved"),
            new DetailRow("Revit Project Base Point Estimate", currentState.ProjectBasePoint.HasEstimatedLocation ? "Available" : "Not available")
        };
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

        string lastFileName = string.IsNullOrWhiteSpace(importState.LastImportedFilePath)
            ? "Not recorded"
            : Path.GetFileName(importState.LastImportedFilePath);
        string tileSummary = importState.ImportedTileIds.Count == 0
            ? "Not recorded"
            : string.Join(", ", importState.ImportedTileIds);

        ReplaceCollection(LastImportRows, new[]
        {
            new DetailRow("Last File", lastFileName),
            new DetailRow("Last File Path", string.IsNullOrWhiteSpace(importState.LastImportedFilePath) ? "Not recorded" : importState.LastImportedFilePath),
            new DetailRow("Last Import Date", importState.LastImportDateUtc.HasValue ? importState.LastImportDateUtc.Value.ToString("u", CultureInfo.InvariantCulture) : "Not recorded"),
            new DetailRow("Last Reference", FormatReferenceSource(importState.LastReferenceSource)),
            new DetailRow("Last Imported Features", importState.LastImportedFeatureCount.ToString(CultureInfo.InvariantCulture)),
            new DetailRow("Tracked Tile Ids", tileSummary)
        });
        RaisePropertyChanged(nameof(HasLastImportRows));
        RaisePropertyChanged(nameof(HasNoLastImportRows));
    }

    private static IReadOnlyCollection<DetailRow> BuildFileRows(PlateauCityModel cityModel, ContextImportPlan plan)
    {
        return new[]
        {
            new DetailRow("Source File", cityModel.SourcePath),
            new DetailRow("Declared SRS", string.IsNullOrWhiteSpace(cityModel.SrsName) ? "Not declared" : cityModel.SrsName),
            new DetailRow("File EPSG", cityModel.EpsgCode.HasValue ? $"EPSG:{cityModel.EpsgCode.Value}" : "Not declared"),
            new DetailRow("File Tile Id", string.IsNullOrWhiteSpace(cityModel.FileTileId) ? "Not detected from file name" : cityModel.FileTileId!),
            new DetailRow("Parsed Buildings", cityModel.Buildings.Count.ToString(CultureInfo.InvariantCulture)),
            new DetailRow("Importable Solids", plan.Solids.Count.ToString(CultureInfo.InvariantCulture)),
            new DetailRow("Import Reference", plan.ReferenceContext.Title),
            new DetailRow("Reference CRS", $"EPSG:{plan.ReferenceContext.ProjectCrs.EpsgCode}  {plan.ReferenceContext.ProjectCrs.NameSnapshot}")
        };
    }

    private static IReadOnlyCollection<string> BuildFeatureNames(PlateauCityModel cityModel)
    {
        List<string> names = cityModel.Buildings
            .Select(building => string.IsNullOrWhiteSpace(building.Name) ? building.Id : building.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(24)
            .ToList();

        if (cityModel.Buildings.Count > names.Count)
        {
            names.Add($"... and {cityModel.Buildings.Count - names.Count} more");
        }

        return names;
    }

    private void ClearPreview()
    {
        preparedPlan = null;
        ReplaceCollection(FileRows, Array.Empty<DetailRow>());
        ReplaceCollection(FeatureNames, Array.Empty<string>());
        RaisePreviewProperties();
    }

    private static PlateauImportReferenceSource GetDefaultReferenceSource(CurrentProjectStateSummary currentState, PlateauImportState? importState)
    {
        if (importState is not null)
        {
            return importState.LastReferenceSource;
        }

        return currentState.StoredWorkingProjectBasePoint?.IsValid == true || currentState.ProjectBasePoint.HasEstimatedLocation
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
                Title = "Working Project Base Point",
                Description = "Uses the saved Working Project Base Point when available, otherwise falls back to the current Revit Project Base Point estimate. This is the preferred local reference for PLATEAU context import."
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
            ? "Working Project Base Point"
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

    private void RaisePreviewProperties()
    {
        RaisePropertyChanged(nameof(CanImport));
        RaisePropertyChanged(nameof(PreparedSolidCount));
        RaisePropertyChanged(nameof(HasFileRows));
        RaisePropertyChanged(nameof(HasNoFileRows));
        RaisePropertyChanged(nameof(HasFeatureNames));
        RaisePropertyChanged(nameof(HasNoFeatureNames));
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
