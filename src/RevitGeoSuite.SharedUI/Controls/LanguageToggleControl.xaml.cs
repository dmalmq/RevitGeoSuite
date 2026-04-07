using System.Windows;
using System.Windows.Controls;
using RevitGeoSuite.SharedUI.Localization;

namespace RevitGeoSuite.SharedUI.Controls;

public partial class LanguageToggleControl : UserControl
{
    public LanguageToggleControl()
    {
        InitializeComponent();
    }

    private void OnEnglishClick(object sender, RoutedEventArgs e)
    {
        UiLocalizer.Instance.SetLanguage(UiLanguage.English);
    }

    private void OnJapaneseClick(object sender, RoutedEventArgs e)
    {
        UiLocalizer.Instance.SetLanguage(UiLanguage.Japanese);
    }
}
