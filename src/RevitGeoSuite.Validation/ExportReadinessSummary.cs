using System.Collections.Generic;

namespace RevitGeoSuite.Validation;

public sealed class ExportReadinessSummary
{
    public ExportReadinessStatus Status { get; set; }

    public string StatusTitle { get; set; } = string.Empty;

    public string StatusMessage { get; set; } = string.Empty;

    public IReadOnlyCollection<ExportReadinessItem> Items { get; set; } = new ExportReadinessItem[0];
}
