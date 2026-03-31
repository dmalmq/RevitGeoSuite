using System;
using System.Collections.ObjectModel;
using RevitGeoSuite.Core.Validation;

namespace RevitGeoSuite.Validation;

public sealed class ValidationViewModel
{
    public ValidationViewModel(ProjectHealthSummary summary)
    {
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        DetailRows = new ObservableCollection<DetailRow>(summary.DetailRows);
        Findings = new ObservableCollection<ValidationResult>(summary.Findings);
        ExportReadinessItems = new ObservableCollection<ExportReadinessItem>(summary.ExportReadiness.Items);
    }

    public ProjectHealthSummary Summary { get; }

    public ObservableCollection<DetailRow> DetailRows { get; }

    public ObservableCollection<ValidationResult> Findings { get; }

    public ObservableCollection<ExportReadinessItem> ExportReadinessItems { get; }

    public string WindowTitle => "Check Project";

    public string DocumentTitle => Summary.DocumentTitle;

    public string StatusTitle => Summary.StatusTitle;

    public string StatusMessage => Summary.StatusMessage;

    public string ExportReadinessTitle => Summary.ExportReadiness.StatusTitle;

    public string ExportReadinessMessage => Summary.ExportReadiness.StatusMessage;

    public bool HasFindings => Summary.HasFindings;

    public bool HasNoFindings => !Summary.HasFindings;
}
