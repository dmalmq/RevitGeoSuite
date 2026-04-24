using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.SharedUI.Controls;

namespace RevitGeoSuite.Georeference;

public partial class GeoreferenceWindow : Window
{
    private readonly GeoreferenceApplyCoordinator applyCoordinator;
    private readonly SplitSurveyProjectBasePointApplyCoordinator splitApplyCoordinator;
    private readonly IDocumentHandle? documentHandle;

    public GeoreferenceWindow(
        GeoreferenceViewModel viewModel,
        GeoreferenceApplyCoordinator applyCoordinator,
        SplitSurveyProjectBasePointApplyCoordinator splitApplyCoordinator,
        IDocumentHandle? documentHandle)
    {
        InitializeComponent();
        ViewModel = viewModel;
        this.applyCoordinator = applyCoordinator;
        this.splitApplyCoordinator = splitApplyCoordinator;
        this.documentHandle = documentHandle;
        DataContext = viewModel;
        Loaded += OnWindowLoaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        PlateauGridMap.OverlayFeatureClicked += OnPlateauGridOverlayFeatureClicked;
        ModuleNavRail.ModuleRequested += OnModuleRequested;
        Closed += OnWindowClosed;
    }

    public GeoreferenceViewModel ViewModel { get; }

    public string? PendingModuleNavigationKey { get; private set; }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshPlateauGridMapAsync(true);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        PlateauGridMap.OverlayFeatureClicked -= OnPlateauGridOverlayFeatureClicked;
        ModuleNavRail.ModuleRequested -= OnModuleRequested;
        Loaded -= OnWindowLoaded;
        Closed -= OnWindowClosed;
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        ViewModel.GoBack();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        ViewModel.GoNext();
    }

    private void OnStepChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string stepName || !Enum.TryParse(stepName, out GeoreferenceStep step))
        {
            return;
        }

        ViewModel.NavigateToStep(step);
    }

    private async void OnClearPlateauGridSelectionClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearPlateauGridSelection();
        await RefreshPlateauGridMapAsync(false);
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GeoreferenceViewModel.IsPlateauGridCoordinateMode)
            || e.PropertyName == nameof(GeoreferenceViewModel.PlateauGridOverlayGeoJson)
            || e.PropertyName == nameof(GeoreferenceViewModel.PlateauGridStatusText)
            || e.PropertyName == nameof(GeoreferenceViewModel.PlateauGridAnchorLatitude)
            || e.PropertyName == nameof(GeoreferenceViewModel.PlateauGridAnchorLongitude)
            || e.PropertyName == nameof(GeoreferenceViewModel.PlateauGridSeedLatitude)
            || e.PropertyName == nameof(GeoreferenceViewModel.PlateauGridSeedLongitude))
        {
            bool fitToBounds = e.PropertyName == nameof(GeoreferenceViewModel.IsPlateauGridCoordinateMode)
                || e.PropertyName == nameof(GeoreferenceViewModel.PlateauGridSeedLatitude)
                || e.PropertyName == nameof(GeoreferenceViewModel.PlateauGridSeedLongitude);
            await RefreshPlateauGridMapAsync(fitToBounds);
        }
    }

    private async void OnPlateauGridOverlayFeatureClicked(object? sender, MapOverlayFeatureClickedEventArgs e)
    {
        if (ViewModel.TogglePlateauGridSelection(e.FeatureId))
        {
            await RefreshPlateauGridMapAsync(false);
        }
    }

    private async Task RefreshPlateauGridMapAsync(bool fitToBounds)
    {
        await PlateauGridMap.SetPointSelectionEnabledAsync(false);
        await PlateauGridMap.ClearMeshGridAsync();
        await PlateauGridMap.ClearFeatureSelectionOverlayAsync();
        await PlateauGridMap.ClearMarkerAsync();

        if (!ViewModel.IsPlateauGridCoordinateMode)
        {
            return;
        }

        if (ViewModel.HasPlateauGridOverlay)
        {
            await PlateauGridMap.ShowFeatureSelectionOverlayAsync(
                ViewModel.PlateauGridOverlayGeoJson,
                fitToBounds,
                ViewModel.PlateauGridSeedLatitude,
                ViewModel.PlateauGridSeedLongitude,
                ViewModel.PlateauGridStatusText);
        }
        else if (fitToBounds && ViewModel.PlateauGridSeedLatitude.HasValue && ViewModel.PlateauGridSeedLongitude.HasValue)
        {
            await PlateauGridMap.SetViewAsync(ViewModel.PlateauGridSeedLatitude.Value, ViewModel.PlateauGridSeedLongitude.Value, 14);
        }

        if (ViewModel.HasPlateauGridAnchor && ViewModel.PlateauGridAnchorLatitude.HasValue && ViewModel.PlateauGridAnchorLongitude.HasValue)
        {
            await PlateauGridMap.SetMarkerAsync(
                ViewModel.PlateauGridAnchorLatitude.Value,
                ViewModel.PlateauGridAnchorLongitude.Value,
                ViewModel.PlateauGridAnchorTitle);
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (documentHandle is null)
        {
            MessageBox.Show(this, "No active Revit document is available for apply.", "Apply Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            BuildApplyConfirmationMessage(),
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
            PlacementApplyResult result = ViewModel.UsesSplitApply
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
            MessageBox.Show(this, ex.Message, "Apply Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string BuildApplyConfirmationMessage()
    {
        string message = ViewModel.UsesSplitApply
            ? $"Apply the new-project georeference setup to '{ViewModel.CurrentState.DocumentTitle}'?\n\nSurvey Point will become CRS origin E 0.000 m, N 0.000 m.\nProject Base Point shared coordinates will resolve to {ViewModel.ProjectBasePointOffsetSummary}.\n\nShared geo metadata and the latest audit summary will be saved in the same Revit transaction."
            : $"Create georeference metadata from the existing setup in '{ViewModel.CurrentState.DocumentTitle}'?\n\nThe current Survey Point and Project Base Point geometry will stay unchanged. The add-in will save canonical CRS metadata and the current Project Base Point offset for downstream workflows.\n\nShared geo metadata and the latest audit summary will be saved in the same Revit transaction.";

        if (ViewModel.IsPlateauGridCoordinateMode && ViewModel.HasPlateauGridSelection)
        {
            message += "\n\nGrid-derived anchor: " + ViewModel.PlateauGridAnchorSummary;
        }

        if (ViewModel.HasExistingSetupMessage)
        {
            message += "\n\nWarning: " + ViewModel.ExistingSetupMessage;
        }

        return message;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
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
            || !string.IsNullOrWhiteSpace(ViewModel.ProjectBasePointOffsetXInput)
            || !string.IsNullOrWhiteSpace(ViewModel.ProjectBasePointOffsetYInput)
            || ViewModel.ConfirmExistingSetup
            || ViewModel.IsPlateauGridCoordinateMode
            || ViewModel.HasPlateauGridSelection
            || ViewModel.HasPreview;
    }
}
