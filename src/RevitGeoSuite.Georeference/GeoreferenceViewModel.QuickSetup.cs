using System.Globalization;
using System.Linq;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.SharedUI.Localization;

namespace RevitGeoSuite.Georeference;

public sealed partial class GeoreferenceViewModel
{
    public bool IsQuickSetupMode => SelectedWorkflowModeOption?.Mode == GeoreferenceWorkflowMode.QuickSetup;

    public bool IsNotQuickSetupMode => !IsQuickSetupMode;

    public string QuickSetupCoordinateHint => SelectedCrs is not null
        ? string.Format(
            CultureInfo.InvariantCulture,
            L("Georef.QuickSetup.CoordinateHint"),
            SelectedCrs.EpsgCode)
        : L("Georef.QuickSetup.SelectCrsFirst");

    private void InitializeQuickSetupDefaults()
    {
        if (!IsQuickSetupMode)
        {
            return;
        }

        selectedApplyModeOption = ApplyModeOptions.First(option => option.Mode == PlacementApplyMode.ProjectLocation);
        RaisePropertyChanged(nameof(SelectedApplyModeOption));
        RaisePropertyChanged(nameof(SelectedApplyModeDescription));
        RaisePropertyChanged(nameof(RequiresTrueNorthAngleInput));

        selectedAnchorTargetOption = AnchorTargetOptions.First(option => option.Target == PlacementAnchorTarget.SurveyPoint);
        RaisePropertyChanged(nameof(SelectedAnchorTargetOption));
        RaisePropertyChanged(nameof(SelectedAnchorTargetDescription));

        SetupSource = "Quick Setup";
    }

    private bool CanGoNextQuickSetupChooseCrs()
    {
        if (SelectedCrs is null)
        {
            return false;
        }

        return TryParseCoordinateValue(KnownCoordinateEastingInput, out _)
            && TryParseCoordinateValue(KnownCoordinateNorthingInput, out _);
    }

    private void RaiseQuickSetupProperties()
    {
        RaisePropertyChanged(nameof(IsQuickSetupMode));
        RaisePropertyChanged(nameof(IsNotQuickSetupMode));
        RaisePropertyChanged(nameof(QuickSetupCoordinateHint));
    }
}
