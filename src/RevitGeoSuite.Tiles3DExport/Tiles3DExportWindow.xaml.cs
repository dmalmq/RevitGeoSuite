using System;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.SharedUI.Controls;

namespace RevitGeoSuite.Tiles3DExport;

public partial class Tiles3DExportWindow : Window
{
    private readonly Tiles3DExportCoordinator exportCoordinator;
    private readonly IDocumentHandle? documentHandle;

    public Tiles3DExportWindow(
        Tiles3DExportViewModel viewModel,
        IDocumentHandle? documentHandle,
        Tiles3DExportCoordinator exportCoordinator)
    {
        InitializeComponent();
        ViewModel = viewModel;
        this.documentHandle = documentHandle;
        this.exportCoordinator = exportCoordinator;
        DataContext = viewModel;
        ModuleNavRail.ModuleRequested += OnModuleRequested;
        Closed += OnWindowClosed;
    }

    public Tiles3DExportViewModel ViewModel { get; }

    public string? PendingModuleNavigationKey { get; private set; }

    public void SetOwner(System.IntPtr ownerHandle)
    {
        new WindowInteropHelper(this).Owner = ownerHandle;
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
            Description = "Select 3D Tiles Output Folder",
            SelectedPath = ViewModel.OutputDirectory
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            ViewModel.OutputDirectory = dialog.SelectedPath;
        }
    }

    private void OnPrepareClick(object sender, RoutedEventArgs e)
    {
        if (documentHandle is null || ViewModel.ResolvedReferenceContext is null || !ViewModel.CanPrepareExport)
        {
            return;
        }

        try
        {
            Tiles3DExportPreparationResult result = exportCoordinator.Prepare(
                documentHandle,
                ViewModel.ResolvedReferenceContext,
                ViewModel.ScopeSelection,
                ViewModel.UsePreciseCrsProjection,
                ViewModel.GeoidHeightOffsetMeters);
            ViewModel.MarkPrepared(result);
        }
        catch (System.Exception ex)
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
            $"Write a 3D Tiles export package to '{ViewModel.OutputDirectory}'?\n\nThis writes tileset.json and content.glb and saves export preferences separately from GeoProjectInfo when the Revit document is editable.",
            "Confirm 3D Tiles Export",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            Tiles3DExportResult result = exportCoordinator.Export(
                documentHandle,
                ViewModel.PreparedPackage,
                ViewModel.OutputDirectory,
                ViewModel.SelectedReferenceSource,
                ViewModel.ScopeSelection,
                ViewModel.ExportState);
            ViewModel.MarkExportSucceeded(result);
            MessageBox.Show(this, result.SummaryMessage, "3D Tiles Export Succeeded", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this, ex.Message, "3D Tiles Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnModuleRequested(object? sender, ModuleNavigationRequestedEventArgs e)
    {
        if (ViewModel.PreparedPackage is not null)
        {
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                $"Switch to '{e.ModuleTitle}'?\n\nPrepared 3D Tiles export data will be discarded.",
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
