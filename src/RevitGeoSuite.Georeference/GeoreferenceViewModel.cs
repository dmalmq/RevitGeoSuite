using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.SharedUI.Localization;

namespace RevitGeoSuite.Georeference;

public sealed class GeoreferenceViewModel : INotifyPropertyChanged
{
    private const double FeetToMeters = 0.3048d;
    private readonly ICoordinateTransformer coordinateTransformer;
    private readonly PlacementPreviewService placementPreviewService;
    private readonly SplitSurveyProjectBasePointPreviewService splitSurveyProjectBasePointPreviewService;
    private readonly PlateauGridCandidateIndex plateauGridCandidateIndex;
    private readonly PlateauGridSelectionOverlayService plateauGridOverlayService;
    private readonly PlateauGridProjectBasePointResolver plateauGridProjectBasePointResolver;
    private CrsDefinition? selectedCrs;
    private GeoreferenceStep currentStep = GeoreferenceStep.CurrentState;
    private string projectBasePointOffsetXInput = string.Empty;
    private string projectBasePointOffsetYInput = string.Empty;
    private bool confirmExistingSetup;
    private bool overrideExistingSetup;
    private string setupValidationMessage = string.Empty;
    private PlacementPreview? preview;
    private PlacementIntent? previewIntent;
    private SplitSurveyProjectBasePointIntent? splitPreviewIntent;
    private GeoreferenceCoordinateInputMode coordinateInputMode = GeoreferenceCoordinateInputMode.Manual;
    private string plateauGridOverlayGeoJson = string.Empty;
    private string plateauGridUnavailableMessage = string.Empty;
    private string plateauGridSeedTitle = string.Empty;
    private double? plateauGridSeedLatitude;
    private double? plateauGridSeedLongitude;
    private PlateauGridProjectBasePointSelection? plateauGridSelection;

    public GeoreferenceViewModel(
        CurrentProjectStateSummary currentState,
        IReadOnlyCollection<CrsDefinition> availableCrs,
        ICoordinateTransformer coordinateTransformer,
        PlacementPreviewService placementPreviewService,
        SplitSurveyProjectBasePointPreviewService splitSurveyProjectBasePointPreviewService,
        PlateauGridCandidateIndex? plateauGridCandidateIndex = null,
        PlateauGridSelectionOverlayService? plateauGridOverlayService = null,
        PlateauGridProjectBasePointResolver? plateauGridProjectBasePointResolver = null)
    {
        CurrentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        AvailableCrs = availableCrs?.OrderBy(definition => definition.EpsgCode).ToArray() ?? throw new ArgumentNullException(nameof(availableCrs));
        this.coordinateTransformer = coordinateTransformer ?? throw new ArgumentNullException(nameof(coordinateTransformer));
        this.placementPreviewService = placementPreviewService ?? throw new ArgumentNullException(nameof(placementPreviewService));
        this.splitSurveyProjectBasePointPreviewService = splitSurveyProjectBasePointPreviewService ?? throw new ArgumentNullException(nameof(splitSurveyProjectBasePointPreviewService));

        JapanMeshCalculator meshCalculator = new JapanMeshCalculator();
        this.plateauGridCandidateIndex = plateauGridCandidateIndex ?? new PlateauGridCandidateIndex(meshCalculator);
        this.plateauGridOverlayService = plateauGridOverlayService ?? new PlateauGridSelectionOverlayService(meshCalculator);
        this.plateauGridProjectBasePointResolver = plateauGridProjectBasePointResolver ?? new PlateauGridProjectBasePointResolver(meshCalculator, coordinateTransformer);

        CurrentStateRows = new ObservableCollection<SummaryRow>(CreateCurrentStateRows(CurrentState));
        PlannedSetupRows = new ObservableCollection<SummaryRow>();
        PreviewFields = new ObservableCollection<PlacementPreviewField>();
        PreviewWarnings = new ObservableCollection<string>();
        PreviewWhatWillChange = new ObservableCollection<string>();
        PreviewWhatWillNotChange = new ObservableCollection<string>();
        PlateauGridOptions = new ObservableCollection<PlateauGridSelectionItem>();

        InitializePlateauGridPicker();
        RefreshPlannedSetupRows();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CurrentProjectStateSummary CurrentState { get; }

    public IReadOnlyCollection<CrsDefinition> AvailableCrs { get; }

    public ObservableCollection<SummaryRow> CurrentStateRows { get; }

    public ObservableCollection<SummaryRow> PlannedSetupRows { get; }

    public ObservableCollection<PlacementPreviewField> PreviewFields { get; }

    public ObservableCollection<string> PreviewWarnings { get; }

    public ObservableCollection<string> PreviewWhatWillChange { get; }

    public ObservableCollection<string> PreviewWhatWillNotChange { get; }

    public ObservableCollection<PlateauGridSelectionItem> PlateauGridOptions { get; }

    public GeoreferenceStep CurrentStep
    {
        get => currentStep;
        private set
        {
            if (currentStep == value)
            {
                return;
            }

            currentStep = value;
            RaiseStepProperties();
        }
    }

    public CrsDefinition? SelectedCrs
    {
        get => selectedCrs;
        set
        {
            if (selectedCrs == value)
            {
                return;
            }

            selectedCrs = value;
            RefreshPlateauGridSelectionState();
            OnSetupChanged(
                nameof(SelectedCrs),
                nameof(SelectedCrsSummary),
                nameof(SurveyOriginSummary),
                nameof(ProjectBasePointOffsetSummary),
                nameof(PlateauGridStatusText),
                nameof(PlateauGridAnchorSummary));
        }
    }

    public string ProjectBasePointOffsetXInput
    {
        get => projectBasePointOffsetXInput;
        set
        {
            if (projectBasePointOffsetXInput == value)
            {
                return;
            }

            projectBasePointOffsetXInput = value ?? string.Empty;
            OnSetupChanged(nameof(ProjectBasePointOffsetXInput), nameof(ProjectBasePointOffsetSummary));
        }
    }

    public string ProjectBasePointOffsetYInput
    {
        get => projectBasePointOffsetYInput;
        set
        {
            if (projectBasePointOffsetYInput == value)
            {
                return;
            }

            projectBasePointOffsetYInput = value ?? string.Empty;
            OnSetupChanged(nameof(ProjectBasePointOffsetYInput), nameof(ProjectBasePointOffsetSummary));
        }
    }

    public bool ConfirmExistingSetup
    {
        get => confirmExistingSetup;
        set
        {
            if (confirmExistingSetup == value)
            {
                return;
            }

            confirmExistingSetup = value;
            OnSetupChanged(nameof(ConfirmExistingSetup));
        }
    }

    public bool OverrideExistingSetup
    {
        get => overrideExistingSetup;
        set
        {
            if (overrideExistingSetup == value)
            {
                return;
            }

            overrideExistingSetup = value;
            confirmExistingSetup = false;
            OnSetupChanged(
                nameof(OverrideExistingSetup),
                nameof(ConfirmExistingSetup),
                nameof(IsNewProjectMode),
                nameof(IsConfirmExistingSetupMode),
                nameof(UsesSplitApply),
                nameof(SetupModeTitle),
                nameof(SetupModeDescription),
                nameof(StepDescription),
                nameof(ProjectBasePointOffsetSummary),
                nameof(SurveyOriginSummary),
                nameof(IsManualCoordinateMode),
                nameof(IsPlateauGridCoordinateMode));
        }
    }

    public bool IsManualCoordinateMode
    {
        get => IsNewProjectMode && coordinateInputMode == GeoreferenceCoordinateInputMode.Manual;
        set
        {
            if (value)
            {
                SetCoordinateInputMode(GeoreferenceCoordinateInputMode.Manual);
            }
        }
    }

    public bool IsPlateauGridCoordinateMode
    {
        get => IsNewProjectMode && coordinateInputMode == GeoreferenceCoordinateInputMode.PlateauGrid;
        set
        {
            if (value && CanUsePlateauGridMode)
            {
                SetCoordinateInputMode(GeoreferenceCoordinateInputMode.PlateauGrid);
            }
        }
    }

    public string SetupValidationMessage
    {
        get => setupValidationMessage;
        private set
        {
            if (setupValidationMessage == value)
            {
                return;
            }

            setupValidationMessage = value;
            RaisePropertyChanged(nameof(SetupValidationMessage));
            RaisePropertyChanged(nameof(HasSetupValidationMessage));
        }
    }

    public PlacementPreview? Preview
    {
        get => preview;
        private set
        {
            preview = value;
            RaisePropertyChanged(nameof(Preview));
            RaisePropertyChanged(nameof(HasPreview));
            RaisePropertyChanged(nameof(PreviewPersistenceSummary));
            RaisePropertyChanged(nameof(PreviewChangeImpactSummary));
            RaisePropertyChanged(nameof(PreviewConfidenceSummary));
            RaisePropertyChanged(nameof(HasPreviewWarnings));
        }
    }

    public bool HasDetectedExistingPointSetup => CurrentState.SurveyPoint.HasSharedPosition && CurrentState.ProjectBasePoint.HasSharedPosition;

    public bool IsNewProjectMode => !HasDetectedExistingPointSetup || OverrideExistingSetup;

    public bool IsConfirmExistingSetupMode => HasDetectedExistingPointSetup && !OverrideExistingSetup;

    public bool UsesSplitApply => IsNewProjectMode;

    public bool HasPreview => Preview is not null;

    public bool HasPreviewWarnings => PreviewWarnings.Count > 0;

    public bool HasSetupValidationMessage => !string.IsNullOrWhiteSpace(SetupValidationMessage);

    public bool HasExistingSetupMessage => !string.IsNullOrWhiteSpace(CurrentState.ExistingSetupMessage);

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(CurrentState.StatusMessage);

    public string ExistingSetupMessage => CurrentState.ExistingSetupMessage;

    public string StatusMessage => CurrentState.StatusMessage;

    public bool CanUsePlateauGridMode => string.IsNullOrWhiteSpace(PlateauGridUnavailableMessage);

    public string PlateauGridUnavailableMessage => plateauGridUnavailableMessage;

    public bool HasPlateauGridUnavailableMessage => !string.IsNullOrWhiteSpace(PlateauGridUnavailableMessage);

    public string PlateauGridOverlayGeoJson => plateauGridOverlayGeoJson;

    public bool HasPlateauGridOverlay => !string.IsNullOrWhiteSpace(PlateauGridOverlayGeoJson);

    public bool HasPlateauGridOptions => PlateauGridOptions.Count > 0;

    public bool HasNoPlateauGridOptions => !HasPlateauGridOptions;

    public int SelectedPlateauGridCount => PlateauGridOptions.Count(option => option.IsSelected);

    public bool HasPlateauGridSelection => SelectedPlateauGridCount > 0;

    public bool CanClearPlateauGridSelection => HasPlateauGridSelection;

    public double? PlateauGridSeedLatitude => plateauGridSeedLatitude;

    public double? PlateauGridSeedLongitude => plateauGridSeedLongitude;

    public string PlateauGridSeedTitle => plateauGridSeedTitle;

    public double? PlateauGridAnchorLatitude => plateauGridSelection?.AnchorLatitude;

    public double? PlateauGridAnchorLongitude => plateauGridSelection?.AnchorLongitude;

    public bool HasPlateauGridAnchor => plateauGridSelection is not null;

    public string PlateauGridAnchorTitle => L("Georef.Grid.AnchorMarker");

    public string PlateauGridStatusText => BuildPlateauGridStatusText();

    public string PlateauGridAnchorSummary => BuildPlateauGridAnchorSummary();

    public string StepTitle => CurrentStep switch
    {
        GeoreferenceStep.CurrentState => $"1. {L("Georef.Step.CurrentState")}",
        GeoreferenceStep.ChooseCrs => $"2. {L("Georef.Simple.Step.Setup")}",
        GeoreferenceStep.Preview => $"3. {L("Georef.Step.Preview")}",
        _ => string.Empty
    };

    public string StepDescription => CurrentStep switch
    {
        GeoreferenceStep.CurrentState => L("Georef.StepDescription.CurrentState"),
        GeoreferenceStep.ChooseCrs => IsNewProjectMode ? L("Georef.Simple.StepDescription.NewProject") : L("Georef.Simple.StepDescription.Existing"),
        GeoreferenceStep.Preview => L("Georef.StepDescription.Preview"),
        _ => string.Empty
    };

    public string SelectedCrsSummary => SelectedCrs is null ? L("Georef.SelectedCrsEmpty") : $"EPSG:{SelectedCrs.EpsgCode}  {SelectedCrs.Name}";

    public string SetupModeTitle => IsNewProjectMode ? L("Georef.Simple.Mode.NewProject.Title") : L("Georef.Simple.Mode.Existing.Title");

    public string SetupModeDescription => IsNewProjectMode ? L("Georef.Simple.Mode.NewProject.Description") : L("Georef.Simple.Mode.Existing.Description");

    public string ExistingSetupConfirmationLabel => L("Georef.Simple.ConfirmExisting.Label");

    public string ExistingSetupConfirmationHint => L("Georef.Simple.ConfirmExisting.Hint");

    public string OverrideExistingSetupLabel => L("Georef.Simple.OverrideExisting.Label");

    public string PreviewPersistenceSummary => Preview?.PersistenceSummary ?? string.Empty;

    public string PreviewChangeImpactSummary => Preview?.ChangeImpactSummary ?? string.Empty;

    public string PreviewConfidenceSummary => Preview?.ConfidenceSummary ?? string.Empty;

    public bool CanGoBack => CurrentStep != GeoreferenceStep.CurrentState;

    public bool ShowNextButton => CurrentStep != GeoreferenceStep.Preview;

    public string NextButtonText => CurrentStep == GeoreferenceStep.ChooseCrs ? L("Common.Preview") : L("Common.Next");

    public bool CanApply => CurrentStep == GeoreferenceStep.Preview && Preview?.IsReadyToApply == true && CurrentState.IsSupportedDocument && !CurrentState.IsReadOnly;

    public bool CanGoNext => CurrentStep switch
    {
        GeoreferenceStep.CurrentState => CurrentState.IsSupportedDocument,
        GeoreferenceStep.ChooseCrs => BuildSetupValidationResult().IsValid,
        _ => false
    };

    public bool CanNavigateToCurrentState => true;

    public bool CanNavigateToChooseCrs => CurrentState.IsSupportedDocument;

    public bool CanNavigateToPreview => GetFurthestNavigableStep() == GeoreferenceStep.Preview;

    public bool IsCurrentStateStepVisible => CurrentStep == GeoreferenceStep.CurrentState;

    public bool IsChooseCrsStepVisible => CurrentStep == GeoreferenceStep.ChooseCrs;

    public bool IsPreviewStepVisible => CurrentStep == GeoreferenceStep.Preview;

    public string SurveyOriginSummary => BuildSurveyOriginSummary();

    public string ProjectBasePointOffsetSummary => IsConfirmExistingSetupMode
        ? FormatCurrentSharedCoordinate(CurrentState.ProjectBasePoint)
        : IsPlateauGridCoordinateMode
            ? BuildPlateauGridProjectBasePointSummary()
            : TryGetManualOffsetCoordinate(out ProjectedCoordinate offset)
                ? string.Format(CultureInfo.InvariantCulture, "E {0:F3} m, N {1:F3} m", offset.Easting, offset.Northing)
                : L("Georef.Simple.PendingOffset");

    public PlacementIntent GetApplyIntent()
    {
        if (!CanApply || previewIntent is null)
        {
            throw new InvalidOperationException("Generate a valid existing-setup preview in an editable project before applying georeference changes.");
        }

        return previewIntent;
    }

    public SplitSurveyProjectBasePointIntent GetSplitApplyIntent()
    {
        if (!CanApply || splitPreviewIntent is null)
        {
            throw new InvalidOperationException("Generate a valid new-project preview in an editable project before applying georeference changes.");
        }

        return splitPreviewIntent;
    }

    public void GoNext()
    {
        if (!CanGoNext)
        {
            return;
        }

        if (CurrentStep == GeoreferenceStep.CurrentState)
        {
            CurrentStep = GeoreferenceStep.ChooseCrs;
            return;
        }

        BuildPreview();
        if (Preview is not null)
        {
            CurrentStep = GeoreferenceStep.Preview;
        }
    }

    public void GoBack()
    {
        if (!CanGoBack)
        {
            return;
        }

        CurrentStep = CurrentStep == GeoreferenceStep.Preview
            ? GeoreferenceStep.ChooseCrs
            : GeoreferenceStep.CurrentState;
    }

    public bool CanNavigateToStep(GeoreferenceStep step) => step switch
    {
        GeoreferenceStep.CurrentState => true,
        GeoreferenceStep.ChooseCrs => CurrentState.IsSupportedDocument,
        GeoreferenceStep.Preview => GetFurthestNavigableStep() == GeoreferenceStep.Preview,
        _ => false
    };

    public void NavigateToStep(GeoreferenceStep step)
    {
        if (!CanNavigateToStep(step) || step == CurrentStep)
        {
            return;
        }

        if (step == GeoreferenceStep.Preview)
        {
            BuildPreview();
            if (Preview is not null)
            {
                CurrentStep = GeoreferenceStep.Preview;
            }

            return;
        }

        CurrentStep = step;
    }

    public bool TogglePlateauGridSelection(string tileId)
    {
        if (string.IsNullOrWhiteSpace(tileId))
        {
            return false;
        }

        PlateauGridSelectionItem? option = PlateauGridOptions.FirstOrDefault(item => string.Equals(item.TileId, tileId, StringComparison.Ordinal));
        if (option is null)
        {
            return false;
        }

        option.IsSelected = !option.IsSelected;
        return true;
    }

    public void ClearPlateauGridSelection()
    {
        PlateauGridSelectionItem[] selectedOptions = PlateauGridOptions.Where(option => option.IsSelected).ToArray();
        if (selectedOptions.Length == 0)
        {
            return;
        }

        foreach (PlateauGridSelectionItem option in selectedOptions)
        {
            option.PropertyChanged -= OnPlateauGridOptionPropertyChanged;
            option.IsSelected = false;
            option.PropertyChanged += OnPlateauGridOptionPropertyChanged;
        }

        RefreshPlateauGridSelectionState();
        HandlePlateauGridSelectionChanged();
    }

    private void BuildPreview()
    {
        SetupValidationResult validation = BuildSetupValidationResult();
        SetupValidationMessage = string.Join(Environment.NewLine, validation.Errors);
        if (!validation.IsValid || SelectedCrs is null)
        {
            previewIntent = null;
            splitPreviewIntent = null;
            Preview = null;
            ResetPreviewCollections();
            return;
        }

        if (IsNewProjectMode)
        {
            splitPreviewIntent = BuildNewProjectSplitIntent();
            previewIntent = null;
            Preview = splitSurveyProjectBasePointPreviewService.CreatePreview(CurrentState, splitPreviewIntent);
        }
        else
        {
            previewIntent = BuildExistingSetupMetadataIntent();
            splitPreviewIntent = null;
            Preview = placementPreviewService.CreatePreview(PlacementCurrentStateFactory.Create(CurrentState), previewIntent);
        }

        ReplaceCollection(PreviewFields, Preview.Fields);
        ReplaceCollection(PreviewWarnings, Preview.Warnings);
        ReplaceCollection(PreviewWhatWillChange, Preview.WhatWillChange);
        ReplaceCollection(PreviewWhatWillNotChange, Preview.WhatWillNotChange);
        RaisePropertyChanged(nameof(HasPreviewWarnings));
        RaisePropertyChanged(nameof(CanApply));
    }

    private SetupValidationResult BuildSetupValidationResult()
    {
        SetupValidationResult result = new SetupValidationResult();
        if (SelectedCrs is null)
        {
            result.Errors.Add(L("Georef.Simple.Validation.SelectCrs"));
        }

        if (IsConfirmExistingSetupMode)
        {
            if (!ConfirmExistingSetup)
            {
                result.Errors.Add(L("Georef.Simple.Validation.ConfirmExisting"));
            }

            if (!CurrentState.SurveyPoint.HasEstimatedLocation)
            {
                result.Errors.Add(L("Georef.Simple.Validation.SurveyUnavailable"));
            }

            if (!CurrentState.ProjectBasePoint.HasEstimatedLocation || !CurrentState.ProjectBasePoint.HasSharedPosition)
            {
                result.Errors.Add(L("Georef.Simple.Validation.ProjectBasePointUnavailable"));
            }
        }
        else if (IsPlateauGridCoordinateMode)
        {
            if (!CanUsePlateauGridMode)
            {
                result.Errors.Add(PlateauGridUnavailableMessage);
            }
            else if (!HasPlateauGridSelection)
            {
                result.Errors.Add(L("Georef.Grid.Validation.SelectGrid"));
            }
            else if (SelectedCrs is not null && (plateauGridSelection?.ProjectedCoordinate).HasValue == false)
            {
                result.Errors.Add(L("Georef.Grid.Validation.ResolveSelection"));
            }
        }
        else
        {
            if (!TryParseCoordinateValue(ProjectBasePointOffsetXInput, out _))
            {
                result.Errors.Add(L("Georef.Simple.Validation.OffsetX"));
            }

            if (!TryParseCoordinateValue(ProjectBasePointOffsetYInput, out _))
            {
                result.Errors.Add(L("Georef.Simple.Validation.OffsetY"));
            }
        }

        return result;
    }

    private SplitSurveyProjectBasePointIntent BuildNewProjectSplitIntent()
    {
        ProjectedCoordinate projectOffset = ParseResolvedProjectBasePointCoordinate();
        ProjectOrigin surveyOrigin = BuildSurveyOriginOrThrow();
        GeographicCoordinate projectBasePointCoordinate = IsPlateauGridCoordinateMode && plateauGridSelection is not null
            ? new GeographicCoordinate(plateauGridSelection.AnchorLatitude, plateauGridSelection.AnchorLongitude)
            : coordinateTransformer.Unproject(projectOffset, SelectedCrs!.ToReference());
        string setupSource = BuildNewProjectSetupSource();

        return new SplitSurveyProjectBasePointIntent
        {
            SelectedCrs = SelectedCrs!.ToReference(),
            SharedSurveyOrigin = surveyOrigin,
            SharedSurveyProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            LocalProjectBasePoint = new WorkingProjectBasePointReference
            {
                ProjectCrs = SelectedCrs.ToReference(),
                Origin = new ProjectOrigin
                {
                    Latitude = projectBasePointCoordinate.Latitude,
                    Longitude = projectBasePointCoordinate.Longitude,
                    ElevationMeters = 0d
                },
                ProjectedCoordinate = projectOffset,
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = setupSource
            },
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = setupSource,
            ApplyMode = PlacementApplyMode.ProjectLocation
        };
    }

    private PlacementIntent BuildExistingSetupMetadataIntent()
    {
        return new PlacementIntent
        {
            SelectedCrs = SelectedCrs!.ToReference(),
            SelectedOrigin = new ProjectOrigin
            {
                Latitude = CurrentState.SurveyPoint.EstimatedLatitudeDegrees!.Value,
                Longitude = CurrentState.SurveyPoint.EstimatedLongitudeDegrees!.Value,
                ElevationMeters = 0d
            },
            SelectedProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = L("Georef.Simple.SetupSource.Existing"),
            ApplyMode = PlacementApplyMode.MetadataOnly,
            AnchorTarget = PlacementAnchorTarget.SurveyPoint,
            WorkingProjectBasePoint = new WorkingProjectBasePointReference
            {
                ProjectCrs = SelectedCrs.ToReference(),
                Origin = new ProjectOrigin
                {
                    Latitude = CurrentState.ProjectBasePoint.EstimatedLatitudeDegrees!.Value,
                    Longitude = CurrentState.ProjectBasePoint.EstimatedLongitudeDegrees!.Value,
                    ElevationMeters = 0d
                },
                ProjectedCoordinate = new ProjectedCoordinate(
                    CurrentState.ProjectBasePoint.SharedEastWestFeet!.Value * FeetToMeters,
                    CurrentState.ProjectBasePoint.SharedNorthSouthFeet!.Value * FeetToMeters),
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = L("Georef.Simple.SetupSource.Existing")
            }
        };
    }

    private void RefreshPlannedSetupRows()
    {
        List<SummaryRow> rows = new List<SummaryRow>
        {
            new SummaryRow(L("Georef.Simple.Planned.Mode"), SetupModeTitle)
        };

        if (IsNewProjectMode)
        {
            rows.Add(new SummaryRow(
                L("Georef.Grid.InputModeLabel"),
                IsPlateauGridCoordinateMode ? L("Georef.Grid.Mode.Grid") : L("Georef.Grid.Mode.Manual")));
        }

        rows.Add(new SummaryRow(L("Georef.Label.SelectedCrs"), SelectedCrsSummary));
        rows.Add(new SummaryRow(L("Georef.Simple.Planned.Survey"), SurveyOriginSummary));
        rows.Add(new SummaryRow(L("Georef.Simple.Planned.Project"), ProjectBasePointOffsetSummary));
        if (IsPlateauGridCoordinateMode)
        {
            rows.Add(new SummaryRow(L("Georef.Grid.SelectionLabel"), BuildSelectedPlateauGridSummary()));
        }

        rows.Add(new SummaryRow(
            L("Georef.Simple.Planned.Apply"),
            IsNewProjectMode
                ? IsPlateauGridCoordinateMode
                    ? L("Georef.Simple.Planned.Apply.NewProject.Grid")
                    : L("Georef.Simple.Planned.Apply.NewProject")
                : L("Georef.Simple.Planned.Apply.Existing")));

        if (IsConfirmExistingSetupMode)
        {
            rows.Add(new SummaryRow(L("Georef.Simple.Planned.Confirmation"), ConfirmExistingSetup ? L("Common.Yes") : L("Common.No")));
        }

        ReplaceCollection(PlannedSetupRows, rows);
    }

    private void OnSetupChanged(params string[] propertyNames)
    {
        InvalidatePreview();
        RefreshPlannedSetupRows();
        foreach (string propertyName in propertyNames)
        {
            RaisePropertyChanged(propertyName);
        }

        RaisePropertyChanged(nameof(SetupModeDescription));
        RaisePropertyChanged(nameof(CanGoNext));
        RaisePropertyChanged(nameof(CanApply));
    }

    private void SetCoordinateInputMode(GeoreferenceCoordinateInputMode mode)
    {
        if (coordinateInputMode == mode)
        {
            return;
        }

        if (mode == GeoreferenceCoordinateInputMode.PlateauGrid && !CanUsePlateauGridMode)
        {
            return;
        }

        coordinateInputMode = mode;
        OnSetupChanged(
            nameof(IsManualCoordinateMode),
            nameof(IsPlateauGridCoordinateMode),
            nameof(ProjectBasePointOffsetSummary),
            nameof(PlateauGridStatusText),
            nameof(PlateauGridAnchorSummary));
    }

    private void InitializePlateauGridPicker()
    {
        if (!TryResolvePlateauGridSeed(out double latitude, out double longitude, out string title))
        {
            plateauGridUnavailableMessage = L("Georef.Grid.Unavailable.NoHint");
            plateauGridSeedLatitude = null;
            plateauGridSeedLongitude = null;
            plateauGridSeedTitle = string.Empty;
            plateauGridSelection = null;
            plateauGridOverlayGeoJson = string.Empty;
            ReplacePlateauGridOptions(Array.Empty<PlateauGridSelectionItem>());
            RaisePlateauGridProperties();
            return;
        }

        plateauGridUnavailableMessage = string.Empty;
        plateauGridSeedLatitude = latitude;
        plateauGridSeedLongitude = longitude;
        plateauGridSeedTitle = title;

        PlateauGridSelectionItem[] options = plateauGridCandidateIndex.GetCandidateGrids(latitude, longitude)
            .Select(CreatePlateauGridSelectionItem)
            .ToArray();

        ReplacePlateauGridOptions(options);
        RefreshPlateauGridSelectionState();
        RaisePlateauGridProperties();
    }

    private bool TryResolvePlateauGridSeed(out double latitude, out double longitude, out string title)
    {
        if (CurrentState.ProjectBasePoint.HasEstimatedLocation)
        {
            latitude = CurrentState.ProjectBasePoint.EstimatedLatitudeDegrees!.Value;
            longitude = CurrentState.ProjectBasePoint.EstimatedLongitudeDegrees!.Value;
            title = L("Georef.Grid.Seed.ProjectBasePoint");
            return true;
        }

        if (CurrentState.SiteLatitudeDegrees.HasValue && CurrentState.SiteLongitudeDegrees.HasValue)
        {
            latitude = CurrentState.SiteLatitudeDegrees.Value;
            longitude = CurrentState.SiteLongitudeDegrees.Value;
            title = L("Georef.Grid.Seed.SiteLocation");
            return true;
        }

        latitude = 0d;
        longitude = 0d;
        title = string.Empty;
        return false;
    }

    private PlateauGridSelectionItem CreatePlateauGridSelectionItem(PlateauGridCandidate candidate)
    {
        return new PlateauGridSelectionItem
        {
            TileId = candidate.TileId,
            Title = candidate.TileId,
            Description = candidate.IsPrimary ? L("Georef.Grid.Option.Primary") : L("Georef.Grid.Option.Neighbor"),
            IsSeedCandidate = candidate.IsPrimary
        };
    }

    private void ReplacePlateauGridOptions(IEnumerable<PlateauGridSelectionItem> values)
    {
        foreach (PlateauGridSelectionItem existing in PlateauGridOptions)
        {
            existing.PropertyChanged -= OnPlateauGridOptionPropertyChanged;
        }

        PlateauGridOptions.Clear();
        foreach (PlateauGridSelectionItem value in values)
        {
            value.PropertyChanged += OnPlateauGridOptionPropertyChanged;
            PlateauGridOptions.Add(value);
        }
    }

    private void OnPlateauGridOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(PlateauGridSelectionItem.IsSelected), StringComparison.Ordinal))
        {
            return;
        }

        RefreshPlateauGridSelectionState();
        HandlePlateauGridSelectionChanged();
    }

    private void HandlePlateauGridSelectionChanged()
    {
        InvalidatePreview();
        RefreshPlannedSetupRows();
        RaisePlateauGridProperties();
        RaisePropertyChanged(nameof(CanGoNext));
        RaisePropertyChanged(nameof(CanApply));
    }

    private void RefreshPlateauGridSelectionState()
    {
        plateauGridSelection = plateauGridProjectBasePointResolver.Resolve(GetSelectedPlateauGridIds(), SelectedCrs?.ToReference());
        plateauGridOverlayGeoJson = PlateauGridOptions.Count == 0
            ? string.Empty
            : plateauGridOverlayService.CreateGeoJson(PlateauGridOptions.ToArray());
    }

    private string[] GetSelectedPlateauGridIds()
    {
        return PlateauGridOptions
            .Where(option => option.IsSelected)
            .Select(option => option.TileId)
            .ToArray();
    }

    private string BuildPlateauGridProjectBasePointSummary()
    {
        if (!CanUsePlateauGridMode)
        {
            return PlateauGridUnavailableMessage;
        }

        if (plateauGridSelection is null)
        {
            return L("Georef.Grid.PendingSelection");
        }

        if (plateauGridSelection.ProjectedCoordinate.HasValue)
        {
            ProjectedCoordinate projectedCoordinate = plateauGridSelection.ProjectedCoordinate.Value;
            return string.Format(
                CultureInfo.InvariantCulture,
                L("Georef.Grid.ProjectSummary.Projected"),
                plateauGridSelection.SelectedMeshCodes.Count,
                projectedCoordinate.Easting,
                projectedCoordinate.Northing);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            L("Georef.Grid.ProjectSummary.Geographic"),
            plateauGridSelection.SelectedMeshCodes.Count,
            plateauGridSelection.AnchorLatitude,
            plateauGridSelection.AnchorLongitude);
    }

    private string BuildPlateauGridStatusText()
    {
        if (!CanUsePlateauGridMode)
        {
            return PlateauGridUnavailableMessage;
        }

        if (PlateauGridOptions.Count == 0)
        {
            return L("Georef.Grid.NoCandidates");
        }

        if (plateauGridSelection is null)
        {
            return string.Format(CultureInfo.InvariantCulture, L("Georef.Grid.Status.Select"), PlateauGridSeedTitle);
        }

        if (plateauGridSelection.ProjectedCoordinate.HasValue)
        {
            ProjectedCoordinate projectedCoordinate = plateauGridSelection.ProjectedCoordinate.Value;
            return string.Format(
                CultureInfo.InvariantCulture,
                L("Georef.Grid.Status.Selected.Projected"),
                plateauGridSelection.SelectedMeshCodes.Count,
                projectedCoordinate.Easting,
                projectedCoordinate.Northing);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            L("Georef.Grid.Status.Selected.Geographic"),
            plateauGridSelection.SelectedMeshCodes.Count,
            plateauGridSelection.AnchorLatitude,
            plateauGridSelection.AnchorLongitude);
    }

    private string BuildPlateauGridAnchorSummary()
    {
        if (!CanUsePlateauGridMode)
        {
            return PlateauGridUnavailableMessage;
        }

        if (plateauGridSelection is null)
        {
            return L("Georef.Grid.Anchor.Pending");
        }

        if (plateauGridSelection.ProjectedCoordinate.HasValue)
        {
            ProjectedCoordinate projectedCoordinate = plateauGridSelection.ProjectedCoordinate.Value;
            return string.Format(
                CultureInfo.InvariantCulture,
                L("Georef.Grid.Anchor.Resolved.Projected"),
                plateauGridSelection.SelectedMeshCodes.Count,
                plateauGridSelection.AnchorLatitude,
                plateauGridSelection.AnchorLongitude,
                projectedCoordinate.Easting,
                projectedCoordinate.Northing);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            L("Georef.Grid.Anchor.Resolved.Geographic"),
            plateauGridSelection.SelectedMeshCodes.Count,
            plateauGridSelection.AnchorLatitude,
            plateauGridSelection.AnchorLongitude);
    }

    private string BuildSelectedPlateauGridSummary()
    {
        if (plateauGridSelection is null)
        {
            return L("Georef.Grid.PendingSelection");
        }

        string[] selectedMeshCodes = plateauGridSelection.SelectedMeshCodes
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        if (selectedMeshCodes.Length <= 4)
        {
            return string.Join(", ", selectedMeshCodes);
        }

        return string.Join(", ", selectedMeshCodes.Take(4))
            + string.Format(CultureInfo.InvariantCulture, L("Georef.Grid.SelectionMore"), selectedMeshCodes.Length - 4);
    }

    private string BuildNewProjectSetupSource()
    {
        return IsPlateauGridCoordinateMode
            ? string.Format(CultureInfo.InvariantCulture, L("Georef.Grid.SetupSource.SelectionExtent"), plateauGridSelection?.SelectedMeshCodes.Count ?? 0)
            : L("Georef.Simple.SetupSource.NewProject");
    }

    private bool TryBuildSurveyOrigin(out ProjectOrigin? origin)
    {
        origin = null;
        if (SelectedCrs is null)
        {
            return false;
        }

        GeographicCoordinate coordinate = coordinateTransformer.Unproject(new ProjectedCoordinate(0d, 0d), SelectedCrs.ToReference());
        origin = new ProjectOrigin { Latitude = coordinate.Latitude, Longitude = coordinate.Longitude, ElevationMeters = 0d };
        return true;
    }

    private ProjectOrigin BuildSurveyOriginOrThrow()
    {
        if (!TryBuildSurveyOrigin(out ProjectOrigin? origin) || origin is null)
        {
            throw new InvalidOperationException("Select a coordinate reference system before generating georeference preview.");
        }

        return origin;
    }

    private string BuildSurveyOriginSummary()
    {
        if (SelectedCrs is null)
        {
            return L("Georef.Simple.PendingCrs");
        }

        if (!TryBuildSurveyOrigin(out ProjectOrigin? origin) || origin is null)
        {
            return L("Georef.Simple.PendingOrigin");
        }

        return string.Format(CultureInfo.InvariantCulture, "E 0.000 m, N 0.000 m / Lat {0:F6}, Lon {1:F6}", origin.Latitude, origin.Longitude);
    }

    private bool TryGetManualOffsetCoordinate(out ProjectedCoordinate offset)
    {
        offset = default;
        if (!TryParseCoordinateValue(ProjectBasePointOffsetXInput, out double x) || !TryParseCoordinateValue(ProjectBasePointOffsetYInput, out double y))
        {
            return false;
        }

        offset = new ProjectedCoordinate(x, y);
        return true;
    }

    private bool TryGetResolvedProjectBasePointCoordinate(out ProjectedCoordinate offset)
    {
        offset = default;
        if (IsPlateauGridCoordinateMode)
        {
            if (plateauGridSelection?.ProjectedCoordinate.HasValue == true)
            {
                offset = plateauGridSelection.ProjectedCoordinate.Value;
                return true;
            }

            return false;
        }

        return TryGetManualOffsetCoordinate(out offset);
    }

    private ProjectedCoordinate ParseResolvedProjectBasePointCoordinate()
    {
        if (!TryGetResolvedProjectBasePointCoordinate(out ProjectedCoordinate offset))
        {
            throw new InvalidOperationException("Resolve valid Project Base Point coordinates before generating georeference preview.");
        }

        return offset;
    }

    private void InvalidatePreview()
    {
        previewIntent = null;
        splitPreviewIntent = null;
        Preview = null;
        ResetPreviewCollections();
        SetupValidationMessage = CurrentStep == GeoreferenceStep.ChooseCrs
            ? string.Join(Environment.NewLine, BuildSetupValidationResult().Errors)
            : string.Empty;
        RaisePropertyChanged(nameof(CanApply));
        RaisePropertyChanged(nameof(CanNavigateToPreview));
    }

    private void ResetPreviewCollections()
    {
        PreviewFields.Clear();
        PreviewWarnings.Clear();
        PreviewWhatWillChange.Clear();
        PreviewWhatWillNotChange.Clear();
        RaisePropertyChanged(nameof(HasPreviewWarnings));
    }

    private GeoreferenceStep GetFurthestNavigableStep()
    {
        return !CurrentState.IsSupportedDocument
            ? GeoreferenceStep.CurrentState
            : BuildSetupValidationResult().IsValid
                ? GeoreferenceStep.Preview
                : GeoreferenceStep.ChooseCrs;
    }

    private void RaiseStepProperties()
    {
        SetupValidationMessage = CurrentStep == GeoreferenceStep.ChooseCrs
            ? string.Join(Environment.NewLine, BuildSetupValidationResult().Errors)
            : string.Empty;
        RaisePropertyChanged(nameof(CurrentStep));
        RaisePropertyChanged(nameof(StepTitle));
        RaisePropertyChanged(nameof(StepDescription));
        RaisePropertyChanged(nameof(CanGoBack));
        RaisePropertyChanged(nameof(CanGoNext));
        RaisePropertyChanged(nameof(CanApply));
        RaisePropertyChanged(nameof(NextButtonText));
        RaisePropertyChanged(nameof(ShowNextButton));
        RaisePropertyChanged(nameof(IsCurrentStateStepVisible));
        RaisePropertyChanged(nameof(IsChooseCrsStepVisible));
        RaisePropertyChanged(nameof(IsPreviewStepVisible));
        RaisePropertyChanged(nameof(CanNavigateToCurrentState));
        RaisePropertyChanged(nameof(CanNavigateToChooseCrs));
        RaisePropertyChanged(nameof(CanNavigateToPreview));
    }

    private void RaisePlateauGridProperties()
    {
        RaisePropertyChanged(nameof(CanUsePlateauGridMode));
        RaisePropertyChanged(nameof(PlateauGridUnavailableMessage));
        RaisePropertyChanged(nameof(HasPlateauGridUnavailableMessage));
        RaisePropertyChanged(nameof(PlateauGridOverlayGeoJson));
        RaisePropertyChanged(nameof(HasPlateauGridOverlay));
        RaisePropertyChanged(nameof(HasPlateauGridOptions));
        RaisePropertyChanged(nameof(HasNoPlateauGridOptions));
        RaisePropertyChanged(nameof(SelectedPlateauGridCount));
        RaisePropertyChanged(nameof(HasPlateauGridSelection));
        RaisePropertyChanged(nameof(CanClearPlateauGridSelection));
        RaisePropertyChanged(nameof(PlateauGridSeedLatitude));
        RaisePropertyChanged(nameof(PlateauGridSeedLongitude));
        RaisePropertyChanged(nameof(PlateauGridSeedTitle));
        RaisePropertyChanged(nameof(PlateauGridAnchorLatitude));
        RaisePropertyChanged(nameof(PlateauGridAnchorLongitude));
        RaisePropertyChanged(nameof(HasPlateauGridAnchor));
        RaisePropertyChanged(nameof(PlateauGridStatusText));
        RaisePropertyChanged(nameof(PlateauGridAnchorSummary));
        RaisePropertyChanged(nameof(ProjectBasePointOffsetSummary));
    }

    private static bool TryParseCoordinateValue(string? text, out double value)
    {
        value = 0d;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return true;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatCurrentSharedCoordinate(BasePointSnapshot point)
    {
        if (!point.HasSharedPosition)
        {
            return L("Georef.Simple.CurrentOffsetUnavailable");
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "E {0:F3} m, N {1:F3} m",
            point.SharedEastWestFeet!.Value * FeetToMeters,
            point.SharedNorthSouthFeet!.Value * FeetToMeters);
    }

    private static IEnumerable<SummaryRow> CreateCurrentStateRows(CurrentProjectStateSummary summary)
    {
        yield return new SummaryRow(L("Georef.Current.Document"), summary.DocumentTitle);
        yield return new SummaryRow(L("Georef.Current.StoredMetadata"), summary.HasStoredGeoInfo ? L("Common.Yes") : L("Common.No"));
        yield return new SummaryRow(L("Georef.Current.ReadOnly"), summary.IsReadOnly ? L("Common.Yes") : L("Common.No"));
        if (summary.SiteLatitudeDegrees.HasValue && summary.SiteLongitudeDegrees.HasValue)
        {
            yield return new SummaryRow(L("Georef.Current.SiteLocation"), $"{summary.SiteLatitudeDegrees.Value:F6}, {summary.SiteLongitudeDegrees.Value:F6}");
        }

        yield return new SummaryRow(L("Georef.Current.SurveyShared"), FormatCurrentSharedCoordinate(summary.SurveyPoint));
        yield return new SummaryRow(L("Georef.Current.ProjectShared"), FormatCurrentSharedCoordinate(summary.ProjectBasePoint));
        if (summary.SurveyPoint.HasEstimatedLocation)
        {
            yield return new SummaryRow(L("Georef.Current.SurveyEstimate"), $"Lat {summary.SurveyPoint.EstimatedLatitudeDegrees!.Value:F6}, Lon {summary.SurveyPoint.EstimatedLongitudeDegrees!.Value:F6}");
        }

        if (summary.ProjectBasePoint.HasEstimatedLocation)
        {
            yield return new SummaryRow(L("Georef.Current.ProjectEstimate"), $"Lat {summary.ProjectBasePoint.EstimatedLatitudeDegrees!.Value:F6}, Lon {summary.ProjectBasePoint.EstimatedLongitudeDegrees!.Value:F6}");
        }

        yield return new SummaryRow(L("Georef.Current.TrueNorthAngle"), $"{summary.ProjectPosition.AngleDegrees:F3}°");
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
        {
            target.Add(value);
        }
    }

    private static string L(string key) => UiLocalizer.Instance.Get(key);

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class SetupValidationResult
    {
        public List<string> Errors { get; } = new List<string>();

        public bool IsValid => Errors.Count == 0;
    }
}
