using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;

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
            InvalidatePreview();
            RefreshSetupIntentValidation();
            RaiseWorkflowModeProperties();
        }
    }

    public bool IsSplitWorkflowMode => SelectedWorkflowModeOption?.Mode == GeoreferenceWorkflowMode.SplitLocalProjectBasePointAndSharedSurvey;

    public bool IsStandardWorkflowMode => !IsSplitWorkflowMode;

    public IEnumerable<ApplyModeOption> AvailableApplyModeOptions => IsSplitWorkflowMode
        ? ApplyModeOptions.Where(option => option.Mode != PlacementApplyMode.MetadataOnly)
        : ApplyModeOptions;

    public string SelectedWorkflowModeDescription => SelectedWorkflowModeOption?.Description ?? string.Empty;

    public bool CanSelectPrimaryAnchorTarget => IsCapturingPrimaryApplyAnchor && IsStandardWorkflowMode;

    public string SelectPointIntroText => IsSplitWorkflowMode
        ? "Choose the far-away shared Survey target first, then confirm the local Project Base Point that should stay near the model."
        : "Choose how to define the apply anchor, then use the map as the primary selection surface.";

    public string ReviewPrimarySectionTitle => IsSplitWorkflowMode ? "Shared Survey Target" : "Primary Apply Anchor";

    public string ReviewPrimarySectionDescription => IsSplitWorkflowMode
        ? "This point becomes the shared Survey / shared-coordinate target. It can be far away from the model because the actual Project Base Point stays local."
        : "Check the primary apply anchor first, then confirm the optional working Project Base Point only if you intend to reuse it in later workflows.";

    public string ReviewWorkingSectionTitle => IsSplitWorkflowMode ? "Local Project Base Point" : "Working Project Base Point";

    public string ReviewWorkingSectionDescription => IsSplitWorkflowMode
        ? "This point is the local Project Base Point reference near the model. Split apply keeps the actual Revit Project Base Point local and aligns shared coordinates from it."
        : "This secondary reference is optional and is stored for later workflows. It does not replace the main apply anchor.";

    public string SetupIntentPrimaryLabel => IsSplitWorkflowMode ? "Shared Survey Target" : "Primary Anchor";

    public string SetupIntentWorkingLabel => IsSplitWorkflowMode ? "Local Project Base Point" : "Working Project Base Point";

    public string SetupIntentHelpText => IsSplitWorkflowMode
        ? "Split apply keeps the actual Revit Project Base Point local, updates shared/project location values from that local point, and repositions the Survey Point to the selected shared target."
        : "Choose what the apply step should change. The next screen is the final confirmation gate before any document modification.";

    public string SplitWorkflowSummary => IsSplitWorkflowMode
        ? "Split mode keeps the actual Revit Project Base Point near the model and uses the Survey Point/shared coordinates for the far-away real-world CRS target."
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

        if (IsSplitWorkflowMode && SelectedApplyModeOption.Mode == PlacementApplyMode.MetadataOnly)
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
            result.Errors.Add("Select a coordinate reference system before generating a split-workflow preview.");
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
            ? "Capture or confirm the local Project Base Point near the model. Split apply keeps the actual Revit Project Base Point local and saves this same point for downstream PLATEAU and export workflows."
            : "Capture the shared Survey target in the selected CRS. This becomes the far-away shared-coordinate origin while the actual Revit Project Base Point stays local.";
    }

    private string BuildSelectedAnchorTargetDescription()
    {
        if (IsSplitWorkflowMode)
        {
            return "Split workflow always treats the primary captured point as the shared Survey target.";
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
            SiteSelectionInputMode.CurrentRevitSetup when IsCapturingWorkingProjectBasePoint => "Read the current actual Revit Project Base Point into the selected CRS and use it as the local split-workflow Project Base Point.",
            SiteSelectionInputMode.CurrentRevitSetup => "Read the current Survey Point/shared setup into the selected CRS and use it as the shared survey target.",
            _ => SelectedSiteSelectionModeOption?.Description ?? string.Empty
        };
    }

    private static IEnumerable<WorkflowModeOption> CreateWorkflowModeOptions()
    {
        yield return new WorkflowModeOption
        {
            Mode = GeoreferenceWorkflowMode.Standard,
            Title = "Standard Georeference",
            Description = "Use one primary anchor and an optional working Project Base Point. This is the existing safe V1 workflow."
        };

        yield return new WorkflowModeOption
        {
            Mode = GeoreferenceWorkflowMode.SplitLocalProjectBasePointAndSharedSurvey,
            Title = "Local Project Base Point + Shared Survey",
            Description = "Keep the actual Revit Project Base Point local near the model, then place the Survey Point/shared coordinates at the far-away CRS target."
        };
    }
}


