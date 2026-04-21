using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
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

    public GeoreferenceViewModel(
        CurrentProjectStateSummary currentState,
        IReadOnlyCollection<CrsDefinition> availableCrs,
        ICoordinateTransformer coordinateTransformer,
        PlacementPreviewService placementPreviewService,
        SplitSurveyProjectBasePointPreviewService splitSurveyProjectBasePointPreviewService)
    {
        CurrentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        AvailableCrs = availableCrs?.OrderBy(definition => definition.EpsgCode).ToArray() ?? throw new ArgumentNullException(nameof(availableCrs));
        this.coordinateTransformer = coordinateTransformer ?? throw new ArgumentNullException(nameof(coordinateTransformer));
        this.placementPreviewService = placementPreviewService ?? throw new ArgumentNullException(nameof(placementPreviewService));
        this.splitSurveyProjectBasePointPreviewService = splitSurveyProjectBasePointPreviewService ?? throw new ArgumentNullException(nameof(splitSurveyProjectBasePointPreviewService));

        CurrentStateRows = new ObservableCollection<SummaryRow>(CreateCurrentStateRows(CurrentState));
        PlannedSetupRows = new ObservableCollection<SummaryRow>();
        PreviewFields = new ObservableCollection<PlacementPreviewField>();
        PreviewWarnings = new ObservableCollection<string>();
        PreviewWhatWillChange = new ObservableCollection<string>();
        PreviewWhatWillNotChange = new ObservableCollection<string>();
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

    public GeoreferenceStep CurrentStep { get => currentStep; private set { if (currentStep == value) return; currentStep = value; RaiseStepProperties(); } }
    public CrsDefinition? SelectedCrs { get => selectedCrs; set { if (selectedCrs == value) return; selectedCrs = value; OnSetupChanged(nameof(SelectedCrs), nameof(SelectedCrsSummary), nameof(SurveyOriginSummary), nameof(ProjectBasePointOffsetSummary)); } }
    public string ProjectBasePointOffsetXInput { get => projectBasePointOffsetXInput; set { if (projectBasePointOffsetXInput == value) return; projectBasePointOffsetXInput = value ?? string.Empty; OnSetupChanged(nameof(ProjectBasePointOffsetXInput), nameof(ProjectBasePointOffsetSummary)); } }
    public string ProjectBasePointOffsetYInput { get => projectBasePointOffsetYInput; set { if (projectBasePointOffsetYInput == value) return; projectBasePointOffsetYInput = value ?? string.Empty; OnSetupChanged(nameof(ProjectBasePointOffsetYInput), nameof(ProjectBasePointOffsetSummary)); } }
    public bool ConfirmExistingSetup { get => confirmExistingSetup; set { if (confirmExistingSetup == value) return; confirmExistingSetup = value; OnSetupChanged(nameof(ConfirmExistingSetup)); } }
    public bool OverrideExistingSetup { get => overrideExistingSetup; set { if (overrideExistingSetup == value) return; overrideExistingSetup = value; confirmExistingSetup = false; OnSetupChanged(nameof(OverrideExistingSetup), nameof(ConfirmExistingSetup), nameof(IsNewProjectMode), nameof(IsConfirmExistingSetupMode), nameof(UsesSplitApply), nameof(SetupModeTitle), nameof(SetupModeDescription), nameof(StepDescription), nameof(ProjectBasePointOffsetSummary), nameof(SurveyOriginSummary)); } }
    public string SetupValidationMessage { get => setupValidationMessage; private set { if (setupValidationMessage == value) return; setupValidationMessage = value; RaisePropertyChanged(nameof(SetupValidationMessage)); RaisePropertyChanged(nameof(HasSetupValidationMessage)); } }
    public PlacementPreview? Preview { get => preview; private set { preview = value; RaisePropertyChanged(nameof(Preview)); RaisePropertyChanged(nameof(HasPreview)); RaisePropertyChanged(nameof(PreviewPersistenceSummary)); RaisePropertyChanged(nameof(PreviewChangeImpactSummary)); RaisePropertyChanged(nameof(PreviewConfidenceSummary)); RaisePropertyChanged(nameof(HasPreviewWarnings)); } }

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
    public string StepTitle => CurrentStep switch { GeoreferenceStep.CurrentState => $"1. {L("Georef.Step.CurrentState")}", GeoreferenceStep.ChooseCrs => $"2. {L("Georef.Simple.Step.Setup")}", GeoreferenceStep.Preview => $"3. {L("Georef.Step.Preview")}", _ => string.Empty };
    public string StepDescription => CurrentStep switch { GeoreferenceStep.CurrentState => L("Georef.StepDescription.CurrentState"), GeoreferenceStep.ChooseCrs => IsNewProjectMode ? L("Georef.Simple.StepDescription.NewProject") : L("Georef.Simple.StepDescription.Existing"), GeoreferenceStep.Preview => L("Georef.StepDescription.Preview"), _ => string.Empty };
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
    public bool CanGoNext => CurrentStep switch { GeoreferenceStep.CurrentState => CurrentState.IsSupportedDocument, GeoreferenceStep.ChooseCrs => BuildSetupValidationResult().IsValid, _ => false };
    public bool CanNavigateToCurrentState => true;
    public bool CanNavigateToChooseCrs => CurrentState.IsSupportedDocument;
    public bool CanNavigateToPreview => GetFurthestNavigableStep() == GeoreferenceStep.Preview;
    public bool IsCurrentStateStepVisible => CurrentStep == GeoreferenceStep.CurrentState;
    public bool IsChooseCrsStepVisible => CurrentStep == GeoreferenceStep.ChooseCrs;
    public bool IsPreviewStepVisible => CurrentStep == GeoreferenceStep.Preview;
    public string SurveyOriginSummary => BuildSurveyOriginSummary();
    public string ProjectBasePointOffsetSummary => IsConfirmExistingSetupMode ? FormatCurrentSharedCoordinate(CurrentState.ProjectBasePoint) : TryGetOffsetCoordinate(out ProjectedCoordinate offset) ? string.Format(CultureInfo.InvariantCulture, "E {0:F3} m, N {1:F3} m", offset.Easting, offset.Northing) : L("Georef.Simple.PendingOffset");

    public PlacementIntent GetApplyIntent()
    {
        if (!CanApply || previewIntent is null) throw new InvalidOperationException("Generate a valid existing-setup preview in an editable project before applying georeference changes.");
        return previewIntent;
    }

    public SplitSurveyProjectBasePointIntent GetSplitApplyIntent()
    {
        if (!CanApply || splitPreviewIntent is null) throw new InvalidOperationException("Generate a valid new-project preview in an editable project before applying georeference changes.");
        return splitPreviewIntent;
    }

    public void GoNext()
    {
        if (!CanGoNext) return;
        if (CurrentStep == GeoreferenceStep.CurrentState) { CurrentStep = GeoreferenceStep.ChooseCrs; return; }
        BuildPreview();
        if (Preview is not null) CurrentStep = GeoreferenceStep.Preview;
    }

    public void GoBack()
    {
        if (!CanGoBack) return;
        CurrentStep = CurrentStep == GeoreferenceStep.Preview ? GeoreferenceStep.ChooseCrs : GeoreferenceStep.CurrentState;
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
        if (!CanNavigateToStep(step) || step == CurrentStep) return;
        if (step == GeoreferenceStep.Preview) { BuildPreview(); if (Preview is not null) CurrentStep = GeoreferenceStep.Preview; return; }
        CurrentStep = step;
    }

    private void BuildPreview()
    {
        SetupValidationResult validation = BuildSetupValidationResult();
        SetupValidationMessage = string.Join(Environment.NewLine, validation.Errors);
        if (!validation.IsValid || SelectedCrs is null) { previewIntent = null; splitPreviewIntent = null; Preview = null; ResetPreviewCollections(); return; }

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
        if (SelectedCrs is null) result.Errors.Add(L("Georef.Simple.Validation.SelectCrs"));
        if (IsConfirmExistingSetupMode)
        {
            if (!ConfirmExistingSetup) result.Errors.Add(L("Georef.Simple.Validation.ConfirmExisting"));
            if (!CurrentState.SurveyPoint.HasEstimatedLocation) result.Errors.Add(L("Georef.Simple.Validation.SurveyUnavailable"));
            if (!CurrentState.ProjectBasePoint.HasEstimatedLocation || !CurrentState.ProjectBasePoint.HasSharedPosition) result.Errors.Add(L("Georef.Simple.Validation.ProjectBasePointUnavailable"));
        }
        else
        {
            if (!TryParseCoordinateValue(ProjectBasePointOffsetXInput, out _)) result.Errors.Add(L("Georef.Simple.Validation.OffsetX"));
            if (!TryParseCoordinateValue(ProjectBasePointOffsetYInput, out _)) result.Errors.Add(L("Georef.Simple.Validation.OffsetY"));
        }

        return result;
    }

    private SplitSurveyProjectBasePointIntent BuildNewProjectSplitIntent()
    {
        ProjectedCoordinate projectOffset = ParseOffsetCoordinate();
        ProjectOrigin surveyOrigin = BuildSurveyOriginOrThrow();
        GeographicCoordinate projectBasePointCoordinate = coordinateTransformer.Unproject(projectOffset, SelectedCrs!.ToReference());
        return new SplitSurveyProjectBasePointIntent
        {
            SelectedCrs = SelectedCrs.ToReference(),
            SharedSurveyOrigin = surveyOrigin,
            SharedSurveyProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            LocalProjectBasePoint = new WorkingProjectBasePointReference
            {
                ProjectCrs = SelectedCrs.ToReference(),
                Origin = new ProjectOrigin { Latitude = projectBasePointCoordinate.Latitude, Longitude = projectBasePointCoordinate.Longitude, ElevationMeters = 0d },
                ProjectedCoordinate = projectOffset,
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = L("Georef.Simple.SetupSource.NewProject")
            },
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = L("Georef.Simple.SetupSource.NewProject"),
            ApplyMode = PlacementApplyMode.ProjectLocation
        };
    }

    private PlacementIntent BuildExistingSetupMetadataIntent()
    {
        return new PlacementIntent
        {
            SelectedCrs = SelectedCrs!.ToReference(),
            SelectedOrigin = new ProjectOrigin { Latitude = CurrentState.SurveyPoint.EstimatedLatitudeDegrees!.Value, Longitude = CurrentState.SurveyPoint.EstimatedLongitudeDegrees!.Value, ElevationMeters = 0d },
            SelectedProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = L("Georef.Simple.SetupSource.Existing"),
            ApplyMode = PlacementApplyMode.MetadataOnly,
            AnchorTarget = PlacementAnchorTarget.SurveyPoint,
            WorkingProjectBasePoint = new WorkingProjectBasePointReference
            {
                ProjectCrs = SelectedCrs.ToReference(),
                Origin = new ProjectOrigin { Latitude = CurrentState.ProjectBasePoint.EstimatedLatitudeDegrees!.Value, Longitude = CurrentState.ProjectBasePoint.EstimatedLongitudeDegrees!.Value, ElevationMeters = 0d },
                ProjectedCoordinate = new ProjectedCoordinate(CurrentState.ProjectBasePoint.SharedEastWestFeet!.Value * FeetToMeters, CurrentState.ProjectBasePoint.SharedNorthSouthFeet!.Value * FeetToMeters),
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = L("Georef.Simple.SetupSource.Existing")
            }
        };
    }

    private void RefreshPlannedSetupRows()
    {
        List<SummaryRow> rows = new List<SummaryRow>
        {
            new SummaryRow(L("Georef.Simple.Planned.Mode"), SetupModeTitle),
            new SummaryRow(L("Georef.Label.SelectedCrs"), SelectedCrsSummary),
            new SummaryRow(L("Georef.Simple.Planned.Survey"), SurveyOriginSummary),
            new SummaryRow(L("Georef.Simple.Planned.Project"), ProjectBasePointOffsetSummary),
            new SummaryRow(L("Georef.Simple.Planned.Apply"), IsNewProjectMode ? L("Georef.Simple.Planned.Apply.NewProject") : L("Georef.Simple.Planned.Apply.Existing"))
        };
        if (IsConfirmExistingSetupMode) rows.Add(new SummaryRow(L("Georef.Simple.Planned.Confirmation"), ConfirmExistingSetup ? L("Common.Yes") : L("Common.No")));
        ReplaceCollection(PlannedSetupRows, rows);
    }

    private void OnSetupChanged(params string[] propertyNames)
    {
        InvalidatePreview();
        RefreshPlannedSetupRows();
        foreach (string propertyName in propertyNames) RaisePropertyChanged(propertyName);
        RaisePropertyChanged(nameof(SetupModeDescription));
        RaisePropertyChanged(nameof(CanGoNext));
        RaisePropertyChanged(nameof(CanApply));
    }

    private bool TryBuildSurveyOrigin(out ProjectOrigin? origin)
    {
        origin = null;
        if (SelectedCrs is null) return false;
        GeographicCoordinate coordinate = coordinateTransformer.Unproject(new ProjectedCoordinate(0d, 0d), SelectedCrs.ToReference());
        origin = new ProjectOrigin { Latitude = coordinate.Latitude, Longitude = coordinate.Longitude, ElevationMeters = 0d };
        return true;
    }

    private ProjectOrigin BuildSurveyOriginOrThrow()
    {
        if (!TryBuildSurveyOrigin(out ProjectOrigin? origin) || origin is null) throw new InvalidOperationException("Select a coordinate reference system before generating georeference preview.");
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

    private bool TryGetOffsetCoordinate(out ProjectedCoordinate offset)
    {
        offset = default;
        if (!TryParseCoordinateValue(ProjectBasePointOffsetXInput, out double x) || !TryParseCoordinateValue(ProjectBasePointOffsetYInput, out double y)) return false;
        offset = new ProjectedCoordinate(x, y);
        return true;
    }

    private ProjectedCoordinate ParseOffsetCoordinate()
    {
        if (!TryGetOffsetCoordinate(out ProjectedCoordinate offset)) throw new InvalidOperationException("Enter valid Project Base Point X/Y offsets before generating georeference preview.");
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

    private GeoreferenceStep GetFurthestNavigableStep() => !CurrentState.IsSupportedDocument ? GeoreferenceStep.CurrentState : BuildSetupValidationResult().IsValid ? GeoreferenceStep.Preview : GeoreferenceStep.ChooseCrs;

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

    private static bool TryParseCoordinateValue(string? text, out double value)
    {
        value = 0d;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) return true;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatCurrentSharedCoordinate(BasePointSnapshot point)
    {
        if (!point.HasSharedPosition) return L("Georef.Simple.CurrentOffsetUnavailable");
        return string.Format(CultureInfo.InvariantCulture, "E {0:F3} m, N {1:F3} m", point.SharedEastWestFeet!.Value * FeetToMeters, point.SharedNorthSouthFeet!.Value * FeetToMeters);
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
        foreach (T value in values) target.Add(value);
    }

    private static string L(string key) => UiLocalizer.Instance.Get(key);

    private void RaisePropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class SetupValidationResult
    {
        public List<string> Errors { get; } = new List<string>();
        public bool IsValid => Errors.Count == 0;
    }
}
