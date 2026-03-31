using System;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using RevitGeoSuite.Core.Storage;

namespace RevitGeoSuite.PlateauImport;

public partial class PlateauImportWindow : Window
{
    private readonly PlateauImportCoordinator importCoordinator;
    private readonly IDocumentHandle? documentHandle;

    public PlateauImportWindow(
        PlateauImportViewModel viewModel,
        IDocumentHandle? documentHandle,
        PlateauImportCoordinator importCoordinator)
    {
        InitializeComponent();
        ViewModel = viewModel;
        this.documentHandle = documentHandle;
        this.importCoordinator = importCoordinator;
        DataContext = viewModel;
    }

    public PlateauImportViewModel ViewModel { get; }

    public void SetOwner(IntPtr ownerHandle)
    {
        new WindowInteropHelper(this).Owner = ownerHandle;
    }

    private void OnBrowseFileClick(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new OpenFileDialog
        {
            Title = "Select PLATEAU CityGML File",
            Filter = "CityGML files (*.gml;*.xml)|*.gml;*.xml|All files (*.*)|*.*",
            FileName = ViewModel.SelectedFilePath
        };

        bool? result = dialog.ShowDialog(this);
        if (result == true)
        {
            ViewModel.SelectedFilePath = dialog.FileName;
        }
    }

    private void OnLoadPreviewClick(object sender, RoutedEventArgs e)
    {
        ViewModel.TryLoadPreview();
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (documentHandle is null || ViewModel.PreparedPlan is null || !ViewModel.CanImport)
        {
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            BuildImportConfirmationMessage(),
            "Confirm PLATEAU Context Import",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);

        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            PlateauImportResult result = importCoordinator.Import(
                documentHandle,
                ViewModel.PreparedPlan,
                ViewModel.SelectedReferenceSource,
                ViewModel.ImportState);
            ViewModel.MarkImportSucceeded(result);
            MessageBox.Show(
                this,
                result.SummaryMessage + "\n\nThe import state was saved in module storage separately from GeoProjectInfo.",
                "Import Succeeded",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string BuildImportConfirmationMessage()
    {
        string fileName = System.IO.Path.GetFileName(ViewModel.SelectedFilePath);
        return $"Import {ViewModel.PreparedSolidCount} lightweight PLATEAU context solids from '{fileName}' into '{ViewModel.DocumentTitle}'?\n\nThis creates Generic Model DirectShape elements and saves module-specific import state separately from GeoProjectInfo.";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
