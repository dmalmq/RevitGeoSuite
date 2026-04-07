using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DExportViewModel : INotifyPropertyChanged
{
    private readonly CurrentProjectStateSummary currentState;
    private readonly GeoProjectInfo? info;
    private readonly Tiles3DExportReferenceResolver referenceResolver;
    private Tiles3DExportState? exportState;
    private Tiles3DExportReferenceContext? referenceContext;
    private Tiles3DExportPackage? preparedPackage;
    private string actionMessage;
    private string outputDirectory;
    private string statusMessage;
    private Tiles3DLevelOfDetailOption? selectedLevelOfDetailOption;
    private Tiles3DExportReferenceSourceOption? selectedReferenceSourceOption;

    public Tiles3DExportViewModel(
        CurrentProjectStateSummary currentState,
        GeoProjectInfo? info,
        Tiles3DExportState? exportState,
        Tiles3DExportReferenceResolver referenceResolver)
    {
        this.currentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        this.info = info;
        this.exportState = exportState;
        this.referenceResolver = referenceResolver ?? throw new ArgumentNullException(nameof(referenceResolver));
        actionMessage = BuildInitialActionMessage(exportState);
        outputDirectory = exportState?.LastExportPath ?? string.Empty;
        statusMessage = string.Empty;
        CurrentStateRows = new ObservableCollection<DetailRow>();
        LastExportRows = new ObservableCollection<DetailRow>();
        PreparedRows = new ObservableCollection<DetailRow>();
        FeatureNames = new ObservableCollection<string>();
        ReferenceSourceOptions = new ObservableCollection<Tiles3DExportReferenceSourceOption>(CreateReferenceSourceOptions());
        LevelOfDetailOptions = new ObservableCollection<Tiles3DLevelOfDetailOption>(CreateLevelOfDetailOptions());
        BuildLastExportRows();

        Tiles3DExportReferenceSource defaultSource = GetDefaultReferenceSource(currentState, exportState);
        selectedReferenceSourceOption = ReferenceSourceOptions.First(option => option.Source == defaultSource);
        selectedLevelOfDetailOption = LevelOfDetailOptions.First(option => string.Equals(option.Level.ToString(), exportState?.LastLodSetting ?? Tiles3DLevelOfDetail.Medium.ToString(), StringComparison.OrdinalIgnoreCase));
        RefreshReferenceContext(clearPrepared: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DetailRow> CurrentStateRows { get; }

    public ObservableCollection<DetailRow> LastExportRows { get; }

    public ObservableCollection<DetailRow> PreparedRows { get; }

    public ObservableCollection<string> FeatureNames { get; }

    public ObservableCollection<Tiles3DExportReferenceSourceOption> ReferenceSourceOptions { get; }

    public ObservableCollection<Tiles3DLevelOfDetailOption> LevelOfDetailOptions { get; }

    public string WindowTitle => "3D Tiles Export";

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

    public string OutputDirectory
    {
        get => outputDirectory;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(outputDirectory, normalized, StringComparison.Ordinal))
            {
                return;
            }

            outputDirectory = normalized;
            RaisePropertyChanged(nameof(OutputDirectory));
            RaisePropertyChanged(nameof(CanExport));
        }
    }

    public bool CanPrepareExport => referenceContext is not null;

    public bool CanExport => preparedPackage is not null && !string.IsNullOrWhiteSpace(OutputDirectory);

    public Tiles3DExportState? ExportState => exportState;

    public Tiles3DExportPackage? PreparedPackage => preparedPackage;

    public Tiles3DLevelOfDetail SelectedLevelOfDetail => SelectedLevelOfDetailOption?.Level ?? Tiles3DLevelOfDetail.Medium;

    public Tiles3DLevelOfDetailOption? SelectedLevelOfDetailOption
    {
        get => selectedLevelOfDetailOption;
        set
        {
            if (selectedLevelOfDetailOption == value || value is null)
            {
                return;
            }

            selectedLevelOfDetailOption = value;
            ClearPrepared();
            StatusMessage = BuildBaseStatusMessage();
            RaisePropertyChanged(nameof(SelectedLevelOfDetailOption));
            RaisePropertyChanged(nameof(SelectedLevelOfDetail));
        }
    }

    public Tiles3DExportReferenceSource SelectedReferenceSource => SelectedReferenceSourceOption?.Source ?? Tiles3DExportReferenceSource.WorkingProjectBasePoint;

    public Tiles3DExportReferenceSourceOption? SelectedReferenceSourceOption
    {
        get => selectedReferenceSourceOption;
        set
        {
            if (selectedReferenceSourceOption == value || value is null)
            {
                return;
            }

            selectedReferenceSourceOption = value;
            RefreshReferenceContext(clearPrepared: true);
            RaisePropertyChanged(nameof(SelectedReferenceSourceOption));
            RaisePropertyChanged(nameof(SelectedReferenceSource));
            RaisePropertyChanged(nameof(ReferenceSourceDescription));
        }
    }

    public string ReferenceSourceTitle => referenceContext?.Title ?? SelectedReferenceSourceOption?.Title ?? "Reference unavailable";

    public string ReferenceSourceDescription => referenceContext?.Description ?? SelectedReferenceSourceOption?.Description ?? string.Empty;

    public Tiles3DExportReferenceContext? ResolvedReferenceContext => referenceContext;

    public bool HasLastExportRows => LastExportRows.Count > 0;

    public bool HasNoLastExportRows => !HasLastExportRows;

    public bool HasPreparedRows => PreparedRows.Count > 0;

    public bool HasNoPreparedRows => !HasPreparedRows;

    public bool HasFeatureNames => FeatureNames.Count > 0;

    public bool HasNoFeatureNames => !HasFeatureNames;

    public void MarkPrepared(Tiles3DExportPreparationResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        preparedPackage = result.Package;
        ReplaceCollection(PreparedRows, result.PreparedRows);
        ReplaceCollection(FeatureNames, result.FeatureNames);
        StatusMessage = result.StatusMessage;
        ActionMessage = string.Empty;
        RaisePreparedProperties();
    }

    public void MarkExportSucceeded(Tiles3DExportResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        exportState = result.UpdatedState;
        BuildLastExportRows();
        ActionMessage = result.SummaryMessage;
        StatusMessage = result.StatePersisted
            ? "3D Tiles export finished and the module-specific export state was saved separately from GeoProjectInfo."
            : "3D Tiles export finished, but the Revit document is read-only so the export state could not be saved back into module storage.";
        RaisePropertyChanged(nameof(ExportState));
    }

    private void RefreshReferenceContext(bool clearPrepared)
    {
        referenceContext = referenceResolver.Resolve(currentState, info, SelectedReferenceSource);
        ReplaceCollection(CurrentStateRows, BuildCurrentStateRows(referenceContext));
        if (clearPrepared)
        {
            ClearPrepared();
        }

        StatusMessage = BuildBaseStatusMessage();
        RaisePropertyChanged(nameof(ReferenceSourceTitle));
        RaisePropertyChanged(nameof(ReferenceSourceDescription));
        RaisePropertyChanged(nameof(CanPrepareExport));
    }

    private string BuildBaseStatusMessage()
    {
        if (!currentState.IsSupportedDocument)
        {
            return string.IsNullOrWhiteSpace(currentState.StatusMessage)
                ? "3D Tiles export is not available for this Revit document."
                : currentState.StatusMessage;
        }

        if (info?.ProjectCrs is null || info.Origin is null)
        {
            return "Shared geo metadata is missing or incomplete. Run Georeference Setup before exporting 3D Tiles.";
        }

        if (referenceContext is null)
        {
            return SelectedReferenceSource == Tiles3DExportReferenceSource.WorkingProjectBasePoint
                ? "No Project Base Point reference is available yet. Save a working Project Base Point in Georeference Setup or switch to Canonical Origin."
                : "The selected export reference could not be resolved from the current shared geo state.";
        }

        return currentState.IsReadOnly
            ? "This Revit project is read-only. Export is still available, but the last export state cannot be saved back into module storage."
            : "Prepare an export package, choose an output directory, and then write a viewer-oriented 3D Tiles bundle.";
    }

    private IReadOnlyCollection<DetailRow> BuildCurrentStateRows(Tiles3DExportReferenceContext? resolvedReference)
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
            new DetailRow("Reference Location", resolvedReference is null ? "Unavailable" : $"{resolvedReference.AnchorLatitude:F6}, {resolvedReference.AnchorLongitude:F6}, elev {resolvedReference.AnchorElevationMeters:F3} m"),
            new DetailRow("Reference Projected", resolvedReference is null ? "Unavailable" : $"E {resolvedReference.AnchorProjectedCoordinate.Easting:F3} m, N {resolvedReference.AnchorProjectedCoordinate.Northing:F3} m"),
            new DetailRow("Local Anchor", resolvedReference is null ? "Unavailable" : $"X {resolvedReference.AnchorXFeet:F3} ft, Y {resolvedReference.AnchorYFeet:F3} ft, Z {resolvedReference.AnchorZFeet:F3} ft"),
            new DetailRow("Working Project Base Point", currentState.StoredWorkingProjectBasePoint?.IsValid == true ? "Saved" : "Not saved"),
            new DetailRow("Revit Project Base Point Estimate", currentState.ProjectBasePoint.HasEstimatedLocation ? "Available" : "Not available")
        };
    }

    private static string BuildInitialActionMessage(Tiles3DExportState? exportState)
    {
        if (exportState is null)
        {
            return string.Empty;
        }

        string pathText = string.IsNullOrWhiteSpace(exportState.LastExportPath) ? "previous folder" : exportState.LastExportPath;
        string dateText = exportState.LastExportDateUtc.HasValue
            ? exportState.LastExportDateUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "an earlier session";
        return $"Last 3D Tiles export restored: '{pathText}' with {exportState.LastLodSetting} LOD on {dateText}. Prepare Export to refresh the package in the current session.";
    }

    private void BuildLastExportRows()
    {
        if (exportState is null)
        {
            ReplaceCollection(LastExportRows, Array.Empty<DetailRow>());
            RaisePropertyChanged(nameof(HasLastExportRows));
            RaisePropertyChanged(nameof(HasNoLastExportRows));
            return;
        }

        ReplaceCollection(LastExportRows, new[]
        {
            new DetailRow("Last Export Path", string.IsNullOrWhiteSpace(exportState.LastExportPath) ? "Not recorded" : exportState.LastExportPath),
            new DetailRow("Last Export Date", exportState.LastExportDateUtc.HasValue ? exportState.LastExportDateUtc.Value.ToString("u", CultureInfo.InvariantCulture) : "Not recorded"),
            new DetailRow("Last Reference", FormatReferenceSource(exportState.LastReferenceSource)),
            new DetailRow("Last LOD", exportState.LastLodSetting),
            new DetailRow("Last Exported Elements", exportState.LastExportedElementCount.ToString(CultureInfo.InvariantCulture)),
            new DetailRow("Last Exported Triangles", exportState.LastExportedTriangleCount.ToString(CultureInfo.InvariantCulture))
        });
        RaisePropertyChanged(nameof(HasLastExportRows));
        RaisePropertyChanged(nameof(HasNoLastExportRows));
    }

    private void ClearPrepared()
    {
        preparedPackage = null;
        ReplaceCollection(PreparedRows, Array.Empty<DetailRow>());
        ReplaceCollection(FeatureNames, Array.Empty<string>());
        RaisePreparedProperties();
    }

    private static Tiles3DExportReferenceSource GetDefaultReferenceSource(CurrentProjectStateSummary currentState, Tiles3DExportState? exportState)
    {
        if (exportState is not null)
        {
            return exportState.LastReferenceSource;
        }

        return currentState.StoredWorkingProjectBasePoint?.IsValid == true || currentState.ProjectBasePoint.HasEstimatedLocation
            ? Tiles3DExportReferenceSource.WorkingProjectBasePoint
            : Tiles3DExportReferenceSource.CanonicalOrigin;
    }

    private static IReadOnlyCollection<Tiles3DExportReferenceSourceOption> CreateReferenceSourceOptions()
    {
        return new[]
        {
            new Tiles3DExportReferenceSourceOption
            {
                Source = Tiles3DExportReferenceSource.WorkingProjectBasePoint,
                Title = "Working Project Base Point",
                Description = "Uses the saved Working Project Base Point when available, otherwise falls back to the current Revit Project Base Point estimate. This is the preferred local reference for viewer exports."
            },
            new Tiles3DExportReferenceSourceOption
            {
                Source = Tiles3DExportReferenceSource.CanonicalOrigin,
                Title = "Canonical Origin",
                Description = "Uses the shared canonical origin from GeoProjectInfo. This is the stable fallback when a Project Base Point reference is not available or not desired."
            }
        };
    }

    private static IReadOnlyCollection<Tiles3DLevelOfDetailOption> CreateLevelOfDetailOptions()
    {
        return new[]
        {
            new Tiles3DLevelOfDetailOption
            {
                Level = Tiles3DLevelOfDetail.Coarse,
                Title = "Coarse",
                Description = "Keeps a sparse subset of triangles for lightweight context viewing."
            },
            new Tiles3DLevelOfDetailOption
            {
                Level = Tiles3DLevelOfDetail.Medium,
                Title = "Medium",
                Description = "Balances size and fidelity for quick viewer validation."
            },
            new Tiles3DLevelOfDetailOption
            {
                Level = Tiles3DLevelOfDetail.Fine,
                Title = "Fine",
                Description = "Keeps all extracted triangles for the highest fidelity in V1."
            }
        };
    }

    private static string FormatReferenceSource(Tiles3DExportReferenceSource referenceSource)
    {
        return referenceSource == Tiles3DExportReferenceSource.WorkingProjectBasePoint
            ? "Working Project Base Point"
            : "Canonical Origin";
    }

    private void RaisePreparedProperties()
    {
        RaisePropertyChanged(nameof(CanExport));
        RaisePropertyChanged(nameof(PreparedPackage));
        RaisePropertyChanged(nameof(HasPreparedRows));
        RaisePropertyChanged(nameof(HasNoPreparedRows));
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
