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

namespace RevitGeoSuite.Georeference;

public sealed class GeoreferenceViewModel : INotifyPropertyChanged
{
    private readonly ICoordinateTransformer coordinateTransformer;
    private readonly PlacementPreviewService placementPreviewService;
    private readonly PlacementIntentValidator intentValidator;
    private readonly SiteSelectionService siteSelectionService;
    private readonly PlacementCurrentState placementCurrentState;

    private CrsDefinition? selectedCrs;
    private GeoreferenceStep currentStep;
    private SelectedMapPoint? selectedPoint;
    private SelectedMapPoint? workingProjectBasePoint;
    private SelectedMapPoint? displayedMapPoint;
    private ApplyModeOption? selectedApplyModeOption;
    private SiteSelectionModeOption? selectedSiteSelectionModeOption;
    private AnchorTargetOption? selectedAnchorTargetOption;
    private CaptureTargetOption? selectedCaptureTargetOption;
    private string setupSource;
    private string trueNorthAngleInput;
    private string setupIntentErrorMessage;
    private string knownCoordinateEastingInput;
    private string knownCoordinateNorthingInput;
    private string knownCoordinateErrorMessage;
    private PlacementPreview? preview;
    private PlacementIntent? previewIntent;

    public GeoreferenceViewModel(
        CurrentProjectStateSummary currentState,
        IReadOnlyCollection<CrsDefinition> availableCrs,
        ICoordinateTransformer coordinateTransformer,
        SiteSelectionService siteSelectionService,
        PlacementPreviewService placementPreviewService,
        PlacementIntentValidator? intentValidator = null)
    {
        CurrentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        AvailableCrs = availableCrs?.OrderBy(definition => definition.EpsgCode).ToArray() ?? throw new ArgumentNullException(nameof(availableCrs));
        this.coordinateTransformer = coordinateTransformer ?? throw new ArgumentNullException(nameof(coordinateTransformer));
        this.siteSelectionService = siteSelectionService ?? throw new ArgumentNullException(nameof(siteSelectionService));
        this.placementPreviewService = placementPreviewService ?? throw new ArgumentNullException(nameof(placementPreviewService));
        this.intentValidator = intentValidator ?? new PlacementIntentValidator();
        placementCurrentState = PlacementCurrentStateFactory.Create(CurrentState);
        currentStep = GeoreferenceStep.CurrentState;
        setupSource = string.Empty;
        trueNorthAngleInput = CurrentState.ProjectPosition.AngleDegrees.ToString("F3", CultureInfo.InvariantCulture);
        setupIntentErrorMessage = string.Empty;
        knownCoordinateEastingInput = string.Empty;
        knownCoordinateNorthingInput = string.Empty;
        knownCoordinateErrorMessage = string.Empty;

        CurrentStateRows = new ObservableCollection<SummaryRow>(CreateCurrentStateRows(CurrentState));
        SelectedPointRows = new ObservableCollection<SummaryRow>();
        WorkingProjectBasePointRows = new ObservableCollection<SummaryRow>();
        ApplyModeOptions = new ObservableCollection<ApplyModeOption>(CreateApplyModeOptions());
        SiteSelectionModeOptions = new ObservableCollection<SiteSelectionModeOption>(CreateSiteSelectionModeOptions());
        AnchorTargetOptions = new ObservableCollection<AnchorTargetOption>(CreateAnchorTargetOptions());
        CaptureTargetOptions = new ObservableCollection<CaptureTargetOption>(CreateCaptureTargetOptions());
        PreviewFields = new ObservableCollection<PlacementPreviewField>();
        PreviewWarnings = new ObservableCollection<string>();
        PreviewWhatWillChange = new ObservableCollection<string>();
        PreviewWhatWillNotChange = new ObservableCollection<string>();

        SelectedApplyModeOption = ApplyModeOptions[0];
        SelectedSiteSelectionModeOption = SiteSelectionModeOptions[0];
        SelectedAnchorTargetOption = AnchorTargetOptions[0];
        SelectedCaptureTargetOption = CaptureTargetOptions[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CurrentProjectStateSummary CurrentState { get; }

    public IReadOnlyCollection<CrsDefinition> AvailableCrs { get; }

    public ObservableCollection<SummaryRow> CurrentStateRows { get; }

    public ObservableCollection<SummaryRow> SelectedPointRows { get; }

    public ObservableCollection<SummaryRow> WorkingProjectBasePointRows { get; }

    public ObservableCollection<ApplyModeOption> ApplyModeOptions { get; }

    public ObservableCollection<SiteSelectionModeOption> SiteSelectionModeOptions { get; }

    public ObservableCollection<AnchorTargetOption> AnchorTargetOptions { get; }

    public ObservableCollection<CaptureTargetOption> CaptureTargetOptions { get; }

    public ObservableCollection<PlacementPreviewField> PreviewFields { get; }

    public ObservableCollection<string> PreviewWarnings { get; }

    public ObservableCollection<string> PreviewWhatWillChange { get; }

    public ObservableCollection<string> PreviewWhatWillNotChange { get; }

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
            RaisePropertyChanged(nameof(SelectedCrs));
            RaisePropertyChanged(nameof(SelectedCrsSummary));

            if (selectedPoint is not null && selectedCrs is not null && !selectedPoint.IsKnownCoordinateInput)
            {
                SelectedPoint = BuildPrimaryMapPoint(selectedPoint.Latitude, selectedPoint.Longitude);
            }

            if (workingProjectBasePoint is not null && selectedCrs is not null && !workingProjectBasePoint.IsKnownCoordinateInput)
            {
                WorkingProjectBasePoint = BuildWorkingProjectBasePoint(workingProjectBasePoint.Latitude, workingProjectBasePoint.Longitude, false);
            }

            InvalidatePreview();
            RefreshSetupIntentValidation();
            RaisePropertyChanged(nameof(CanGoNext));
            RaisePropertyChanged(nameof(CanApply));
        }
    }

    public SelectedMapPoint? SelectedPoint
    {
        get => selectedPoint;
        private set
        {
            selectedPoint = value;
            RefreshSelectedPointRows();
            if (selectedPoint is not null && string.IsNullOrWhiteSpace(SetupSource))
            {
                SetupSource = selectedPoint.SourceLabel;
            }

            InvalidatePreview();
            RaisePropertyChanged(nameof(SelectedPoint));
            RaisePropertyChanged(nameof(HasSelectedPoint));
            RaisePropertyChanged(nameof(SelectedPointSummary));
            RefreshSetupIntentValidation();
            RaisePropertyChanged(nameof(CanGoNext));
            RaisePropertyChanged(nameof(CanApply));
        }
    }

    public SelectedMapPoint? WorkingProjectBasePoint
    {
        get => workingProjectBasePoint;
        private set
        {
            workingProjectBasePoint = value;
            RefreshWorkingProjectBasePointRows();
            InvalidatePreview();
            RaisePropertyChanged(nameof(WorkingProjectBasePoint));
            RaisePropertyChanged(nameof(HasWorkingProjectBasePoint));
            RaisePropertyChanged(nameof(HasNoWorkingProjectBasePoint));
            RaisePropertyChanged(nameof(CanClearWorkingProjectBasePoint));
            RaisePropertyChanged(nameof(WorkingProjectBasePointSummary));
        }
    }

    public SelectedMapPoint? DisplayedMapPoint
    {
        get => displayedMapPoint;
        private set
        {
            displayedMapPoint = value;
            RaisePropertyChanged(nameof(DisplayedMapPoint));
        }
    }

    public ApplyModeOption? SelectedApplyModeOption
    {
        get => selectedApplyModeOption;
        set
        {
            if (selectedApplyModeOption == value || value is null)
            {
                return;
            }

            selectedApplyModeOption = value;
            InvalidatePreview();
            RaisePropertyChanged(nameof(SelectedApplyModeOption));
            RaisePropertyChanged(nameof(SelectedApplyModeDescription));
            RaisePropertyChanged(nameof(RequiresTrueNorthAngleInput));
            RefreshSetupIntentValidation();
            RaisePropertyChanged(nameof(CanGoNext));
            RaisePropertyChanged(nameof(CanApply));
        }
    }

    public SiteSelectionModeOption? SelectedSiteSelectionModeOption
    {
        get => selectedSiteSelectionModeOption;
        set
        {
            if (selectedSiteSelectionModeOption == value || value is null)
            {
                return;
            }

            selectedSiteSelectionModeOption = value;
            KnownCoordinateErrorMessage = string.Empty;
            InvalidatePreview();
            RaisePropertyChanged(nameof(SelectedSiteSelectionModeOption));
            RaisePropertyChanged(nameof(SelectedSiteSelectionModeDescription));
            RaisePropertyChanged(nameof(IsMapSelectionMode));
            RaisePropertyChanged(nameof(IsKnownCoordinateMode));
        }
    }

    public AnchorTargetOption? SelectedAnchorTargetOption
    {
        get => selectedAnchorTargetOption;
        set
        {
            if (selectedAnchorTargetOption == value || value is null)
            {
                return;
            }

            selectedAnchorTargetOption = value;
            InvalidatePreview();
            RaisePropertyChanged(nameof(SelectedAnchorTargetOption));
            RaisePropertyChanged(nameof(SelectedAnchorTargetDescription));
        }
    }

    public CaptureTargetOption? SelectedCaptureTargetOption
    {
        get => selectedCaptureTargetOption;
        set
        {
            if (selectedCaptureTargetOption == value || value is null)
            {
                return;
            }

            selectedCaptureTargetOption = value;
            KnownCoordinateErrorMessage = string.Empty;
            RaisePropertyChanged(nameof(SelectedCaptureTargetOption));
            RaisePropertyChanged(nameof(SelectedCaptureTargetDescription));
            RaisePropertyChanged(nameof(IsCapturingPrimaryApplyAnchor));
            RaisePropertyChanged(nameof(IsCapturingWorkingProjectBasePoint));
        }
    }

    public string SetupSource
    {
        get => setupSource;
        set
        {
            if (string.Equals(setupSource, value, StringComparison.Ordinal))
            {
                return;
            }

            setupSource = value ?? string.Empty;
            InvalidatePreview();
            RaisePropertyChanged(nameof(SetupSource));
            RefreshSetupIntentValidation();
            RaisePropertyChanged(nameof(CanGoNext));
            RaisePropertyChanged(nameof(CanApply));
        }
    }

    public string TrueNorthAngleInput
    {
        get => trueNorthAngleInput;
        set
        {
            if (string.Equals(trueNorthAngleInput, value, StringComparison.Ordinal))
            {
                return;
            }

            trueNorthAngleInput = value ?? string.Empty;
            InvalidatePreview();
            RaisePropertyChanged(nameof(TrueNorthAngleInput));
            RefreshSetupIntentValidation();
            RaisePropertyChanged(nameof(CanGoNext));
            RaisePropertyChanged(nameof(CanApply));
        }
    }

    public string KnownCoordinateEastingInput
    {
        get => knownCoordinateEastingInput;
        set
        {
            if (string.Equals(knownCoordinateEastingInput, value, StringComparison.Ordinal))
            {
                return;
            }

            knownCoordinateEastingInput = value ?? string.Empty;
            KnownCoordinateErrorMessage = string.Empty;
            InvalidatePreview();
            RaisePropertyChanged(nameof(KnownCoordinateEastingInput));
        }
    }

    public string KnownCoordinateNorthingInput
    {
        get => knownCoordinateNorthingInput;
        set
        {
            if (string.Equals(knownCoordinateNorthingInput, value, StringComparison.Ordinal))
            {
                return;
            }

            knownCoordinateNorthingInput = value ?? string.Empty;
            KnownCoordinateErrorMessage = string.Empty;
            InvalidatePreview();
            RaisePropertyChanged(nameof(KnownCoordinateNorthingInput));
        }
    }

    public string SetupIntentErrorMessage
    {
        get => setupIntentErrorMessage;
        private set
        {
            if (string.Equals(setupIntentErrorMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            setupIntentErrorMessage = value;
            RaisePropertyChanged(nameof(SetupIntentErrorMessage));
            RaisePropertyChanged(nameof(HasSetupIntentErrorMessage));
        }
    }

    public string KnownCoordinateErrorMessage
    {
        get => knownCoordinateErrorMessage;
        private set
        {
            if (string.Equals(knownCoordinateErrorMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            knownCoordinateErrorMessage = value;
            RaisePropertyChanged(nameof(KnownCoordinateErrorMessage));
            RaisePropertyChanged(nameof(HasKnownCoordinateErrorMessage));
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

    public bool HasSelectedPoint => SelectedPoint is not null;

    public bool HasWorkingProjectBasePoint => WorkingProjectBasePoint is not null;

    public bool HasNoWorkingProjectBasePoint => !HasWorkingProjectBasePoint;

    public bool HasSiteLocation => CurrentState.SiteLatitudeDegrees.HasValue && CurrentState.SiteLongitudeDegrees.HasValue;

    public bool HasProjectBasePointLocation => WorkingProjectBasePoint is not null
        || CurrentState.StoredWorkingProjectBasePoint?.IsValid == true
        || CurrentState.ProjectBasePoint.HasEstimatedLocation;

    public bool HasSetupIntentErrorMessage => !string.IsNullOrWhiteSpace(SetupIntentErrorMessage);

    public bool HasKnownCoordinateErrorMessage => !string.IsNullOrWhiteSpace(KnownCoordinateErrorMessage);

    public bool HasPreview => Preview is not null;

    public bool HasPreviewWarnings => PreviewWarnings.Count > 0;

    public bool IsMapSelectionMode => SelectedSiteSelectionModeOption?.Mode == SiteSelectionInputMode.MapPoint;

    public bool IsKnownCoordinateMode => SelectedSiteSelectionModeOption?.Mode == SiteSelectionInputMode.KnownCoordinates;

    public bool IsCapturingPrimaryApplyAnchor => SelectedCaptureTargetOption?.Target != ReferenceCaptureTarget.WorkingProjectBasePoint;

    public bool IsCapturingWorkingProjectBasePoint => SelectedCaptureTargetOption?.Target == ReferenceCaptureTarget.WorkingProjectBasePoint;

    public bool CanClearWorkingProjectBasePoint => HasWorkingProjectBasePoint;

    public string WindowTitle => "Georeference Setup";

    public string StepTitle => CurrentStep switch
    {
        GeoreferenceStep.CurrentState => "1. Current State",
        GeoreferenceStep.ChooseCrs => "2. Choose CRS",
        GeoreferenceStep.SelectPoint => "3. Select Site Reference",
        GeoreferenceStep.ReviewPoint => "4. Review Selected Point",
        GeoreferenceStep.SetupIntent => "5. Choose Setup Intent",
        GeoreferenceStep.Preview => "6. Preview Changes",
        _ => string.Empty
    };

    public string StepDescription => CurrentStep switch
    {
        GeoreferenceStep.CurrentState => "Review the current project location status before any georeference workflow continues.",
        GeoreferenceStep.ChooseCrs => "Select the project CRS before choosing a primary anchor and an optional working Project Base Point.",
        GeoreferenceStep.SelectPoint => "Use the map or enter known Easting/Northing coordinates in the selected CRS to define the primary apply anchor and, if needed, a separate working Project Base Point.",
        GeoreferenceStep.ReviewPoint => "Review the selected geographic and projected coordinates that will feed the apply step and later working-coordinate workflows.",
        GeoreferenceStep.SetupIntent => "Choose what the apply step should change. The next screen is the final confirmation gate before any document modification.",
        GeoreferenceStep.Preview => "Review the current vs proposed state, then confirm before applying changes to the Revit document.",
        _ => string.Empty
    };

    public string ExistingSetupMessage => CurrentState.ExistingSetupMessage;

    public bool HasExistingSetupMessage => !string.IsNullOrWhiteSpace(ExistingSetupMessage);

    public string StatusMessage => CurrentState.StatusMessage;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string SelectedCrsSummary => SelectedCrs is null
        ? "No CRS selected yet."
        : $"EPSG:{SelectedCrs.EpsgCode}  {SelectedCrs.Name}";

    public string SelectedApplyModeDescription => SelectedApplyModeOption?.Description ?? string.Empty;

    public string SelectedSiteSelectionModeDescription => SelectedSiteSelectionModeOption?.Description ?? string.Empty;

    public string SelectedAnchorTargetDescription => SelectedAnchorTargetOption?.Description ?? string.Empty;

    public string SelectedCaptureTargetDescription => SelectedCaptureTargetOption?.Description ?? string.Empty;

    public string PreviewPersistenceSummary => Preview?.PersistenceSummary ?? string.Empty;

    public string PreviewChangeImpactSummary => Preview?.ChangeImpactSummary ?? string.Empty;

    public string PreviewConfidenceSummary => Preview?.ConfidenceSummary ?? string.Empty;

    public string SelectedPointSummary => SelectedPoint is null
        ? "Not selected"
        : $"{SelectedPoint.Latitude:F6}, {SelectedPoint.Longitude:F6}";

    public string WorkingProjectBasePointSummary => WorkingProjectBasePoint is null
        ? CurrentState.StoredWorkingProjectBasePoint is null
            ? "Not selected"
            : FormatWorkingProjectBasePoint(CurrentState.StoredWorkingProjectBasePoint)
        : $"{WorkingProjectBasePoint.Latitude:F6}, {WorkingProjectBasePoint.Longitude:F6}";

    public string ProjectBasePointZoomButtonText => WorkingProjectBasePoint is not null || CurrentState.StoredWorkingProjectBasePoint?.IsValid == true
        ? "Zoom To Working Project Base Point"
        : "Zoom To Project Base Point";

    public string NextButtonText => CurrentStep == GeoreferenceStep.SetupIntent ? "Preview" : "Next";

    public bool ShowNextButton => CurrentStep != GeoreferenceStep.Preview;

    public bool CanGoBack => CurrentStep != GeoreferenceStep.CurrentState;

    public bool RequiresTrueNorthAngleInput => SelectedApplyModeOption?.Mode == PlacementApplyMode.ProjectLocationAndAngle;

    public bool CanApply => CurrentStep == GeoreferenceStep.Preview
        && Preview?.IsReadyToApply == true
        && CurrentState.IsSupportedDocument
        && !CurrentState.IsReadOnly;

    public bool CanGoNext => CurrentStep switch
    {
        GeoreferenceStep.CurrentState => CurrentState.IsSupportedDocument,
        GeoreferenceStep.ChooseCrs => SelectedCrs is not null,
        GeoreferenceStep.SelectPoint => SelectedPoint is not null,
        GeoreferenceStep.ReviewPoint => SelectedPoint is not null,
        GeoreferenceStep.SetupIntent => BuildIntentValidationResult().IsValid,
        GeoreferenceStep.Preview => false,
        _ => false
    };

    public bool IsCurrentStateStepVisible => CurrentStep == GeoreferenceStep.CurrentState;

    public bool IsChooseCrsStepVisible => CurrentStep == GeoreferenceStep.ChooseCrs;

    public bool IsSelectPointStepVisible => CurrentStep == GeoreferenceStep.SelectPoint;

    public bool IsReviewPointStepVisible => CurrentStep == GeoreferenceStep.ReviewPoint;

    public bool IsSetupIntentStepVisible => CurrentStep == GeoreferenceStep.SetupIntent;

    public bool IsPreviewStepVisible => CurrentStep == GeoreferenceStep.Preview;

    public PlacementIntent GetApplyIntent()
    {
        if (!CanApply || previewIntent is null)
        {
            throw new InvalidOperationException("Generate a valid preview in an editable project before applying changes.");
        }

        return previewIntent;
    }

    public void GoNext()
    {
        if (!CanGoNext)
        {
            return;
        }

        if (CurrentStep == GeoreferenceStep.SetupIntent)
        {
            BuildPreview();
            if (Preview is not null)
            {
                CurrentStep = GeoreferenceStep.Preview;
            }

            return;
        }

        CurrentStep = CurrentStep switch
        {
            GeoreferenceStep.CurrentState => GeoreferenceStep.ChooseCrs,
            GeoreferenceStep.ChooseCrs => GeoreferenceStep.SelectPoint,
            GeoreferenceStep.SelectPoint => GeoreferenceStep.ReviewPoint,
            GeoreferenceStep.ReviewPoint => GeoreferenceStep.SetupIntent,
            _ => CurrentStep
        };
    }

    public void GoBack()
    {
        if (!CanGoBack)
        {
            return;
        }

        CurrentStep = CurrentStep switch
        {
            GeoreferenceStep.ChooseCrs => GeoreferenceStep.CurrentState,
            GeoreferenceStep.SelectPoint => GeoreferenceStep.ChooseCrs,
            GeoreferenceStep.ReviewPoint => GeoreferenceStep.SelectPoint,
            GeoreferenceStep.SetupIntent => GeoreferenceStep.ReviewPoint,
            GeoreferenceStep.Preview => GeoreferenceStep.SetupIntent,
            _ => CurrentStep
        };
    }

    public void SetSelectedMapPoint(double latitude, double longitude)
    {
        if (SelectedCrs is null)
        {
            return;
        }

        SelectedMapPoint capturedPoint = IsCapturingWorkingProjectBasePoint
            ? BuildWorkingProjectBasePoint(latitude, longitude, false)
            : BuildPrimaryMapPoint(latitude, longitude);

        ApplyCapturedPoint(capturedPoint);
    }

    public bool TryUseKnownCoordinates()
    {
        if (SelectedCrs is null)
        {
            KnownCoordinateErrorMessage = "Select a coordinate reference system before entering known coordinates.";
            return false;
        }

        if (!TryParseCoordinateValue(KnownCoordinateEastingInput, out double easting))
        {
            KnownCoordinateErrorMessage = "Enter a valid Easting value in the selected CRS.";
            return false;
        }

        if (!TryParseCoordinateValue(KnownCoordinateNorthingInput, out double northing))
        {
            KnownCoordinateErrorMessage = "Enter a valid Northing value in the selected CRS.";
            return false;
        }

        ProjectedCoordinate projectedCoordinate = new ProjectedCoordinate(easting, northing);
        GeographicCoordinate geographicCoordinate = coordinateTransformer.Unproject(projectedCoordinate, SelectedCrs.ToReference());
        SelectedMapPoint capturedPoint = IsCapturingWorkingProjectBasePoint
            ? BuildWorkingProjectBasePoint(geographicCoordinate.Latitude, geographicCoordinate.Longitude, true, projectedCoordinate)
            : BuildPrimaryKnownCoordinatePoint(geographicCoordinate.Latitude, geographicCoordinate.Longitude, projectedCoordinate);

        ApplyCapturedPoint(capturedPoint);
        KnownCoordinateErrorMessage = string.Empty;
        return true;
    }

    public void ClearWorkingProjectBasePoint()
    {
        bool wasDisplayed = ReferenceEquals(DisplayedMapPoint, WorkingProjectBasePoint);
        WorkingProjectBasePoint = null;
        if (wasDisplayed)
        {
            DisplayedMapPoint = SelectedPoint;
        }
    }
    public bool TryGetProjectBasePointLocation(out double latitude, out double longitude)
    {
        if (WorkingProjectBasePoint is not null)
        {
            latitude = WorkingProjectBasePoint.Latitude;
            longitude = WorkingProjectBasePoint.Longitude;
            return true;
        }

        if (CurrentState.StoredWorkingProjectBasePoint?.IsValid == true)
        {
            latitude = CurrentState.StoredWorkingProjectBasePoint.Origin!.Latitude;
            longitude = CurrentState.StoredWorkingProjectBasePoint.Origin.Longitude;
            return true;
        }

        if (CurrentState.ProjectBasePoint.HasEstimatedLocation)
        {
            latitude = CurrentState.ProjectBasePoint.EstimatedLatitudeDegrees!.Value;
            longitude = CurrentState.ProjectBasePoint.EstimatedLongitudeDegrees!.Value;
            return true;
        }

        latitude = 0d;
        longitude = 0d;
        return false;
    }

    private void ApplyCapturedPoint(SelectedMapPoint capturedPoint)
    {
        if (IsCapturingWorkingProjectBasePoint)
        {
            WorkingProjectBasePoint = capturedPoint;
        }
        else
        {
            SelectedPoint = capturedPoint;
        }

        DisplayedMapPoint = capturedPoint;
    }

    private SelectedMapPoint BuildPrimaryMapPoint(double latitude, double longitude)
    {
        ProjectedCoordinate projectedCoordinate = coordinateTransformer.Project(
            new GeographicCoordinate(latitude, longitude),
            SelectedCrs!.ToReference());

        return new SelectedMapPoint
        {
            Latitude = latitude,
            Longitude = longitude,
            ProjectedCoordinate = projectedCoordinate,
            SourceLabel = "Selected from OSM map",
            ConfidenceLabel = "Approximate (map-based selection)",
            ConfidenceLevel = GeoConfidenceLevel.Approximate,
            AnchorTarget = PlacementAnchorTarget.Unspecified,
            IsKnownCoordinateInput = false
        };
    }

    private SelectedMapPoint BuildPrimaryKnownCoordinatePoint(double latitude, double longitude, ProjectedCoordinate projectedCoordinate)
    {
        PlacementAnchorTarget anchorTarget = SelectedAnchorTargetOption?.Target ?? PlacementAnchorTarget.SurveyPoint;
        string anchorTitle = SelectedAnchorTargetOption?.Title ?? "Survey Point";

        return new SelectedMapPoint
        {
            Latitude = latitude,
            Longitude = longitude,
            ProjectedCoordinate = projectedCoordinate,
            SourceLabel = $"Entered EPSG:{SelectedCrs!.EpsgCode} coordinates for {anchorTitle}",
            ConfidenceLabel = "Verified (user-entered CRS coordinates)",
            ConfidenceLevel = GeoConfidenceLevel.Verified,
            AnchorTarget = anchorTarget,
            IsKnownCoordinateInput = true
        };
    }

    private SelectedMapPoint BuildWorkingProjectBasePoint(double latitude, double longitude, bool isKnownCoordinateInput, ProjectedCoordinate? knownProjectedCoordinate = null)
    {
        ProjectedCoordinate projectedCoordinate = knownProjectedCoordinate
            ?? coordinateTransformer.Project(new GeographicCoordinate(latitude, longitude), SelectedCrs!.ToReference());
        string sourceLabel = isKnownCoordinateInput
            ? $"Entered EPSG:{SelectedCrs!.EpsgCode} coordinates for Working Project Base Point"
            : "Selected from OSM map for Working Project Base Point";
        string confidenceLabel = isKnownCoordinateInput
            ? "Verified (user-entered CRS coordinates)"
            : "Approximate (map-based selection)";
        GeoConfidenceLevel confidence = isKnownCoordinateInput ? GeoConfidenceLevel.Verified : GeoConfidenceLevel.Approximate;

        return new SelectedMapPoint
        {
            Latitude = latitude,
            Longitude = longitude,
            ProjectedCoordinate = projectedCoordinate,
            SourceLabel = sourceLabel,
            ConfidenceLabel = confidenceLabel,
            ConfidenceLevel = confidence,
            AnchorTarget = PlacementAnchorTarget.ProjectBasePoint,
            IsKnownCoordinateInput = isKnownCoordinateInput
        };
    }

    private void BuildPreview()
    {
        PlacementIntentValidationResult validationResult = BuildIntentValidationResult();
        SetupIntentErrorMessage = string.Join(Environment.NewLine, validationResult.Errors);
        if (!validationResult.IsValid)
        {
            previewIntent = null;
            Preview = null;
            ResetPreviewCollections();
            return;
        }

        PlacementIntent intent = BuildIntent();
        previewIntent = intent;
        Preview = placementPreviewService.CreatePreview(placementCurrentState, intent);
        ReplaceCollection(PreviewFields, Preview.Fields);
        ReplaceCollection(PreviewWarnings, Preview.Warnings);
        ReplaceCollection(PreviewWhatWillChange, Preview.WhatWillChange);
        ReplaceCollection(PreviewWhatWillNotChange, Preview.WhatWillNotChange);
        RaisePropertyChanged(nameof(CanApply));
    }

    private void RefreshSelectedPointRows()
    {
        SelectedPointRows.Clear();
        if (SelectedPoint is null)
        {
            return;
        }

        SelectedPointRows.Add(new SummaryRow("Latitude / Longitude", $"{SelectedPoint.Latitude:F6}, {SelectedPoint.Longitude:F6}"));
        SelectedPointRows.Add(new SummaryRow("Projected Easting", $"{SelectedPoint.ProjectedCoordinate.Easting:F4} m"));
        SelectedPointRows.Add(new SummaryRow("Projected Northing", $"{SelectedPoint.ProjectedCoordinate.Northing:F4} m"));
        if (SelectedPoint.AnchorTarget != PlacementAnchorTarget.Unspecified)
        {
            SelectedPointRows.Add(new SummaryRow("Anchor Target", FormatAnchorTarget(SelectedPoint.AnchorTarget)));
        }

        SelectedPointRows.Add(new SummaryRow("Source", SelectedPoint.SourceLabel));
        SelectedPointRows.Add(new SummaryRow("Confidence", SelectedPoint.ConfidenceLabel));
        if (SelectedCrs is not null)
        {
            SelectedPointRows.Add(new SummaryRow("Selected CRS", $"EPSG:{SelectedCrs.EpsgCode}  {SelectedCrs.Name}"));
        }
    }

    private void RefreshWorkingProjectBasePointRows()
    {
        WorkingProjectBasePointRows.Clear();
        if (WorkingProjectBasePoint is null)
        {
            return;
        }

        WorkingProjectBasePointRows.Add(new SummaryRow("Latitude / Longitude", $"{WorkingProjectBasePoint.Latitude:F6}, {WorkingProjectBasePoint.Longitude:F6}"));
        WorkingProjectBasePointRows.Add(new SummaryRow("Projected Easting", $"{WorkingProjectBasePoint.ProjectedCoordinate.Easting:F4} m"));
        WorkingProjectBasePointRows.Add(new SummaryRow("Projected Northing", $"{WorkingProjectBasePoint.ProjectedCoordinate.Northing:F4} m"));
        WorkingProjectBasePointRows.Add(new SummaryRow("Role", "Working Project Base Point"));
        WorkingProjectBasePointRows.Add(new SummaryRow("Source", WorkingProjectBasePoint.SourceLabel));
        WorkingProjectBasePointRows.Add(new SummaryRow("Confidence", WorkingProjectBasePoint.ConfidenceLabel));
        if (SelectedCrs is not null)
        {
            WorkingProjectBasePointRows.Add(new SummaryRow("Selected CRS", $"EPSG:{SelectedCrs.EpsgCode}  {SelectedCrs.Name}"));
        }
    }

    private void RefreshSetupIntentValidation()
    {
        if (CurrentStep != GeoreferenceStep.SetupIntent)
        {
            SetupIntentErrorMessage = string.Empty;
            return;
        }

        PlacementIntentValidationResult validationResult = BuildIntentValidationResult();
        SetupIntentErrorMessage = string.Join(Environment.NewLine, validationResult.Errors);
    }

    private PlacementIntentValidationResult BuildIntentValidationResult()
    {
        if (SelectedCrs is null || SelectedPoint is null || SelectedApplyModeOption is null)
        {
            PlacementIntentValidationResult missingResult = new PlacementIntentValidationResult();
            if (SelectedCrs is null)
            {
                missingResult.Errors.Add("Select a coordinate reference system before generating a preview.");
            }

            if (SelectedPoint is null)
            {
                missingResult.Errors.Add("Select a primary map point or enter known coordinates before generating a preview.");
            }

            if (SelectedApplyModeOption is null)
            {
                missingResult.Errors.Add("Choose a setup intent before generating a preview.");
            }

            return missingResult;
        }

        return intentValidator.Validate(BuildIntent());
    }

    private PlacementIntent BuildIntent()
    {
        double? trueNorthAngle = ParseTrueNorthAngle();
        return siteSelectionService.CreateIntent(
            SelectedCrs!,
            SelectedPoint!,
            SelectedApplyModeOption!.Mode,
            SetupSource,
            trueNorthAngle,
            WorkingProjectBasePoint);
    }

    private double? ParseTrueNorthAngle()
    {
        if (!RequiresTrueNorthAngleInput)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(TrueNorthAngleInput))
        {
            return null;
        }

        if (double.TryParse(TrueNorthAngleInput, NumberStyles.Float, CultureInfo.CurrentCulture, out double currentCultureValue))
        {
            return currentCultureValue;
        }

        if (double.TryParse(TrueNorthAngleInput, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariantValue))
        {
            return invariantValue;
        }

        return null;
    }

    private void InvalidatePreview()
    {
        previewIntent = null;
        Preview = null;
        ResetPreviewCollections();
        RaisePropertyChanged(nameof(CanApply));
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

    private void ResetPreviewCollections()
    {
        PreviewFields.Clear();
        PreviewWarnings.Clear();
        PreviewWhatWillChange.Clear();
        PreviewWhatWillNotChange.Clear();
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
        {
            target.Add(value);
        }
    }

    private static IEnumerable<ApplyModeOption> CreateApplyModeOptions()
    {
        yield return new ApplyModeOption
        {
            Mode = PlacementApplyMode.MetadataOnly,
            Title = "Metadata Only",
            Description = "Save canonical CRS, origin, confidence, and provenance without changing Revit project location values."
        };
        yield return new ApplyModeOption
        {
            Mode = PlacementApplyMode.ProjectLocation,
            Title = "Project Location",
            Description = "Update Revit project location values from the selected point while keeping the current true north angle."
        };
        yield return new ApplyModeOption
        {
            Mode = PlacementApplyMode.ProjectLocationAndAngle,
            Title = "Project Location + True North",
            Description = "Update Revit project location values and true north. Building geometry is still not rotated."
        };
    }

    private static IEnumerable<SiteSelectionModeOption> CreateSiteSelectionModeOptions()
    {
        yield return new SiteSelectionModeOption
        {
            Mode = SiteSelectionInputMode.MapPoint,
            Title = "Map Point",
            Description = "Pan, zoom, and click on the map to choose the approximate site reference."
        };
        yield return new SiteSelectionModeOption
        {
            Mode = SiteSelectionInputMode.KnownCoordinates,
            Title = "Known CRS Coordinates",
            Description = "Enter explicit Easting/Northing values in the selected CRS and convert them into a geographic point for review."
        };
    }

    private static IEnumerable<AnchorTargetOption> CreateAnchorTargetOptions()
    {
        yield return new AnchorTargetOption
        {
            Target = PlacementAnchorTarget.SurveyPoint,
            Title = "Survey Point",
            Description = "Treat the primary CRS coordinates as the intended Survey Point location for the apply step."
        };
        yield return new AnchorTargetOption
        {
            Target = PlacementAnchorTarget.ProjectBasePoint,
            Title = "Project Base Point",
            Description = "Treat the primary CRS coordinates as the intended Project Base Point location for the apply step."
        };
    }

    private static IEnumerable<CaptureTargetOption> CreateCaptureTargetOptions()
    {
        yield return new CaptureTargetOption
        {
            Target = ReferenceCaptureTarget.PrimaryApplyAnchor,
            Title = "Primary Apply Anchor",
            Description = "Capture the main point that the Revit apply step will use as its anchor."
        };
        yield return new CaptureTargetOption
        {
            Target = ReferenceCaptureTarget.WorkingProjectBasePoint,
            Title = "Working Project Base Point",
            Description = "Capture an optional secondary Project Base Point reference for later import/export workflows."
        };
    }

    private static IEnumerable<SummaryRow> CreateCurrentStateRows(CurrentProjectStateSummary summary)
    {
        yield return new SummaryRow("Document", summary.DocumentTitle);
        yield return new SummaryRow("Stored Geo Metadata", summary.HasStoredGeoInfo ? "Yes" : "No");
        yield return new SummaryRow("Read-Only", summary.IsReadOnly ? "Yes" : "No");
        if (summary.SiteLatitudeDegrees.HasValue && summary.SiteLongitudeDegrees.HasValue)
        {
            yield return new SummaryRow("Site Location", $"{summary.SiteLatitudeDegrees.Value:F6}, {summary.SiteLongitudeDegrees.Value:F6}");
        }

        if (summary.SiteTimeZoneHours.HasValue)
        {
            yield return new SummaryRow("Site Time Zone", $"UTC{summary.SiteTimeZoneHours.Value:+0.##;-0.##;0}");
        }

        yield return new SummaryRow("Project Position", FormatLengthPair(summary.ProjectPosition.EastWestFeet, summary.ProjectPosition.NorthSouthFeet));
        yield return new SummaryRow("Project Elevation", FormatLength(summary.ProjectPosition.ElevationFeet));
        yield return new SummaryRow("True North Angle", $"{summary.ProjectPosition.AngleDegrees:F3}°");
        yield return new SummaryRow(summary.SurveyPoint.Name, FormatPoint(summary.SurveyPoint));
        yield return new SummaryRow(summary.ProjectBasePoint.Name, FormatPoint(summary.ProjectBasePoint));
        if (summary.StoredCrs is not null)
        {
            yield return new SummaryRow("Stored CRS", $"EPSG:{summary.StoredCrs.EpsgCode}  {summary.StoredCrs.NameSnapshot}");
        }

        if (summary.StoredOrigin is not null)
        {
            yield return new SummaryRow("Stored Origin", $"{summary.StoredOrigin.Latitude:F6}, {summary.StoredOrigin.Longitude:F6}, elev {summary.StoredOrigin.ElevationMeters:F3} m");
        }

        if (summary.StoredWorkingProjectBasePoint?.IsValid == true)
        {
            yield return new SummaryRow("Stored Working Project Base Point", FormatWorkingProjectBasePoint(summary.StoredWorkingProjectBasePoint));
        }

        if (summary.StoredConfidence.HasValue)
        {
            yield return new SummaryRow("Stored Confidence", summary.StoredConfidence.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(summary.SetupSource))
        {
            yield return new SummaryRow("Setup Source", summary.SetupSource);
        }
    }

    private static string FormatLengthPair(double eastWestFeet, double northSouthFeet)
    {
        return $"EW {FormatLength(eastWestFeet)}, NS {FormatLength(northSouthFeet)}";
    }

    private static string FormatLength(double feet)
    {
        return $"{feet:F3} ft / {feet * 0.3048:F3} m";
    }

    private static string FormatPoint(BasePointSnapshot point)
    {
        return $"X {point.XFeet:F3} ft, Y {point.YFeet:F3} ft, Z {point.ZFeet:F3} ft";
    }

    private static string FormatAnchorTarget(PlacementAnchorTarget anchorTarget)
    {
        return anchorTarget switch
        {
            PlacementAnchorTarget.SurveyPoint => "Survey Point",
            PlacementAnchorTarget.ProjectBasePoint => "Project Base Point",
            _ => "Survey Point"
        };
    }

    private static string FormatWorkingProjectBasePoint(WorkingProjectBasePointReference workingProjectBasePoint)
    {
        return $"EPSG:{workingProjectBasePoint.ProjectCrs!.EpsgCode} / E {workingProjectBasePoint.ProjectedCoordinate!.Value.Easting:F3} m, N {workingProjectBasePoint.ProjectedCoordinate.Value.Northing:F3} m / Lat {workingProjectBasePoint.Origin!.Latitude:F6}, Lon {workingProjectBasePoint.Origin.Longitude:F6}";
    }

    private void RaiseStepProperties()
    {
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
        RaisePropertyChanged(nameof(IsSelectPointStepVisible));
        RaisePropertyChanged(nameof(IsReviewPointStepVisible));
        RaisePropertyChanged(nameof(IsSetupIntentStepVisible));
        RaisePropertyChanged(nameof(IsPreviewStepVisible));
    }

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}





