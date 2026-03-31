using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.SharedUI.Controls;

public partial class CrsPickerControl : UserControl
{
    public static readonly DependencyProperty AvailableDefinitionsProperty = DependencyProperty.Register(
        nameof(AvailableDefinitions),
        typeof(IEnumerable<CrsDefinition>),
        typeof(CrsPickerControl),
        new PropertyMetadata(null, OnAvailableDefinitionsChanged));

    public static readonly DependencyProperty SelectedDefinitionProperty = DependencyProperty.Register(
        nameof(SelectedDefinition),
        typeof(CrsDefinition),
        typeof(CrsPickerControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDefinitionChanged));

    public CrsPickerControl()
    {
        InitializeComponent();
        FilteredDefinitions = new ObservableCollection<CrsDefinition>();
        Loaded += OnLoaded;
    }

    public IEnumerable<CrsDefinition>? AvailableDefinitions
    {
        get => (IEnumerable<CrsDefinition>?)GetValue(AvailableDefinitionsProperty);
        set => SetValue(AvailableDefinitionsProperty, value);
    }

    public ObservableCollection<CrsDefinition> FilteredDefinitions { get; }

    public CrsDefinition? SelectedDefinition
    {
        get => (CrsDefinition?)GetValue(SelectedDefinitionProperty);
        set => SetValue(SelectedDefinitionProperty, value);
    }

    private static void OnAvailableDefinitionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((CrsPickerControl)d).RefreshFilter();
    }

    private static void OnSelectedDefinitionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        CrsPickerControl control = (CrsPickerControl)d;
        if (control.DefinitionsList.SelectedItem != e.NewValue)
        {
            control.DefinitionsList.SelectedItem = e.NewValue;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshFilter();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshFilter();
    }

    private void RefreshFilter()
    {
        IReadOnlyList<CrsDefinition> filtered = CrsFilter.Apply(AvailableDefinitions, SearchBox?.Text);

        CrsDefinition? selection = SelectedDefinition;
        FilteredDefinitions.Clear();
        foreach (CrsDefinition definition in filtered)
        {
            FilteredDefinitions.Add(definition);
        }

        if (selection is not null && FilteredDefinitions.Contains(selection))
        {
            DefinitionsList.SelectedItem = selection;
            return;
        }

        if (FilteredDefinitions.Count > 0)
        {
            DefinitionsList.SelectedIndex = 0;
        }
        else
        {
            SelectedDefinition = null;
        }
    }
}
