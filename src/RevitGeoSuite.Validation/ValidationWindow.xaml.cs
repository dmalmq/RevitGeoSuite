using System.Windows;
using System.Windows.Interop;

namespace RevitGeoSuite.Validation;

public partial class ValidationWindow : Window
{
    public ValidationWindow(ValidationViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public ValidationViewModel ViewModel { get; }

    public void SetOwner(System.IntPtr ownerHandle)
    {
        new WindowInteropHelper(this).Owner = ownerHandle;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
