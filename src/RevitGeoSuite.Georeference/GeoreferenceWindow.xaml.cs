using System;
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
        ModuleNavRail.ModuleRequested += OnModuleRequested;
        Closed += OnWindowClosed;
    }

    public GeoreferenceViewModel ViewModel { get; }

    public string? PendingModuleNavigationKey { get; private set; }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ModuleNavRail.ModuleRequested -= OnModuleRequested;
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
            || ViewModel.HasPreview;
    }
}
