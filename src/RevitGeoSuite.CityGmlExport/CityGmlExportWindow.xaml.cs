using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.SharedUI.Controls;

namespace RevitGeoSuite.CityGmlExport;

public partial class CityGmlExportWindow : Window
{
    private readonly CityGmlExportCoordinator exportCoordinator;
    private readonly IDocumentHandle? documentHandle;

    public CityGmlExportWindow(
        CityGmlExportViewModel viewModel,
        IDocumentHandle? documentHandle,
        CityGmlExportCoordinator exportCoordinator)
    {
        EnsureThemeDictionary();
        InitializeComponent();
        ViewModel = viewModel;
        this.documentHandle = documentHandle;
        this.exportCoordinator = exportCoordinator;
        DataContext = viewModel;
        ModuleNavRail.ModuleRequested += OnModuleRequested;
        Closed += OnWindowClosed;
    }

    public CityGmlExportViewModel ViewModel { get; }

    public string? PendingModuleNavigationKey { get; private set; }

    public void SetOwner(IntPtr ownerHandle)
    {
        new WindowInteropHelper(this).Owner = ownerHandle;
    }

    private void EnsureThemeDictionary()
    {
        string assemblyName = typeof(MapControl).Assembly.GetName().Name ?? "RevitGeoSuite.SharedUI";
        Uri themeUri = new Uri($"/{assemblyName};component/Styles/SuiteTheme.xaml", UriKind.Relative);
        bool alreadyLoaded = Resources.MergedDictionaries.Any(dictionary =>
            dictionary.Source is not null &&
            string.Equals(dictionary.Source.OriginalString, themeUri.OriginalString, StringComparison.OrdinalIgnoreCase));
        if (!alreadyLoaded)
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeUri });
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ModuleNavRail.ModuleRequested -= OnModuleRequested;
        Closed -= OnWindowClosed;
    }

    private void OnBrowseOutputClick(object sender, RoutedEventArgs e)
    {
        using Forms.FolderBrowserDialog dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select CityGML Output Folder",
            SelectedPath = ViewModel.OutputDirectory
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            ViewModel.OutputDirectory = dialog.SelectedPath;
        }
    }

    private void OnPrepareClick(object sender, RoutedEventArgs e)
    {
        if (documentHandle is null || ViewModel.ResolvedReferenceContext is null)
        {
            return;
        }

        try
        {
            CityGmlExportPreparationResult result = exportCoordinator.Prepare(
                documentHandle,
                ViewModel.ResolvedReferenceContext,
                ViewModel.ScopeSelection,
                ViewModel.SelectedSchemaVersion,
                ViewModel.CategoryMappingOverrides,
                ViewModel.CodelistOverrides);
            ViewModel.MarkPrepared(result);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Prepare Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (documentHandle is null || ViewModel.PreparedPackage is null || !ViewModel.CanExport)
        {
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            $"Write CityGML export output to '{ViewModel.OutputDirectory}'?\n\nThis writes city-model.gml and saves CityGML export settings separately from GeoProjectInfo when the Revit document is editable.",
            "Confirm CityGML Export",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            CityGmlExportResult result = exportCoordinator.Export(
                documentHandle,
                ViewModel.PreparedPackage,
                ViewModel.OutputDirectory,
                ViewModel.SelectedReferenceSource,
                ViewModel.ScopeSelection,
                ViewModel.ExportState,
                ViewModel.CategoryMappingOverrides,
                ViewModel.CodelistOverrides);
            ViewModel.MarkExportSucceeded(result);
            MessageBox.Show(this, result.SummaryMessage, "CityGML Export Succeeded", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "CityGML Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnModuleRequested(object? sender, ModuleNavigationRequestedEventArgs e)
    {
        if (ViewModel.PreparedPackage is not null)
        {
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                $"Switch to '{e.ModuleTitle}'?\n\nPrepared CityGML export data will be discarded.",
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

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
