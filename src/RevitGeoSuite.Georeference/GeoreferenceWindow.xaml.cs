using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.SharedUI.Controls;

namespace RevitGeoSuite.Georeference;

public partial class GeoreferenceWindow : Window
{
    private readonly GeoreferenceApplyCoordinator applyCoordinator;
    private readonly SplitSurveyProjectBasePointApplyCoordinator splitApplyCoordinator;
    private readonly ProjectBasePointMoveCoordinator projectBasePointMoveCoordinator;
    private readonly IDocumentHandle? documentHandle;

    public GeoreferenceWindow(
        GeoreferenceViewModel viewModel,
        GeoreferenceApplyCoordinator applyCoordinator,
        SplitSurveyProjectBasePointApplyCoordinator splitApplyCoordinator,
        ProjectBasePointMoveCoordinator projectBasePointMoveCoordinator,
        IDocumentHandle? documentHandle)
    {
        InitializeComponent();
        ViewModel = viewModel;
        this.applyCoordinator = applyCoordinator;
        this.splitApplyCoordinator = splitApplyCoordinator;
        this.projectBasePointMoveCoordinator = projectBasePointMoveCoordinator;
        this.documentHandle = documentHandle;
        DataContext = viewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ModuleNavRail.ModuleRequested += OnModuleRequested;
        Closed += OnWindowClosed;
    }

    public GeoreferenceViewModel ViewModel { get; }

    public string? PendingModuleNavigationKey { get; private set; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshContextMarkersAsync();
        await CenterMapAsync();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ModuleNavRail.ModuleRequested -= OnModuleRequested;
        Closed -= OnWindowClosed;
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        ViewModel.GoBack();
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        GeoreferenceStep previousStep = ViewModel.CurrentStep;
        ViewModel.GoNext();
        await HandleStepTransitionAsync(previousStep);
    }

    private async void OnStepChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string stepName || !Enum.TryParse(stepName, out GeoreferenceStep step))
        {
            return;
        }

        GeoreferenceStep previousStep = ViewModel.CurrentStep;
        ViewModel.NavigateToStep(step);
        await HandleStepTransitionAsync(previousStep);
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (documentHandle is null)
        {
            MessageBox.Show(this, "No active Revit document is available for apply.", "Apply Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string confirmationMessage = BuildApplyConfirmationMessage();
        MessageBoxResult confirmation = MessageBox.Show(
            this,
            confirmationMessage,
            "Confirm Georeference Apply",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            PlacementApplyResult result = ViewModel.IsSplitWorkflowMode
                ? splitApplyCoordinator.Apply(documentHandle, ViewModel)
                : applyCoordinator.Apply(documentHandle, ViewModel);
            MessageBox.Show(
                this,
                "Georeference changes were applied successfully.\n\n" + result.AuditSummary + "\n\nShared geo metadata and the latest audit summary were saved to the project.",
                "Apply Succeeded",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Apply Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string BuildApplyConfirmationMessage()
    {
        string message = ViewModel.IsSplitWorkflowMode
            ? $"Apply the split Project Base Point + Survey workflow to '{ViewModel.CurrentState.DocumentTitle}'?\n\n{ViewModel.PreviewChangeImpactSummary}\n\nThis will keep the actual Revit Project Base Point local, reposition the Survey Point in Revit, and save shared geo metadata plus the latest audit summary in one transaction."
            : $"Apply the previewed georeference changes to '{ViewModel.CurrentState.DocumentTitle}'?\n\n{ViewModel.PreviewChangeImpactSummary}\n\nShared geo metadata and the latest audit summary will be saved in the same Revit transaction.";
        if (ViewModel.HasExistingSetupMessage)
        {
            message += "\n\nWarning: " + ViewModel.ExistingSetupMessage;
        }

        return message;
    }

    private void OnMoveActualProjectBasePointClick(object sender, RoutedEventArgs e)
    {
        if (documentHandle is null)
        {
            MessageBox.Show(this, "No active Revit document is available for Project Base Point move.", "Move Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ViewModel.CanMoveActualProjectBasePoint)
        {
            MessageBox.Show(this, ViewModel.ActualProjectBasePointMoveStatusMessage, "Move Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            ProjectBasePointMovePreview preview = projectBasePointMoveCoordinator.Preview(documentHandle, ViewModel);
            if (preview.ExceedsPlanMoveLimit)
            {
                MessageBox.Show(this, preview.BlockingMessage, "Move Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (preview.IsNoOp)
            {
                MessageBox.Show(this, "The actual Project Base Point already matches the captured Working Project Base Point within tolerance.", "No Move Needed", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBoxResult confirmation = MessageBox.Show(
                this,
                BuildProjectBasePointMoveConfirmationMessage(preview),
                "Confirm Local Project Base Point Alignment",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

            if (confirmation != MessageBoxResult.OK)
            {
                return;
            }

            ProjectBasePointMoveResult result = projectBasePointMoveCoordinator.Apply(documentHandle, preview);
            MessageBox.Show(
                this,
                result.Summary + "\n\nReopen Georeference Setup to review the updated Project Base Point context.",
                "Project Base Point Alignment Succeeded",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Project Base Point Alignment Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string BuildProjectBasePointMoveConfirmationMessage(ProjectBasePointMovePreview preview)
    {
        double currentEastMeters = preview.CurrentSharedEastWestFeet * 0.3048d;
        double currentNorthMeters = preview.CurrentSharedNorthSouthFeet * 0.3048d;
        double proposedEastMeters = preview.ProposedSharedEastWestFeet * 0.3048d;
        double proposedNorthMeters = preview.ProposedSharedNorthSouthFeet * 0.3048d;
        string currentGeo = ViewModel.CurrentState.ProjectBasePoint.HasEstimatedLocation
            ? $"Lat {ViewModel.CurrentState.ProjectBasePoint.EstimatedLatitudeDegrees!.Value:F6}, Lon {ViewModel.CurrentState.ProjectBasePoint.EstimatedLongitudeDegrees!.Value:F6}"
            : "Not available";
        string proposedGeo = ViewModel.WorkingProjectBasePoint is null
            ? "Not available"
            : $"Lat {ViewModel.WorkingProjectBasePoint.Latitude:F6}, Lon {ViewModel.WorkingProjectBasePoint.Longitude:F6}";

        string message =
            $"Align the actual Project Base Point locally in '{ViewModel.CurrentState.DocumentTitle}'?\n\n" +
            $"Current local position: X {preview.CurrentLocalXFeet:F3} ft, Y {preview.CurrentLocalYFeet:F3} ft, Z {preview.CurrentLocalZFeet:F3} ft\n" +
            $"Proposed local position: X {preview.ProposedLocalXFeet:F3} ft, Y {preview.ProposedLocalYFeet:F3} ft, Z {preview.ProposedLocalZFeet:F3} ft\n\n" +
            $"Current shared position: E {currentEastMeters:F3} m, N {currentNorthMeters:F3} m\n" +
            $"Proposed shared position: E {proposedEastMeters:F3} m, N {proposedNorthMeters:F3} m\n\n" +
            $"Current Project Base Point estimate: {currentGeo}\n" +
            $"Proposed Project Base Point estimate: {proposedGeo}\n\n" +
            $"Required local plan move: {preview.RequiredPlanMoveFeet * 0.3048d:F1} m\n\n" +
            "Survey Point, Project Location, and True North will stay unchanged.\n" +
            "Project Base Point elevation will stay unchanged. Far-away targets should keep using the saved Working Project Base Point instead of moving the actual Revit Project Base Point.";

        if (!string.IsNullOrWhiteSpace(preview.WarningMessage))
        {
            message += "\n\nWarning: " + preview.WarningMessage;
        }

        return message;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnMapPointSelected(object? sender, MapPointSelectedEventArgs e)
    {
        ViewModel.SetSelectedMapPoint(e.Latitude, e.Longitude);
        if (ViewModel.DisplayedMapPoint is not null)
        {
            await SiteMap.SetMarkerAsync(ViewModel.DisplayedMapPoint.Latitude, ViewModel.DisplayedMapPoint.Longitude, "Selected Point");
        }
    }

    private async void OnUseKnownCoordinatesClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TryUseKnownCoordinates())
        {
            await RefreshContextMarkersAsync();
            await CenterMapAsync();
        }
    }

    private async void OnUseCurrentRevitSetupClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TryUseCurrentRevitSetup())
        {
            await RefreshContextMarkersAsync();
            await CenterMapAsync();
        }
    }

    private void OnClearWorkingProjectBasePointClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearWorkingProjectBasePoint();
    }

    private async void OnMapSearchClick(object sender, RoutedEventArgs e)
    {
        await RunMapSearchAsync();
    }

    private async void OnMapSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await RunMapSearchAsync();
    }

    private async void OnZoomToSiteClick(object sender, RoutedEventArgs e)
    {
        await CenterMapOnSiteAsync();
    }

    private async void OnZoomToProjectBasePointClick(object sender, RoutedEventArgs e)
    {
        await CenterMapOnProjectBasePointAsync();
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GeoreferenceViewModel.DisplayedMapPoint))
        {
            if (ViewModel.DisplayedMapPoint is not null)
            {
                await SiteMap.SetMarkerAsync(ViewModel.DisplayedMapPoint.Latitude, ViewModel.DisplayedMapPoint.Longitude, "Selected Point");
            }
            else
            {
                await SiteMap.ClearMarkerAsync();
            }

            return;
        }

        if (e.PropertyName == nameof(GeoreferenceViewModel.CurrentStep))
        {
            await RefreshContextMarkersAsync();
        }
    }

    private void OnModuleRequested(object? sender, ModuleNavigationRequestedEventArgs e)
    {
        if (HasPendingNavigationChanges())
        {
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                $"Switch to '{e.ModuleTitle}'?\n\nCurrent georeference selections will be discarded.",
                "Switch Module",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel);

            if (confirmation != MessageBoxResult.OK)
            {
                return;
            }
        }

        PendingModuleNavigationKey = e.ModuleKey;
        Close();
    }

    private bool HasPendingNavigationChanges()
    {
        return ViewModel.CurrentStep != GeoreferenceStep.CurrentState
            || ViewModel.SelectedCrs is not null
            || ViewModel.HasSelectedPoint
            || ViewModel.HasWorkingProjectBasePoint
            || ViewModel.HasPreview
            || !string.IsNullOrWhiteSpace(ViewModel.KnownCoordinateEastingInput)
            || !string.IsNullOrWhiteSpace(ViewModel.KnownCoordinateNorthingInput);
    }

    private async Task HandleStepTransitionAsync(GeoreferenceStep previousStep)
    {
        if (previousStep != ViewModel.CurrentStep && ViewModel.CurrentStep == GeoreferenceStep.SelectPoint)
        {
            await RefreshContextMarkersAsync();
            await CenterMapAsync();
        }

        if (ViewModel.CurrentStep == GeoreferenceStep.ReviewPoint && ViewModel.DisplayedMapPoint is not null)
        {
            await SiteMap.SetMarkerAsync(ViewModel.DisplayedMapPoint.Latitude, ViewModel.DisplayedMapPoint.Longitude, "Selected Point");
        }
    }

    private async Task RunMapSearchAsync()
    {
        string query = MapSearchTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        await SiteMap.SearchAsync(query);
    }

    private async Task RefreshContextMarkersAsync()
    {
        List<MapReferenceMarker> markers = new List<MapReferenceMarker>();

        if (ViewModel.TryGetSurveyPointContextLocation(out double surveyLatitude, out double surveyLongitude))
        {
            markers.Add(new MapReferenceMarker
            {
                Latitude = surveyLatitude,
                Longitude = surveyLongitude,
                Title = "Survey Point",
                Kind = "survey"
            });
        }

        if (ViewModel.TryGetCurrentProjectBasePointContextLocation(out double projectLatitude, out double projectLongitude))
        {
            markers.Add(new MapReferenceMarker
            {
                Latitude = projectLatitude,
                Longitude = projectLongitude,
                Title = "Project Base Point",
                Kind = "projectBasePoint"
            });
        }

        if (markers.Count == 0)
        {
            await SiteMap.ClearReferenceMarkersAsync();
            return;
        }

        await SiteMap.ShowReferenceMarkersAsync(markers);
    }

    private async Task CenterMapAsync()
    {
        if (ViewModel.DisplayedMapPoint is not null)
        {
            await SiteMap.SetViewAsync(ViewModel.DisplayedMapPoint.Latitude, ViewModel.DisplayedMapPoint.Longitude, 17);
            await SiteMap.SetMarkerAsync(ViewModel.DisplayedMapPoint.Latitude, ViewModel.DisplayedMapPoint.Longitude, "Selected Point");
            return;
        }

        if (ViewModel.HasProjectBasePointLocation)
        {
            await CenterMapOnProjectBasePointAsync();
            return;
        }

        await CenterMapOnSiteAsync();
    }

    private async Task CenterMapOnSiteAsync()
    {
        if (ViewModel.HasSiteLocation)
        {
            await SiteMap.SetViewAsync(ViewModel.CurrentState.SiteLatitudeDegrees!.Value, ViewModel.CurrentState.SiteLongitudeDegrees!.Value, 15);
            return;
        }

        await SiteMap.SetViewAsync(35.681236, 139.767125, 11);
    }

    private async Task CenterMapOnProjectBasePointAsync()
    {
        if (ViewModel.TryGetProjectBasePointLocation(out double latitude, out double longitude))
        {
            await SiteMap.SetViewAsync(latitude, longitude, 17);
            return;
        }

        await CenterMapOnSiteAsync();
    }
}







