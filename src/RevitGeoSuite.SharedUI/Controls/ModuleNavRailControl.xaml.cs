using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RevitGeoSuite.Core.Modules;
using RevitGeoSuite.SharedUI.Localization;

namespace RevitGeoSuite.SharedUI.Controls;

public partial class ModuleNavRailControl : UserControl
{
    public static readonly DependencyProperty CurrentModuleKeyProperty = DependencyProperty.Register(
        nameof(CurrentModuleKey),
        typeof(string),
        typeof(ModuleNavRailControl),
        new PropertyMetadata(string.Empty, OnCurrentModuleKeyChanged));

    public ModuleNavRailControl()
    {
        NavigationItems = new ObservableCollection<ModuleNavItem>
        {
            new ModuleNavItem("Georeference", RibbonIconKind.Georeference, "Module.Georeference", "Nav.Georeference"),
            new ModuleNavItem("PlateauImport", RibbonIconKind.PlateauImport, "Module.PlateauImport", "Nav.PlateauImport"),
            new ModuleNavItem("MeshInspector", RibbonIconKind.MeshInspector, "Module.MeshInspector", "Nav.MeshInspector"),
            new ModuleNavItem("Validation", RibbonIconKind.Validation, "Module.Validation", "Nav.Validation"),
            new ModuleNavItem("Tiles3DExport", RibbonIconKind.Tiles3DExport, "Module.Tiles3DExport", "Nav.Tiles3DExport"),
            new ModuleNavItem("CityGmlExport", RibbonIconKind.CityGmlExport, "Module.CityGmlExport", "Nav.CityGmlExport")
        };

        InitializeComponent();
        RefreshLabels();
        UiLocalizer.Instance.PropertyChanged += OnLocalizerPropertyChanged;
    }

    public event EventHandler<ModuleNavigationRequestedEventArgs>? ModuleRequested;

    public ObservableCollection<ModuleNavItem> NavigationItems { get; }

    public string CurrentModuleKey
    {
        get => (string)GetValue(CurrentModuleKeyProperty);
        set => SetValue(CurrentModuleKeyProperty, value);
    }

    private static void OnCurrentModuleKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ModuleNavRailControl control)
        {
            control.RefreshSelection();
        }
    }

    private void OnLocalizerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName) || string.Equals(e.PropertyName, "Item[]", StringComparison.Ordinal))
        {
            RefreshLabels();
        }
    }

    private void RefreshSelection()
    {
        string currentKey = CurrentModuleKey ?? string.Empty;
        foreach (ModuleNavItem item in NavigationItems)
        {
            item.IsCurrent = string.Equals(item.Key, currentKey, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void RefreshLabels()
    {
        foreach (ModuleNavItem item in NavigationItems)
        {
            item.Title = UiLocalizer.Instance.Get(item.LabelKey);
            item.ShortTitle = UiLocalizer.Instance.Get(item.ShortLabelKey);
        }

        RefreshSelection();
    }

    private void OnNavItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ModuleNavItem item || item.IsCurrent)
        {
            return;
        }

        ModuleRequested?.Invoke(this, new ModuleNavigationRequestedEventArgs(item.Key, item.Title));
    }
}

public sealed class ModuleNavItem : INotifyPropertyChanged
{
    private string title;
    private string shortTitle;
    private bool isCurrent;

    public ModuleNavItem(string key, RibbonIconKind iconKind, string labelKey, string shortLabelKey)
    {
        Key = key;
        IconKind = iconKind;
        Icon = ModuleIconFactory.CreateRail(iconKind);
        LabelKey = labelKey;
        ShortLabelKey = shortLabelKey;
        title = key;
        shortTitle = key;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public RibbonIconKind IconKind { get; }

    public ImageSource Icon { get; }

    public string LabelKey { get; }

    public string ShortLabelKey { get; }

    public string Title
    {
        get => title;
        set
        {
            if (string.Equals(title, value, StringComparison.Ordinal))
            {
                return;
            }

            title = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    public string ShortTitle
    {
        get => shortTitle;
        set
        {
            if (string.Equals(shortTitle, value, StringComparison.Ordinal))
            {
                return;
            }

            shortTitle = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShortTitle)));
        }
    }

    public bool IsCurrent
    {
        get => isCurrent;
        set
        {
            if (isCurrent == value)
            {
                return;
            }

            isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        }
    }
}
