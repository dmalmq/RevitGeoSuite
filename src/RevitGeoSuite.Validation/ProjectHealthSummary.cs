using System.Collections.Generic;
using RevitGeoSuite.Core.Validation;

namespace RevitGeoSuite.Validation;

public sealed class ProjectHealthSummary
{
    public string DocumentTitle { get; set; } = string.Empty;

    public string StatusTitle { get; set; } = string.Empty;

    public string StatusMessage { get; set; } = string.Empty;

    public IReadOnlyCollection<DetailRow> DetailRows { get; set; } = new DetailRow[0];

    public IReadOnlyCollection<ValidationResult> Findings { get; set; } = new ValidationResult[0];

    public ExportReadinessSummary ExportReadiness { get; set; } = new ExportReadinessSummary();

    public bool HasFindings => Findings.Count > 0;
}
