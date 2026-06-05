namespace RevitGeoSuite.SharedUI.Web.Contracts;

/// <summary>Payload of the <c>readiness.getStatus</c> request.</summary>
[TsExport]
public sealed class ReadinessStatusRequest
{
}

/// <summary>Response of the <c>readiness.getStatus</c> request.</summary>
[TsExport]
public sealed class ReadinessStatusResponse
{
    public string? Error { get; set; }

    public string Status { get; set; } = string.Empty;

    public string StatusMessage { get; set; } = string.Empty;

    public ReadinessFinding[] Findings { get; set; } = System.Array.Empty<ReadinessFinding>();

    public ReadinessExportSummary ExportReadiness { get; set; } = new ReadinessExportSummary();
}

[TsExport]
public sealed class ReadinessFinding
{
    public string Code { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

[TsExport]
public sealed class ReadinessExportSummary
{
    public string Status { get; set; } = string.Empty;

    public string StatusTitle { get; set; } = string.Empty;

    public string StatusMessage { get; set; } = string.Empty;

    public ReadinessExportItem[] Items { get; set; } = System.Array.Empty<ReadinessExportItem>();
}

[TsExport]
public sealed class ReadinessExportItem
{
    public string Title { get; set; } = string.Empty;

    public bool IsSatisfied { get; set; }

    public string Detail { get; set; } = string.Empty;

    public string StatusText { get; set; } = string.Empty;
}
