using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;

namespace RevitGeoSuite.FloorPlanExport.StarterKit;

/// <summary>
/// Lightweight bilingual EN/JA WPF modal dialog for <see cref="NewKanizuProjectCommand"/>.
/// Collects station metadata, federation mode, level stack, optional worksharing/workset setup,
/// and stale-identity clear confirmation, validating interactively via <see cref="NewKanizuProjectPlanner"/>.
/// </summary>
public sealed class NewKanizuProjectDialog : Window
{
    private readonly NewKanizuProjectLanguage _language;
    private readonly StaleIdentitySnapshot? _activeDocumentStaleSnapshot;
    private readonly bool _hasEditableActiveDocument;

    private readonly RadioButton _createFromTemplateRadio;
    private readonly RadioButton _initializeOpenDocRadio;
    private readonly TextBox _templatePathBox;
    private readonly Button _browseTemplateButton;
    private readonly TextBox _stationNameBox;
    private readonly TextBox _stationCodeBox;
    private readonly TextBox _buildingSlugBox;
    private readonly ComboBox _federationModeCombo;
    private readonly TextBox _hubModelIdBox;
    private readonly TextBox _levelsBox;
    private readonly CheckBox _enableWorksharingBox;
    private readonly CheckBox _createExportViewsBox;
    private readonly CheckBox _createWorkingViewsBox;
    private readonly CheckBox _createAreaViewsBox;
    private readonly CheckBox _clearStaleIdentityBox;
    private readonly TextBlock _staleBannerText;
    private readonly TextBlock _validationText;

    public NewKanizuProjectDialog(
        NewKanizuProjectLanguage language,
        StaleIdentitySnapshot? activeDocumentStaleSnapshot,
        bool hasEditableActiveDocument,
        string? defaultTemplatePath = null)
    {
        _language = language;
        _activeDocumentStaleSnapshot = activeDocumentStaleSnapshot;
        _hasEditableActiveDocument = hasEditableActiveDocument;

        Title = NewKanizuProjectStrings.CommandTitle(language);
        Width = 640;
        Height = 720;
        MinWidth = 560;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));

        ScrollViewer scroll = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(18),
        };

        StackPanel root = new() { Orientation = Orientation.Vertical };

        root.Children.Add(new TextBlock
        {
            Text = NewKanizuProjectStrings.CommandTitle(language),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x5E, 0x51)),
            Margin = new Thickness(0, 0, 0, 4),
        });
        root.Children.Add(new TextBlock
        {
            Text = NewKanizuProjectStrings.DialogSubtitle(language),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
            Margin = new Thickness(0, 0, 0, 12),
        });

        // Initialization mode
        root.Children.Add(MakeSectionHeader(NewKanizuProjectStrings.ModeSectionLabel(language)));
        _createFromTemplateRadio = new RadioButton
        {
            Content = NewKanizuProjectStrings.ModeCreateFromTemplateLabel(language),
            Margin = new Thickness(0, 2, 0, 4),
            IsChecked = !hasEditableActiveDocument,
        };
        _initializeOpenDocRadio = new RadioButton
        {
            Content = NewKanizuProjectStrings.ModeInitializeOpenDocumentLabel(language),
            Margin = new Thickness(0, 0, 0, 8),
            IsEnabled = hasEditableActiveDocument,
            IsChecked = hasEditableActiveDocument,
        };
        _createFromTemplateRadio.Checked += (_, _) => UpdateControlStates();
        _initializeOpenDocRadio.Checked += (_, _) => UpdateControlStates();
        root.Children.Add(_createFromTemplateRadio);
        root.Children.Add(_initializeOpenDocRadio);

        // Template path row
        root.Children.Add(MakeFieldLabel(NewKanizuProjectStrings.TemplatePathLabel(language)));
        Grid templateRow = new() { Margin = new Thickness(0, 0, 0, 10) };
        templateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        templateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _templatePathBox = new TextBox
        {
            Text = defaultTemplatePath ?? string.Empty,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _browseTemplateButton = new Button
        {
            Content = NewKanizuProjectStrings.BrowseLabel(language),
            Padding = new Thickness(12, 4, 12, 4),
        };
        _browseTemplateButton.Click += OnBrowseTemplate;
        Grid.SetColumn(_templatePathBox, 0);
        Grid.SetColumn(_browseTemplateButton, 1);
        templateRow.Children.Add(_templatePathBox);
        templateRow.Children.Add(_browseTemplateButton);
        root.Children.Add(templateRow);

        // Station metadata
        root.Children.Add(MakeFieldLabel(NewKanizuProjectStrings.StationNameLabel(language)));
        _stationNameBox = MakeTextBox(activeDocumentStaleSnapshot?.DocumentTitle ?? string.Empty);
        root.Children.Add(_stationNameBox);

        Grid codeSlugGrid = new() { Margin = new Thickness(0, 0, 0, 8) };
        codeSlugGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        codeSlugGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        codeSlugGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        StackPanel codeCol = new();
        codeCol.Children.Add(MakeFieldLabel(NewKanizuProjectStrings.StationCodeLabel(language)));
        _stationCodeBox = MakeTextBox(string.Empty);
        codeCol.Children.Add(_stationCodeBox);
        Grid.SetColumn(codeCol, 0);
        codeSlugGrid.Children.Add(codeCol);

        StackPanel slugCol = new();
        slugCol.Children.Add(MakeFieldLabel(NewKanizuProjectStrings.BuildingSlugLabel(language)));
        _buildingSlugBox = MakeTextBox(string.Empty);
        slugCol.Children.Add(_buildingSlugBox);
        Grid.SetColumn(slugCol, 2);
        codeSlugGrid.Children.Add(slugCol);
        root.Children.Add(codeSlugGrid);

        // Federation mode + HubModelId
        Grid fedGrid = new() { Margin = new Thickness(0, 0, 0, 8) };
        fedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        fedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        StackPanel fedModeCol = new();
        fedModeCol.Children.Add(MakeFieldLabel(NewKanizuProjectStrings.FederationModeLabel(language)));
        _federationModeCombo = new ComboBox { Padding = new Thickness(6, 4, 6, 4) };
        foreach (KanizuFederationMode mode in Enum.GetValues(typeof(KanizuFederationMode)))
        {
            _federationModeCombo.Items.Add(new ComboBoxItem
            {
                Content = NewKanizuProjectStrings.FormatFederationMode(mode, language),
                Tag = mode,
            });
        }

        _federationModeCombo.SelectedIndex = 0;
        _federationModeCombo.SelectionChanged += (_, _) => UpdateControlStates();
        fedModeCol.Children.Add(_federationModeCombo);
        Grid.SetColumn(fedModeCol, 0);
        fedGrid.Children.Add(fedModeCol);

        StackPanel hubCol = new();
        hubCol.Children.Add(MakeFieldLabel(NewKanizuProjectStrings.HubModelIdLabel(language)));
        _hubModelIdBox = MakeTextBox(string.Empty);
        hubCol.Children.Add(_hubModelIdBox);
        Grid.SetColumn(hubCol, 2);
        fedGrid.Children.Add(hubCol);
        root.Children.Add(fedGrid);

        root.Children.Add(new TextBlock
        {
            Text = NewKanizuProjectStrings.FederationPersistenceNote(language),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            Margin = new Thickness(0, -4, 0, 8),
        });

        // Levels text area (CSV lines: Name, Ordinal, ElevationM, ShortName)
        root.Children.Add(MakeFieldLabel(NewKanizuProjectStrings.LevelsSectionLabel(language)));
        _levelsBox = new TextBox
        {
            Text = NewKanizuProjectPlanner.FormatLevelsText(NewKanizuProjectOptions.DefaultLevels),
            AcceptsReturn = true,
            Height = 84,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 10),
        };
        root.Children.Add(_levelsBox);

        // Worksets & views checkboxes
        _enableWorksharingBox = new CheckBox
        {
            Content = new TextBlock { Text = NewKanizuProjectStrings.WorksharingCheckboxLabel(language), TextWrapping = TextWrapping.Wrap },
            Margin = new Thickness(0, 0, 0, 6),
            IsChecked = activeDocumentStaleSnapshot?.IsWorkshared ?? false,
        };
        _createExportViewsBox = new CheckBox
        {
            Content = NewKanizuProjectStrings.CreateExportViewsCheckboxLabel(language),
            Margin = new Thickness(0, 0, 0, 4),
            IsChecked = true,
        };
        _createWorkingViewsBox = new CheckBox
        {
            Content = NewKanizuProjectStrings.CreateWorkingViewsCheckboxLabel(language),
            Margin = new Thickness(0, 0, 0, 4),
            IsChecked = true,
        };
        _createAreaViewsBox = new CheckBox
        {
            Content = NewKanizuProjectStrings.CreateAreaViewsCheckboxLabel(language),
            Margin = new Thickness(0, 0, 0, 10),
            IsChecked = true,
        };
        root.Children.Add(_enableWorksharingBox);
        root.Children.Add(_createExportViewsBox);
        root.Children.Add(_createWorkingViewsBox);
        root.Children.Add(_createAreaViewsBox);

        // Stale identity banner & explicit opt-in checkbox
        _staleBannerText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x34, 0x12)),
            Margin = new Thickness(0, 0, 0, 6),
            Visibility = Visibility.Collapsed,
        };
        _clearStaleIdentityBox = new CheckBox
        {
            Content = new TextBlock { Text = NewKanizuProjectStrings.ClearStaleIdentityCheckboxLabel(language), TextWrapping = TextWrapping.Wrap },
            Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed,
        };
        root.Children.Add(_staleBannerText);
        root.Children.Add(_clearStaleIdentityBox);

        // Validation error output
        _validationText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C)),
            Margin = new Thickness(0, 0, 0, 12),
            Visibility = Visibility.Collapsed,
        };
        root.Children.Add(_validationText);

        // Buttons row
        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Button cancelButton = new()
        {
            Content = NewKanizuProjectStrings.CancelButtonLabel(language),
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        Button okButton = new()
        {
            Content = NewKanizuProjectStrings.InitializeButtonLabel(language),
            Padding = new Thickness(18, 6, 18, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x5E, 0x51)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            IsDefault = true,
        };
        okButton.Click += OnSubmit;
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);
        root.Children.Add(buttons);

        scroll.Content = root;
        Content = scroll;
        UpdateControlStates();
    }

    public NewKanizuProjectOptions? SelectedOptions { get; private set; }

    private void UpdateControlStates()
    {
        bool createFromTemplate = _createFromTemplateRadio.IsChecked == true;
        _templatePathBox.IsEnabled = createFromTemplate;
        _browseTemplateButton.IsEnabled = createFromTemplate;

        KanizuFederationMode fedMode = SelectedFederationMode();
        _hubModelIdBox.IsEnabled = fedMode == KanizuFederationMode.Spoke;

        bool showStale = !createFromTemplate && _activeDocumentStaleSnapshot != null;
        if (showStale)
        {
            StaleIdentityReport report = NewKanizuProjectPlanner.InspectStaleIdentity(_activeDocumentStaleSnapshot);
            if (report.HasStaleIdentity || report.IsShowcaseModel)
            {
                _staleBannerText.Text = string.Join("\n", report.Findings.Select(f => f.GetMessage(_language)));
                _staleBannerText.Visibility = Visibility.Visible;
                _clearStaleIdentityBox.Visibility = report.IsShowcaseModel ? Visibility.Collapsed : Visibility.Visible;
                return;
            }
        }

        _staleBannerText.Visibility = Visibility.Collapsed;
        _clearStaleIdentityBox.Visibility = Visibility.Collapsed;
    }

    private void OnBrowseTemplate(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dlg = new()
        {
            Title = NewKanizuProjectStrings.TemplatePathLabel(_language),
            Filter = "Revit Project Template (*.rte)|*.rte|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == true)
        {
            _templatePathBox.Text = dlg.FileName;
        }
    }

    private void OnSubmit(object sender, RoutedEventArgs e)
    {
        if (!NewKanizuProjectPlanner.TryParseLevelsText(_levelsBox.Text, _language, out List<NewKanizuLevelSpec> levels, out string? parseError))
        {
            ShowValidationError(parseError!);
            return;
        }

        bool createFromTemplate = _createFromTemplateRadio.IsChecked == true;
        NewKanizuProjectOptions candidate = new()
        {
            StationName = _stationNameBox.Text?.Trim() ?? string.Empty,
            StationCode = string.IsNullOrWhiteSpace(_stationCodeBox.Text) ? null : _stationCodeBox.Text.Trim(),
            BuildingSlug = string.IsNullOrWhiteSpace(_buildingSlugBox.Text) ? null : _buildingSlugBox.Text.Trim(),
            ModelRole = ShowcaseExportGuard.AuthoringModelRole,
            FederationMode = SelectedFederationMode(),
            HubModelId = string.IsNullOrWhiteSpace(_hubModelIdBox.Text) ? null : _hubModelIdBox.Text.Trim(),
            Levels = levels,
            EnableWorksharing = _enableWorksharingBox.IsChecked == true,
            InitializationMode = createFromTemplate
                ? NewKanizuProjectInitializationMode.CreateFromTemplate
                : NewKanizuProjectInitializationMode.InitializeOpenDocument,
            TemplatePath = createFromTemplate ? _templatePathBox.Text?.Trim() : null,
            ClearStaleIdentity = _clearStaleIdentityBox.IsChecked == true,
            CreateExportPlanViews = _createExportViewsBox.IsChecked == true,
            CreateWorkingPlanViews = _createWorkingViewsBox.IsChecked == true,
            CreateAreaPlanViews = _createAreaViewsBox.IsChecked == true,
            Language = _language,
        };

        if (createFromTemplate && string.IsNullOrWhiteSpace(candidate.TemplatePath))
        {
            ShowValidationError(NewKanizuProjectStrings.Select(
                _language,
                "Select a Kanizu Starter Kit .rte template file.",
                "Kanizu スターターキットの .rte テンプレートファイルを選択してください。"));
            return;
        }

        NewKanizuProjectPlan plan = NewKanizuProjectPlanner.Plan(
            candidate,
            createFromTemplate ? null : _activeDocumentStaleSnapshot);

        if (!plan.IsValid)
        {
            ShowValidationError(string.Join("\n", plan.Errors.Select(err => "• " + err.GetMessage(_language))));
            return;
        }

        SelectedOptions = candidate;
        DialogResult = true;
        Close();
    }

    private void ShowValidationError(string text)
    {
        _validationText.Text = text;
        _validationText.Visibility = Visibility.Visible;
    }

    private KanizuFederationMode SelectedFederationMode() =>
        _federationModeCombo.SelectedItem is ComboBoxItem { Tag: KanizuFederationMode mode }
            ? mode
            : KanizuFederationMode.Standalone;

    private static TextBlock MakeSectionHeader(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 4, 0, 4),
    };

    private static TextBlock MakeFieldLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
        Margin = new Thickness(0, 2, 0, 2),
    };

    private static TextBox MakeTextBox(string initialText) => new()
    {
        Text = initialText,
        Padding = new Thickness(6, 4, 6, 4),
        Margin = new Thickness(0, 0, 0, 8),
    };
}
