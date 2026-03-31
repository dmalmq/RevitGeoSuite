using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.MeshInspector;

public sealed class MeshInspectorViewModel : INotifyPropertyChanged
{
    private readonly MeshInspectorService service;
    private readonly CurrentProjectStateSummary currentState;
    private readonly GeoProjectInfo? info;
    private string actionMessage;
    private bool canSavePrimaryMeshCode;
    private MeshInspectorSummary summary;
    private MeshReferenceSourceOption? selectedReferenceSourceOption;

    public MeshInspectorViewModel(MeshInspectorService service, CurrentProjectStateSummary currentState, GeoProjectInfo? info)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.currentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        this.info = info;
        actionMessage = string.Empty;
        DetailRows = new ObservableCollection<DetailRow>();
        NeighborMeshCodes = new ObservableCollection<string>();
        ReferenceSourceOptions = new ObservableCollection<MeshReferenceSourceOption>(CreateReferenceSourceOptions());
        summary = this.service.BuildSummary(this.currentState, this.info, MeshReferenceSource.SurveyPoint);
        canSavePrimaryMeshCode = summary.CanSavePrimaryMeshCode;
        SelectedReferenceSourceOption = ReferenceSourceOptions[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DetailRow> DetailRows { get; }

    public ObservableCollection<string> NeighborMeshCodes { get; }

    public ObservableCollection<MeshReferenceSourceOption> ReferenceSourceOptions { get; }

    public string WindowTitle => "Mesh Inspector";

    public string DocumentTitle => summary.DocumentTitle;

    public string StatusMessage => summary.StatusMessage;

    public bool HasStatusMessage => summary.HasStatusMessage;

    public string PrimaryMeshCode => summary.PrimaryMeshCode;

    public bool HasPrimaryMeshCode => summary.HasPrimaryMeshCode;

    public bool HasNeighborMeshCodes => summary.HasNeighborMeshCodes;

    public bool HasNoNeighborMeshCodes => !HasNeighborMeshCodes;

    public bool HasOverlay => summary.HasOverlay;

    public string OverlayGeoJson => summary.OverlayGeoJson;

    public double? CenterLatitude => summary.CenterLatitude;

    public double? CenterLongitude => summary.CenterLongitude;

    public string ReferenceSourceTitle => summary.ReferenceSourceTitle;

    public string ReferenceSourceDescription => summary.ReferenceSourceDescription;

    public MeshReferenceSourceOption? SelectedReferenceSourceOption
    {
        get => selectedReferenceSourceOption;
        set
        {
            if (selectedReferenceSourceOption == value || value is null)
            {
                return;
            }

            selectedReferenceSourceOption = value;
            RefreshSummary();
            RaisePropertyChanged(nameof(SelectedReferenceSourceOption));
        }
    }

    public string ActionMessage
    {
        get => actionMessage;
        private set
        {
            if (string.Equals(actionMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            actionMessage = value;
            RaisePropertyChanged(nameof(ActionMessage));
            RaisePropertyChanged(nameof(HasActionMessage));
        }
    }

    public bool HasActionMessage => !string.IsNullOrWhiteSpace(ActionMessage);

    public bool CanCopyPrimaryMeshCode => HasPrimaryMeshCode;

    public bool CanSavePrimaryMeshCode
    {
        get => canSavePrimaryMeshCode;
        private set
        {
            if (canSavePrimaryMeshCode == value)
            {
                return;
            }

            canSavePrimaryMeshCode = value;
            RaisePropertyChanged(nameof(CanSavePrimaryMeshCode));
        }
    }

    public void SetActionMessage(string message)
    {
        ActionMessage = message ?? string.Empty;
    }

    public void MarkPrimaryMeshCodeSaved()
    {
        if (info is not null)
        {
            info.PrimaryMeshCode = new MeshCode { Value = PrimaryMeshCode };
        }

        ActionMessage = "The canonical primary mesh code was saved into GeoProjectInfo.";
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        MeshReferenceSource referenceSource = selectedReferenceSourceOption?.Source ?? MeshReferenceSource.SurveyPoint;
        summary = service.BuildSummary(currentState, info, referenceSource);
        CanSavePrimaryMeshCode = summary.CanSavePrimaryMeshCode;
        ReplaceCollection(DetailRows, summary.DetailRows);
        ReplaceCollection(NeighborMeshCodes, summary.NeighborMeshCodes);
        RaiseSummaryProperties();
    }

    private void RaiseSummaryProperties()
    {
        RaisePropertyChanged(nameof(DocumentTitle));
        RaisePropertyChanged(nameof(StatusMessage));
        RaisePropertyChanged(nameof(HasStatusMessage));
        RaisePropertyChanged(nameof(PrimaryMeshCode));
        RaisePropertyChanged(nameof(HasPrimaryMeshCode));
        RaisePropertyChanged(nameof(HasNeighborMeshCodes));
        RaisePropertyChanged(nameof(HasNoNeighborMeshCodes));
        RaisePropertyChanged(nameof(HasOverlay));
        RaisePropertyChanged(nameof(OverlayGeoJson));
        RaisePropertyChanged(nameof(CenterLatitude));
        RaisePropertyChanged(nameof(CenterLongitude));
        RaisePropertyChanged(nameof(ReferenceSourceTitle));
        RaisePropertyChanged(nameof(ReferenceSourceDescription));
        RaisePropertyChanged(nameof(CanCopyPrimaryMeshCode));
    }

    private static IReadOnlyCollection<MeshReferenceSourceOption> CreateReferenceSourceOptions()
    {
        return new[]
        {
            new MeshReferenceSourceOption
            {
                Source = MeshReferenceSource.SurveyPoint,
                Title = "Survey Point / Canonical Origin",
                Description = "Uses the canonical stored origin that drives the shared suite mesh key."
            },
            new MeshReferenceSourceOption
            {
                Source = MeshReferenceSource.ProjectBasePoint,
                Title = "Project Base Point",
                Description = "Uses the saved Working Project Base Point when available, otherwise falls back to the Revit Project Base Point estimate."
            }
        };
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
        {
            target.Add(value);
        }
    }

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

