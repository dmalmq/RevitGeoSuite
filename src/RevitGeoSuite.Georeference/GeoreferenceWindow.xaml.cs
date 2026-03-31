using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.Georeference;

public partial class GeoreferenceWindow : Window
{
    private readonly GeoreferenceApplyCoordinator applyCoordinator;
    private readonly IDocumentHandle? documentHandle;

    public GeoreferenceWindow(GeoreferenceViewModel viewModel, GeoreferenceApplyCoordinator applyCoordinator, IDocumentHandle? documentHandle)
    {
        InitializeComponent();
        ViewModel = viewModel;
        this.applyCoordinator = applyCoordinator;
        this.documentHandle = documentHandle;
        DataContext = viewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public GeoreferenceViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await CenterMapAsync();
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        ViewModel.GoBack();
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        GeoreferenceStep previousStep = ViewModel.CurrentStep;
        ViewModel.GoNext();

        if (previousStep != ViewModel.CurrentStep && ViewModel.CurrentStep == GeoreferenceStep.SelectPoint)
        {
            await CenterMapAsync();
        }

        if (ViewModel.CurrentStep == GeoreferenceStep.ReviewPoint && ViewModel.DisplayedMapPoint is not null)
        {
            await SiteMap.SetMarkerAsync(ViewModel.DisplayedMapPoint.Latitude, ViewModel.DisplayedMapPoint.Longitude);
        }
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
            PlacementApplyResult result = applyCoordinator.Apply(documentHandle, ViewModel);
            MessageBox.Show(
                this,
                "Georeference changes were applied successfully.\n\n" + result.AuditSummary + "\n\nShared geo metadata and the latest audit summary were saved to the project.",
                "Apply Succeeded",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (System.Exception ex)
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
        string message = $"Apply the previewed georeference changes to '{ViewModel.CurrentState.DocumentTitle}'?\n\n{ViewModel.PreviewChangeImpactSummary}\n\nShared geo metadata and the latest audit summary will be saved in the same Revit transaction.";
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

    private async void OnMapPointSelected(object? sender, RevitGeoSuite.SharedUI.Controls.MapPointSelectedEventArgs e)
    {
        ViewModel.SetSelectedMapPoint(e.Latitude, e.Longitude);
        if (ViewModel.DisplayedMapPoint is not null)
        {
            await SiteMap.SetMarkerAsync(ViewModel.DisplayedMapPoint.Latitude, ViewModel.DisplayedMapPoint.Longitude);
        }
    }

    private async void OnUseKnownCoordinatesClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TryUseKnownCoordinates())
        {
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
        if (e.PropertyName == nameof(GeoreferenceViewModel.DisplayedMapPoint) && ViewModel.DisplayedMapPoint is not null)
        {
            await SiteMap.SetMarkerAsync(ViewModel.DisplayedMapPoint.Latitude, ViewModel.DisplayedMapPoint.Longitude);
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

    private async Task CenterMapAsync()
    {
        if (ViewModel.DisplayedMapPoint is not null)
        {
            await SiteMap.SetViewAsync(ViewModel.DisplayedMapPoint.Latitude, ViewModel.DisplayedMapPoint.Longitude, 17);
            await SiteMap.SetMarkerAsync(ViewModel.DisplayedMapPoint.Latitude, ViewModel.DisplayedMapPoint.Longitude);
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

