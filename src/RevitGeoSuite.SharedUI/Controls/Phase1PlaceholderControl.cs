using System.Windows.Controls;

namespace RevitGeoSuite.SharedUI.Controls;

public sealed class Phase1PlaceholderControl : UserControl
{
    public Phase1PlaceholderControl()
    {
        Content = new TextBlock
        {
            Text = "Revit Geo Suite Phase 1 placeholder control",
            Margin = new System.Windows.Thickness(12)
        };
    }
}
