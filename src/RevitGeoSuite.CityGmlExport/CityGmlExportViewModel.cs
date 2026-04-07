using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlExportViewModel : INotifyPropertyChanged
{
    private readonly CurrentProjectStateSummary currentState;
    private readonly GeoProjectInfo? info;
    private readonly CityGmlExportReferenceResolver referenceResolver;
    private CityGmlExportState? exportState;
    private CityGmlExportReferenceContext? referenceContext;
    private CityGmlExportPackage? preparedPackage;
    private string actionMessage;
    private string outputDirectory;
    private string statusMessage;
    private string buildingCodeOverride;
    private string roadCodeOverride;
    private string vegetationCodeOverride;
    private CityGmlSchemaVersionOption? selectedSchemaVersionOption;
    private CityGmlExportReferenceSourceOption? selectedReferenceSourceOption;
    private CityGmlExportViewOption? selectedViewOption;

    public CityGmlExportViewModel(
        CurrentProjectStateSummary currentState,
        GeoProjectInfo? info,
        CityGmlExportState? exportState,
        CityGmlExportReferenceResolver referenceResolver,
        IReadOnlyCollection<CityGmlExportViewOption>? availableViewOptions = null,
        IReadOnlyCollection<CityGmlExportLinkOption>? linkedModelOptions = null,
        string? activeViewUniqueId = null)
    {
        this.currentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        this.info = info;
        this.exportState = exportState;
        this.referenceResolver = referenceResolver ?? throw new ArgumentNullException(nameof(referenceResolver));
        actionMessage = BuildInitialActionMessage(exportState);
        outputDirectory = exportState?.LastExportPath ?? string.Empty;
        statusMessage = string.Empty;
        buildingCodeOverride = exportState is not null && exportState.CodelistOverrides.TryGetValue(nameof(CityGmlSemanticType.Building), out string buildingCode) ? buildingCode : string.Empty;
        roadCodeOverride = exportState is not null && exportState.CodelistOverrides.TryGetValue(nameof(CityGmlSemanticType.Road), out string roadCode) ? roadCode : string.Empty;
        vegetationCodeOverride = exportState is not null && exportState.CodelistOverrides.TryGetValue(nameof(CityGmlSemanticType.Vegetation), out string vegetationCode) ? vegetationCode : string.Empty;

        CurrentStateRows = new ObservableCollection<DetailRow>();
        LastExportRows = new ObservableCollection<DetailRow>();
        PreparedRows = new ObservableCollection<DetailRow>();
        FeatureNames = new ObservableCollection<string>();
        ValidationMessages = new ObservableCollection<string>();
        ReferenceSourceOptions = new ObservableCollection<CityGmlExportReferenceSourceOption>(CreateReferenceSourceOptions());
        SchemaVersionOptions = new ObservableCollection<CityGmlSchemaVersionOption>(CreateSchemaVersionOptions());
        AvailableViewOptions = new ObservableCollection<CityGmlExportViewOption>(availableViewOptions ?? Array.Empty<CityGmlExportViewOption>());
        LinkedModelOptions = new ObservableCollection<CityGmlExportLinkOption>(linkedModelOptions ?? Array.Empty<CityGmlExportLinkOption>());

        foreach (CityGmlExportLinkOption option in LinkedModelOptions)
        {
            option.PropertyChanged += OnLinkedModelOptionPropertyChanged;
        }

        CityGmlExportReferenceSource defaultSource = GetDefaultReferenceSource(currentState, exportState);
        selectedReferenceSourceOption = ReferenceSourceOptions.FirstOrDefault(option => option.Source == defaultSource) ?? ReferenceSourceOptions.First();
        string schemaVersion = exportState?.TargetSchemaVersion ?? CityGmlExportProfile.LightweightCityGml20;
        selectedSchemaVersionOption = SchemaVersionOptions.FirstOrDefault(option => string.Equals(option.Value, schemaVersion, StringComparison.Ordinal)) ?? SchemaVersionOptions.First();
        selectedViewOption = ResolveDefaultViewOption(exportState, activeViewUniqueId);
        ApplyPersistedLinkedModelSelection(exportState);

        BuildLastExportRows();
        RefreshReferenceContext(clearPrepared: false);
        RefreshScopeDependentRows();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DetailRow> CurrentStateRows { get; }

    public ObservableCollection<DetailRow> LastExportRows { get; }

    public ObservableCollection<DetailRow> PreparedRows { get; }

    public ObservableCollection<string> FeatureNames { get; }

    public ObservableCollection<string> ValidationMessages { get; }

    public ObservableCollection<CityGmlExportReferenceSourceOption> ReferenceSourceOptions { get; }

    public ObservableCollection<CityGmlSchemaVersionOption> SchemaVersionOptions { get; }

    public ObservableCollection<CityGmlExportViewOption> AvailableViewOptions { get; }

    public ObservableCollection<CityGmlExportLinkOption> LinkedModelOptions { get; }

    public string WindowTitle => "CityGML Export";

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

    public string BuildingCodeOverride
    {
        get => buildingCodeOverride;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(buildingCodeOverride, normalized, StringComparison.Ordinal))
            {
                return;
            }

            buildingCodeOverride = normalized;
            ClearPrepared();
            RaisePropertyChanged(nameof(BuildingCodeOverride));
        }
    }

    public string RoadCodeOverride
    {
        get => roadCodeOverride;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(roadCodeOverride, normalized, StringComparison.Ordinal))
            {
                return;
            }

            roadCodeOverride = normalized;
            ClearPrepared();
            RaisePropertyChanged(nameof(RoadCodeOverride));
        }
    }

    public string VegetationCodeOverride
    {
        get => vegetationCodeOverride;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(vegetationCodeOverride, normalized, StringComparison.Ordinal))
            {
                return;
            }

            vegetationCodeOverride = normalized;
            ClearPrepared();
            RaisePropertyChanged(nameof(VegetationCodeOverride));
        }
    }

    public bool CanPrepareExport => referenceContext is not null && SelectedViewOption is not null;

    public bool CanExport => preparedPackage is not null && !preparedPackage.ValidationReport.HasErrors && !string.IsNullOrWhiteSpace(OutputDirectory);

    public CityGmlExportState? ExportState => exportState;

    public CityGmlExportPackage? PreparedPackage => preparedPackage;

    public IReadOnlyDictionary<string, string> CategoryMappingOverrides => exportState?.CategoryMappingOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> CodelistOverrides
    {
        get
        {
            Dictionary<string, string> overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(BuildingCodeOverride))
            {
                overrides[nameof(CityGmlSemanticType.Building)] = BuildingCodeOverride;
            }

            if (!string.IsNullOrWhiteSpace(RoadCodeOverride))
            {
                overrides[nameof(CityGmlSemanticType.Road)] = RoadCodeOverride;
            }

            if (!string.IsNullOrWhiteSpace(VegetationCodeOverride))
            {
                overrides[nameof(CityGmlSemanticType.Vegetation)] = VegetationCodeOverride;
            }

            return overrides;
        }
    }

    public CityGmlSchemaVersionOption? SelectedSchemaVersionOption
    {
        get => selectedSchemaVersionOption;
        set
        {
            if (selectedSchemaVersionOption == value || value is null)
            {
                return;
            }

            selectedSchemaVersionOption = value;
            ClearPrepared();
            StatusMessage = BuildBaseStatusMessage();
            RaisePropertyChanged(nameof(SelectedSchemaVersionOption));
            RaisePropertyChanged(nameof(SelectedSchemaVersion));
            RefreshScopeDependentRows();
        }
    }

    public string SelectedSchemaVersion => SelectedSchemaVersionOption?.Value ?? CityGmlExportProfile.LightweightCityGml20;

    public CityGmlExportReferenceSource SelectedReferenceSource => SelectedReferenceSourceOption?.Source ?? CityGmlExportReferenceSource.WorkingProjectBasePoint;

    public CityGmlExportReferenceSourceOption? SelectedReferenceSourceOption
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

    public CityGmlExportViewOption? SelectedViewOption
    {
        get => selectedViewOption;
        set
        {
            if (selectedViewOption == value)
            {
                return;
            }

            selectedViewOption = value;
            ClearPrepared();
            StatusMessage = BuildBaseStatusMessage();
            RaisePropertyChanged(nameof(SelectedViewOption));
            RaisePropertyChanged(nameof(SelectedViewDescription));
            RaisePropertyChanged(nameof(CanPrepareExport));
            RaisePropertyChanged(nameof(ScopeSelection));
            RefreshScopeDependentRows();
        }
    }

    public string SelectedViewDescription => SelectedViewOption?.Description ?? "Select a non-template 3D view to export only the geometry visible in that view.";

    public string ReferenceSourceTitle => referenceContext?.Title ?? SelectedReferenceSourceOption?.Title ?? "Reference unavailable";

    public string ReferenceSourceDescription => referenceContext?.Description ?? SelectedReferenceSourceOption?.Description ?? string.Empty;

    public CityGmlExportReferenceContext? ResolvedReferenceContext => referenceContext;

    public bool HasLastExportRows => LastExportRows.Count > 0;

    public bool HasNoLastExportRows => !HasLastExportRows;

    public bool HasPreparedRows => PreparedRows.Count > 0;

    public bool HasNoPreparedRows => !HasPreparedRows;

    public bool HasFeatureNames => FeatureNames.Count > 0;

    public bool HasNoFeatureNames => !HasFeatureNames;

    public bool HasValidationMessages => ValidationMessages.Count > 0;

    public bool HasNoValidationMessages => !HasValidationMessages;

    public bool HasAvailableViews => AvailableViewOptions.Count > 0;

    public bool HasNoAvailableViews => !HasAvailableViews;

    public bool HasLinkedModelOptions => LinkedModelOptions.Count > 0;

    public bool HasNoLinkedModelOptions => !HasLinkedModelOptions;

    public int SelectedLinkedModelCount => LinkedModelOptions.Count(option => option.IsSelected);

    public string LinkedModelSummary => BuildLinkedModelSummary(LinkedModelOptions.Where(option => option.IsSelected).Select(option => option.Title));

    public CityGmlExportScopeSelection ScopeSelection => new CityGmlExportScopeSelection
    {
        SelectedView = SelectedViewOption,
        SelectedLinkedModels = LinkedModelOptions.Where(option => option.IsSelected).ToArray()
    };

    public void MarkPrepared(CityGmlExportPreparationResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        preparedPackage = result.Package;
        ReplaceCollection(PreparedRows, result.PreparedRows);
        ReplaceCollection(FeatureNames, result.FeatureNames);
        ReplaceCollection(ValidationMessages, result.ValidationMessages);
        StatusMessage = result.StatusMessage;
        ActionMessage = string.Empty;
        RaisePreparedProperties();
    }

    public void MarkExportSucceeded(CityGmlExportResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        exportState = result.UpdatedState;
        BuildLastExportRows();
        ActionMessage = result.SummaryMessage;
        StatusMessage = result.StatePersisted
            ? "CityGML export finished and the module-specific export state was saved separately from GeoProjectInfo."
            : "CityGML export finished, but the Revit document is read-only so the export state could not be saved back into module storage.";
        RaisePropertyChanged(nameof(ExportState));
    }

    private void RefreshReferenceContext(bool clearPrepared)
    {
        referenceContext = referenceResolver.Resolve(currentState, info, SelectedReferenceSource);
        if (clearPrepared)
        {
            ClearPrepared();
        }

        RefreshScopeDependentRows();
        StatusMessage = BuildBaseStatusMessage();
        RaisePropertyChanged(nameof(ReferenceSourceTitle));
        RaisePropertyChanged(nameof(ReferenceSourceDescription));
        RaisePropertyChanged(nameof(CanPrepareExport));
    }

    private void RefreshScopeDependentRows()
    {
        ReplaceCollection(CurrentStateRows, BuildCurrentStateRows(referenceContext));
        RaisePropertyChanged(nameof(SelectedViewDescription));
        RaisePropertyChanged(nameof(LinkedModelSummary));
        RaisePropertyChanged(nameof(SelectedLinkedModelCount));
        RaisePropertyChanged(nameof(HasAvailableViews));
        RaisePropertyChanged(nameof(HasNoAvailableViews));
        RaisePropertyChanged(nameof(HasLinkedModelOptions));
        RaisePropertyChanged(nameof(HasNoLinkedModelOptions));
        RaisePropertyChanged(nameof(ScopeSelection));
    }

    private string BuildBaseStatusMessage()
    {
        if (!currentState.IsSupportedDocument)
        {
            return string.IsNullOrWhiteSpace(currentState.StatusMessage)
                ? "CityGML export is not available for this Revit document."
                : currentState.StatusMessage;
        }

        if (info?.ProjectCrs is null || info.Origin is null)
        {
            return "Shared geo metadata is missing or incomplete. Run Georeference Setup before exporting CityGML.";
        }

        if (referenceContext is null)
        {
            return SelectedReferenceSource == CityGmlExportReferenceSource.WorkingProjectBasePoint
                ? "No Project Base Point reference is available yet. Save a working Project Base Point in Georeference Setup or switch to Canonical Origin."
                : "The selected export reference could not be resolved from the current shared geo state.";
        }

        if (!HasAvailableViews)
        {
            return "No non-template 3D view is available. Create or open a 3D view before preparing CityGML export.";
        }

        if (SelectedViewOption is null)
        {
            return "Select a non-template 3D view. Only model geometry visible in that view will be exported.";
        }

        return currentState.IsReadOnly
            ? "This Revit project is read-only. Export is still available, but the last export state cannot be saved back into module storage."
            : "Prepare a CityGML package from the selected 3D view, optionally include checked linked models, review the semantic and validation summary, and then write city-model.gml.";
    }

    private static string BuildInitialActionMessage(CityGmlExportState? exportState)
    {
        if (exportState is null || string.IsNullOrWhiteSpace(exportState.LastExportPath))
        {
            return "Prepare an export package first. The module writes a lightweight CityGML 2.0 profile and keeps export settings separate from GeoProjectInfo.";
        }

        string viewText = string.IsNullOrWhiteSpace(exportState.LastViewName)
            ? string.Empty
            : $" Last export view: '{exportState.LastViewName}'.";
        return $"Previous CityGML export settings were restored from module state. Last export path: '{exportState.LastExportPath}'.{viewText}";
    }

    private void BuildLastExportRows()
    {
        List<DetailRow> rows = new List<DetailRow>();
        if (exportState is not null)
        {
            rows.Add(new DetailRow("Last Path", exportState.LastExportPath));
            rows.Add(new DetailRow("Last Schema", exportState.TargetSchemaVersion));
            rows.Add(new DetailRow("Last Reference", exportState.LastReferenceSource == CityGmlExportReferenceSource.WorkingProjectBasePoint ? "Working Project Base Point" : "Canonical Origin"));
            if (!string.IsNullOrWhiteSpace(exportState.LastViewName))
            {
                rows.Add(new DetailRow("Last View", exportState.LastViewName));
            }

            rows.Add(new DetailRow("Last Linked Models", BuildLinkedModelSummary(exportState.LastSelectedLinkNames)));
            if (exportState.LastExportDateUtc.HasValue)
            {
                rows.Add(new DetailRow("Last Exported", exportState.LastExportDateUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")));
            }

            rows.Add(new DetailRow("Last Feature Count", exportState.LastExportedFeatureCount.ToString()));
        }

        ReplaceCollection(LastExportRows, rows);
        RaisePropertyChanged(nameof(HasLastExportRows));
        RaisePropertyChanged(nameof(HasNoLastExportRows));
    }

    private IReadOnlyCollection<DetailRow> BuildCurrentStateRows(CityGmlExportReferenceContext? context)
    {
        if (context is null)
        {
            return new[]
            {
                new DetailRow("Shared Geo State", "Missing or unresolved"),
                new DetailRow("Required Action", "Run Georeference Setup and save shared CRS + origin before preparing CityGML export."),
                new DetailRow("Export View", SelectedViewOption?.Title ?? "Not selected"),
                new DetailRow("Linked Models", LinkedModelSummary)
            };
        }

        return new[]
        {
            new DetailRow("Reference", context.Title),
            new DetailRow("Reference CRS", $"EPSG:{context.ProjectCrs.EpsgCode}  {context.ProjectCrs.NameSnapshot}"),
            new DetailRow("Anchor Geographic", $"{context.AnchorLatitude:F6}, {context.AnchorLongitude:F6}"),
            new DetailRow("Anchor Projected", $"{context.AnchorProjectedCoordinate.Easting:F3}, {context.AnchorProjectedCoordinate.Northing:F3}"),
            new DetailRow("Anchor Elevation", $"{context.AnchorElevationMeters:F3} m"),
            new DetailRow("Export View", SelectedViewOption?.Title ?? "Not selected"),
            new DetailRow("Linked Models", LinkedModelSummary),
            new DetailRow("Target Profile", SelectedSchemaVersion)
        };
    }

    private static IReadOnlyCollection<CityGmlExportReferenceSourceOption> CreateReferenceSourceOptions()
    {
        return new[]
        {
            new CityGmlExportReferenceSourceOption
            {
                Source = CityGmlExportReferenceSource.WorkingProjectBasePoint,
                Title = "Working Project Base Point",
                Description = "Preferred when the model has a practical local working reference that should drive CityGML export coordinates."
            },
            new CityGmlExportReferenceSourceOption
            {
                Source = CityGmlExportReferenceSource.CanonicalOrigin,
                Title = "Canonical Origin",
                Description = "Use the canonical stored survey-based origin from GeoProjectInfo as the CityGML export anchor."
            }
        };
    }

    private static IReadOnlyCollection<CityGmlSchemaVersionOption> CreateSchemaVersionOptions()
    {
        return new[]
        {
            new CityGmlSchemaVersionOption
            {
                Value = CityGmlExportProfile.LightweightCityGml20,
                Title = "CityGML 2.0 Lightweight",
                Description = "Writes a lightweight CityGML 2.0-oriented profile with semantic type mapping, generic Revit attributes, and profile validation."
            }
        };
    }

    private static CityGmlExportReferenceSource GetDefaultReferenceSource(CurrentProjectStateSummary currentState, CityGmlExportState? exportState)
    {
        if (exportState is not null)
        {
            return exportState.LastReferenceSource;
        }

        return currentState.StoredWorkingProjectBasePoint?.IsValid == true
            ? CityGmlExportReferenceSource.WorkingProjectBasePoint
            : CityGmlExportReferenceSource.CanonicalOrigin;
    }

    private CityGmlExportViewOption? ResolveDefaultViewOption(CityGmlExportState? state, string? activeViewUniqueId)
    {
        if (state is not null && !string.IsNullOrWhiteSpace(state.LastViewUniqueId))
        {
            CityGmlExportViewOption? persisted = AvailableViewOptions.FirstOrDefault(option => string.Equals(option.UniqueId, state.LastViewUniqueId, StringComparison.Ordinal));
            if (persisted is not null)
            {
                return persisted;
            }
        }

        if (!string.IsNullOrWhiteSpace(activeViewUniqueId))
        {
            CityGmlExportViewOption? active = AvailableViewOptions.FirstOrDefault(option => string.Equals(option.UniqueId, activeViewUniqueId, StringComparison.Ordinal));
            if (active is not null)
            {
                return active;
            }
        }

        return AvailableViewOptions.FirstOrDefault();
    }

    private void ApplyPersistedLinkedModelSelection(CityGmlExportState? state)
    {
        if (state is null || state.LastSelectedLinkUniqueIds.Count == 0)
        {
            return;
        }

        HashSet<string> selectedIds = new HashSet<string>(state.LastSelectedLinkUniqueIds, StringComparer.Ordinal);
        foreach (CityGmlExportLinkOption option in LinkedModelOptions)
        {
            option.IsSelected = selectedIds.Contains(option.UniqueId);
        }
    }

    private void OnLinkedModelOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(CityGmlExportLinkOption.IsSelected), StringComparison.Ordinal))
        {
            return;
        }

        ClearPrepared();
        StatusMessage = BuildBaseStatusMessage();
        RefreshScopeDependentRows();
    }

    private static string BuildLinkedModelSummary(IEnumerable<string> linkNames)
    {
        string[] names = linkNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0)
        {
            return "Host model only";
        }

        if (names.Length <= 3)
        {
            return string.Join(", ", names);
        }

        return string.Join(", ", names.Take(3)) + $" (+{names.Length - 3} more)";
    }

    private void ClearPrepared()
    {
        preparedPackage = null;
        ReplaceCollection(PreparedRows, Array.Empty<DetailRow>());
        ReplaceCollection(FeatureNames, Array.Empty<string>());
        ReplaceCollection(ValidationMessages, Array.Empty<string>());
        RaisePreparedProperties();
    }

    private void RaisePreparedProperties()
    {
        RaisePropertyChanged(nameof(PreparedPackage));
        RaisePropertyChanged(nameof(CanExport));
        RaisePropertyChanged(nameof(HasPreparedRows));
        RaisePropertyChanged(nameof(HasNoPreparedRows));
        RaisePropertyChanged(nameof(HasFeatureNames));
        RaisePropertyChanged(nameof(HasNoFeatureNames));
        RaisePropertyChanged(nameof(HasValidationMessages));
        RaisePropertyChanged(nameof(HasNoValidationMessages));
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (T value in values)
        {
            collection.Add(value);
        }
    }

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
