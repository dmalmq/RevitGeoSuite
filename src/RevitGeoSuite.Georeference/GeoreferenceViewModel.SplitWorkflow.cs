using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.SharedUI.Localization;

namespace RevitGeoSuite.Georeference;

public sealed partial class GeoreferenceViewModel
{
    private readonly SplitSurveyProjectBasePointPreviewService splitSurveyProjectBasePointPreviewService;
    private SplitSurveyProjectBasePointIntent? splitPreviewIntent;
    private WorkflowModeOption? selectedWorkflowModeOption;

    public ObservableCollection<WorkflowModeOption> WorkflowModeOptions { get; } = new();

    public WorkflowModeOption? SelectedWorkflowModeOption
    {
        get => selectedWorkflowModeOption;
        set
        {
            if (selectedWorkflowModeOption == value || value is null)
            {
                return;
            }

            selectedWorkflowModeOption = value;
            EnsureValidApplyModeSelectionForCurrentWorkflow();
            InitializeSplitWorkflowDefaults();
            InitializeQuickSetupDefaults();
            InvalidatePreview();
            RefreshSetupIntentValidation();
            RaiseWorkflowModeProperties();
        }
    }

    public bool IsSplitWorkflowMode => SelectedWorkflowModeOption?.Mode == GeoreferenceWorkflowMode.SplitLocalProjectBasePointAndSharedSurvey;

    public bool IsStandardWorkflowMode => !IsSplitWorkflowMode && !IsQuickSetupMode;

    public IEnumerable<ApplyModeOption> AvailableApplyModeOptions => IsSplitWorkflowMode
        ? ApplyModeOptions.Where(option => option.Mode != PlacementApplyMode.MetadataOnly)
        : ApplyModeOptions;

    public string SelectedWorkflowModeDescription => SelectedWorkflowModeOption?.Description ?? string.Empty;

    public bool CanSelectPrimaryAnchorTarget => IsCapturingPrimaryApplyAnchor && IsStandardWorkflowMode;

    public string SelectPointIntroText => IsSplitWorkflowMode
        ? UiLocalizer.Instance.Get("Georef.SelectPointIntro.Split")
        : UiLocalizer.Instance.Get("Georef.SelectPointIntro.Standard");

    public string ReviewPrimarySectionTitle => IsSplitWorkflowMode ? UiLocalizer.Instance.Get("Georef.Review.PrimaryTitle.Split") : UiLocalizer.Instance.Get("Georef.Review.PrimaryTitle.Standard");

    public string ReviewPrimarySectionDescription => IsSplitWorkflowMode
        ? UiLocalizer.Instance.Get("Georef.Review.PrimaryDescription.Split")
        : UiLocalizer.Instance.Get("Georef.Review.PrimaryDescription.Standard");

    public string ReviewWorkingSectionTitle => IsSplitWorkflowMode ? UiLocalizer.Instance.Get("Georef.Review.WorkingTitle.Split") : UiLocalizer.Instance.Get("Georef.Review.WorkingTitle.Standard");

    public string ReviewWorkingSectionDescription => IsSplitWorkflowMode
        ? UiLocalizer.Instance.Get("Georef.Review.WorkingDescription.Split")
        : UiLocalizer.Instance.Get("Georef.Review.WorkingDescription.Standard");

    public string SetupIntentPrimaryLabel => IsSplitWorkflowMode ? UiLocalizer.Instance.Get("Georef.SetupIntent.PrimaryLabel.Split") : UiLocalizer.Instance.Get("Georef.SetupIntent.PrimaryLabel.Standard");

    public string SetupIntentWorkingLabel => IsSplitWorkflowMode ? UiLocalizer.Instance.Get("Georef.SetupIntent.WorkingLabel.Split") : UiLocalizer.Instance.Get("Georef.SetupIntent.WorkingLabel.Standard");

    public string SetupIntentHelpText => IsSplitWorkflowMode
        ? UiLocalizer.Instance.Get("Georef.StepDescription.SetupIntent.Split")
        : UiLocalizer.Instance.Get("Georef.StepDescription.SetupIntent.Standard");

    public string SplitWorkflowSummary => IsSplitWorkflowMode
        ? UiLocalizer.Instance.Get("Georef.SplitWorkflowSummary")
        : "";

    public SplitSurveyProjectBasePointIntent GetSplitApplyIntent()
    {
        if (!CanApply || splitPreviewIntent is null)
        {
            throw new InvalidOperationException("Generate a valid split-workflow preview in an editable project before applying changes.");
        }

        return splitPreviewIntent;
    }

    private void InitializeWorkflowModes()
    {
        WorkflowModeOptions.Clear();
        foreach (WorkflowModeOption option in CreateWorkflowModeOptions())
        {
            WorkflowModeOptions.Add(option);
        }

        selectedWorkflowModeOption = WorkflowModeOptions.First(option => option.Mode == GeoreferenceWorkflowMode.Standard);
    }

    private void EnsureValidApplyModeSelectionForCurrentWorkflow()
    {
        if (SelectedApplyModeOption is null)
        {
            return;
        }

        if ((IsSplitWorkflowMode || IsQuickSetupMode) && SelectedApplyModeOption.Mode == PlacementApplyMode.MetadataOnly)
        {
            selectedApplyModeOption = ApplyModeOptions.First(option => option.Mode == PlacementApplyMode.ProjectLocation);
            RaisePropertyChanged(nameof(SelectedApplyModeOption));
            RaisePropertyChanged(nameof(SelectedApplyModeDescription));
            RaisePropertyChanged(nameof(RequiresTrueNorthAngleInput));
        }
    }

    private void InitializeSplitWorkflowDefaults()
    {
        if (!IsSplitWorkflowMode || SelectedCrs is null)
        {
            return;
        }

        if (WorkingProjectBasePoint is null && TryBuildCurrentRevitWorkingProjectBasePoint(out SelectedMapPoint point))
        {
            WorkingProjectBasePoint = point;
            if (DisplayedMapPoint is null)
            {
                DisplayedMapPoint = point;
            }
        }
    }

    private PlacementIntentValidationResult BuildSplitIntentValidationResult()
    {
        PlacementIntentValidationResult result = new PlacementIntentValidationResult();
        if (SelectedCrs is null)
        {
            result.Errors.Add(UiLocalizer.Instance.Get("Georef.Error.Preview.SelectCrs"));
        }

        if (SelectedPoint is null)
        {
            result.Errors.Add("Capture a shared Survey target before generating a split-workflow preview.");
        }

        if (WorkingProjectBasePoint is null)
        {
            result.Errors.Add("Capture or confirm the local Project Base Point before generating a split-workflow preview.");
        }

        if (SelectedApplyModeOption is null)
        {
            result.Errors.Add("Choose a setup intent before generating a split-workflow preview.");
        }
        else if (SelectedApplyModeOption.Mode == PlacementApplyMode.MetadataOnly)
        {
            result.Errors.Add("Split workflow requires Project Location or Project Location + True North. Metadata Only is not supported.");
        }

        if (string.IsNullOrWhiteSpace(SetupSource))
        {
            result.Errors.Add("Setup source cannot be empty.");
        }

        if (RequiresTrueNorthAngleInput && !ParseTrueNorthAngle().HasValue)
        {
            result.Errors.Add("Enter a true north angle before generating a split-workflow preview for the angle-update mode.");
        }

        if (WorkingProjectBasePoint is not null && SelectedCrs is not null)
        {
            if (!WorkingProjectBasePoint.ProjectedCoordinate.IsFinite)
            {
                result.Errors.Add("Local Project Base Point requires projected Easting / Northing coordinates.");
            }
        }

        return result;
    }

    private SplitSurveyProjectBasePointIntent BuildSplitIntent()
    {
        return new SplitSurveyProjectBasePointIntent
        {
            SelectedCrs = SelectedCrs!.ToReference(),
            SharedSurveyOrigin = new ProjectOrigin
            {
                Latitude = SelectedPoint!.Latitude,
                Longitude = SelectedPoint.Longitude,
                ElevationMeters = 0d
            },
            SharedSurveyProjectedCoordinate = SelectedPoint.ProjectedCoordinate,
            LocalProjectBasePoint = new WorkingProjectBasePointReference
            {
                ProjectCrs = SelectedCrs.ToReference(),
                Origin = new ProjectOrigin
                {
                    Latitude = WorkingProjectBasePoint!.Latitude,
                    Longitude = WorkingProjectBasePoint.Longitude,
                    ElevationMeters = 0d
                },
                ProjectedCoordinate = WorkingProjectBasePoint.ProjectedCoordinate,
                Confidence = WorkingProjectBasePoint.ConfidenceLevel,
                SetupSource = WorkingProjectBasePoint.SourceLabel
            },
            TrueNorthAngle = ParseTrueNorthAngle(),
            Confidence = SelectedPoint.ConfidenceLevel,
            SetupSource = SetupSource,
            ApplyMode = SelectedApplyModeOption!.Mode
        };
    }

    private void RaiseWorkflowModeProperties()
    {
        RaisePropertyChanged(nameof(SelectedWorkflowModeOption));
        RaisePropertyChanged(nameof(SelectedWorkflowModeDescription));
        RaisePropertyChanged(nameof(IsSplitWorkflowMode));
        RaisePropertyChanged(nameof(IsStandardWorkflowMode));
        RaiseQuickSetupProperties();
        RaisePropertyChanged(nameof(AvailableApplyModeOptions));
        RaisePropertyChanged(nameof(CanSelectPrimaryAnchorTarget));
        RaisePropertyChanged(nameof(SelectPointIntroText));
        RaisePropertyChanged(nameof(ReviewPrimarySectionTitle));
        RaisePropertyChanged(nameof(ReviewPrimarySectionDescription));
        RaisePropertyChanged(nameof(ReviewWorkingSectionTitle));
        RaisePropertyChanged(nameof(ReviewWorkingSectionDescription));
        RaisePropertyChanged(nameof(SetupIntentPrimaryLabel));
        RaisePropertyChanged(nameof(SetupIntentWorkingLabel));
        RaisePropertyChanged(nameof(SetupIntentHelpText));
        RaisePropertyChanged(nameof(SplitWorkflowSummary));
        RaisePropertyChanged(nameof(SelectedCaptureTargetDescription));
        RaisePropertyChanged(nameof(SelectedAnchorTargetDescription));
        RaisePropertyChanged(nameof(SelectedSiteSelectionModeDescription));
        RaisePropertyChanged(nameof(CurrentRevitSetupButtonText));
        RaisePropertyChanged(nameof(CurrentRevitSetupHint));
        RaisePropertyChanged(nameof(CanShowActualProjectBasePointMoveSection));
        RaisePropertyChanged(nameof(CanGoNext));
        RaisePropertyChanged(nameof(CanApply));
        RaisePropertyChanged(nameof(SelectedPointSummary));
        RaisePropertyChanged(nameof(WorkingProjectBasePointSummary));
        RaisePropertyChanged(nameof(StepDescription));
        RaisePropertyChanged(nameof(StepTitle));
        RaiseStepNavigationProperties();
    }

    private string BuildSelectedCaptureTargetDescription()
    {
        if (!IsSplitWorkflowMode)
        {
            return SelectedCaptureTargetOption?.Description ?? string.Empty;
        }

        return IsCapturingWorkingProjectBasePoint
            ? UiLocalizer.Instance.Get("Georef.SelectedCaptureDescription.SplitWorking")
            : UiLocalizer.Instance.Get("Georef.SelectedCaptureDescription.SplitShared");
    }

    private string BuildSelectedAnchorTargetDescription()
    {
        if (IsSplitWorkflowMode)
        {
            return UiLocalizer.Instance.Get("Georef.SelectedAnchorDescription.Split");
        }

        return SelectedAnchorTargetOption?.Description ?? string.Empty;
    }

    private string BuildSelectedSiteSelectionModeDescription()
    {
        if (!IsSplitWorkflowMode)
        {
            return SelectedSiteSelectionModeOption?.Description ?? string.Empty;
        }

        return SelectedSiteSelectionModeOption?.Mode switch
        {
            SiteSelectionInputMode.CurrentRevitSetup when IsCapturingWorkingProjectBasePoint => UiLocalizer.Instance.Get("Georef.SelectedSiteDescription.SplitCurrentWorking"),
            SiteSelectionInputMode.CurrentRevitSetup => UiLocalizer.Instance.Get("Georef.SelectedSiteDescription.SplitCurrentSurvey"),
            _ => SelectedSiteSelectionModeOption?.Description ?? string.Empty
        };
    }

    private static IEnumerable<WorkflowModeOption> CreateWorkflowModeOptions()
    {
        yield return new WorkflowModeOption
        {
            Mode = GeoreferenceWorkflowMode.QuickSetup,
            Title = UiLocalizer.Instance.Get("Georef.QuickSetup.Title"),
            Description = UiLocalizer.Instance.Get("Georef.QuickSetup.Description")
        };

        yield return new WorkflowModeOption
        {
            Mode = GeoreferenceWorkflowMode.Standard,
            Title = UiLocalizer.Instance.Get("Module.Georeference"),
            Description = UiLocalizer.Instance.Get("Georef.StepDescription.Crs.Standard")
        };

        yield return new WorkflowModeOption
        {
            Mode = GeoreferenceWorkflowMode.SplitLocalProjectBasePointAndSharedSurvey,
            Title = UiLocalizer.Instance.Get("Georef.Review.WorkingTitle.Split") + " + " + UiLocalizer.Instance.Get("Georef.Review.PrimaryTitle.Split"),
            Description = UiLocalizer.Instance.Get("Georef.SplitWorkflowSummary")
        };
    }
}





