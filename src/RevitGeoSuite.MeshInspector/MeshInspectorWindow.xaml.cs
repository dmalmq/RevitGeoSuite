using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.MeshInspector;

public partial class MeshInspectorWindow : Window
{
    private readonly IDocumentHandle? documentHandle;
    private readonly MeshCodePersistenceService persistenceService;

    public MeshInspectorWindow(MeshInspectorViewModel viewModel, IDocumentHandle? documentHandle, MeshCodePersistenceService persistenceService)
    {
        InitializeComponent();
        ViewModel = viewModel;
        this.documentHandle = documentHandle;
        this.persistenceService = persistenceService;
        DataContext = viewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public MeshInspectorViewModel ViewModel { get; }

    public void SetOwner(System.IntPtr ownerHandle)
    {
        new WindowInteropHelper(this).Owner = ownerHandle;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await MeshMap.SetPointSelectionEnabledAsync(false);
        await LoadMapAsync();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnCopyPrimaryMeshClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasPrimaryMeshCode)
        {
            return;
        }

        Clipboard.SetText(ViewModel.PrimaryMeshCode);
        ViewModel.SetActionMessage("The displayed mesh code was copied to the clipboard.");
    }

    private void OnSavePrimaryMeshClick(object sender, RoutedEventArgs e)
    {
        if (documentHandle is null || !ViewModel.CanSavePrimaryMeshCode)
        {
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            "Save the calculated canonical primary mesh code back into GeoProjectInfo? This only updates the shared convenience key and leaves CRS, origin, and orientation unchanged.",
            "Save Primary Mesh Code",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);

        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            persistenceService.SavePrimaryMeshCode(documentHandle, new MeshCode { Value = ViewModel.PrimaryMeshCode });
            ViewModel.MarkPrimaryMeshCodeSaved();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save Primary Mesh Code Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnReferenceSourceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        await LoadMapAsync();
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MeshInspectorViewModel.OverlayGeoJson)
            || e.PropertyName == nameof(MeshInspectorViewModel.CenterLatitude)
            || e.PropertyName == nameof(MeshInspectorViewModel.CenterLongitude))
        {
            await LoadMapAsync();
        }
    }

    private async Task LoadMapAsync()
    {
        await MeshMap.ClearMeshGridAsync();
        await MeshMap.ClearMarkerAsync();

        if (ViewModel.CenterLatitude.HasValue && ViewModel.CenterLongitude.HasValue)
        {
            await MeshMap.SetViewAsync(ViewModel.CenterLatitude.Value, ViewModel.CenterLongitude.Value, 14);
        }

        if (ViewModel.HasOverlay)
        {
            await MeshMap.ShowMeshGridAsync(ViewModel.OverlayGeoJson);
        }
    }
}
