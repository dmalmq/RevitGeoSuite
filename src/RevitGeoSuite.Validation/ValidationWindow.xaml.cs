using System;
using System.Windows;
using System.Windows.Interop;
using RevitGeoSuite.SharedUI.Controls;

namespace RevitGeoSuite.Validation;

public partial class ValidationWindow : Window
{
    public ValidationWindow(ValidationViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        ModuleNavRail.ModuleRequested += OnModuleRequested;
        Closed += OnWindowClosed;
    }

    public ValidationViewModel ViewModel { get; }

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

    private void OnModuleRequested(object? sender, ModuleNavigationRequestedEventArgs e)
    {
        PendingModuleNavigationKey = e.ModuleKey;
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
