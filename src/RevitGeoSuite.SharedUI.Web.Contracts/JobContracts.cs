namespace RevitGeoSuite.SharedUI.Web.Contracts;

/// <summary>Returned synchronously when a long-running job is started.</summary>
[TsExport]
public sealed class JobStarted
{
    public string JobId { get; set; } = string.Empty;
}

/// <summary>Streamed as the <c>job.progress</c> event while a job runs.</summary>
[TsExport]
public sealed class JobProgress
{
    public string JobId { get; set; } = string.Empty;
    public string? Phase { get; set; }
    public int Current { get; set; }
    public int Total { get; set; }
    public int Percent { get; set; }
    public string? Message { get; set; }
}

/// <summary>Sent as the <c>job.completed</c> event when a job finishes successfully.</summary>
[TsExport]
public sealed class JobCompleted
{
    public string JobId { get; set; } = string.Empty;
    public object? Result { get; set; }
}

/// <summary>Sent as the <c>job.failed</c> event when a job throws or is cancelled.</summary>
[TsExport]
public sealed class JobFailed
{
    public string JobId { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public bool Cancelled { get; set; }
}

/// <summary>Payload of the <c>job.cancel</c> request.</summary>
[TsExport]
public sealed class JobCancelRequest
{
    public string JobId { get; set; } = string.Empty;
}

/// <summary>Response of the <c>job.cancel</c> request.</summary>
[TsExport]
public sealed class JobCancelResponse
{
    public bool Cancelled { get; set; }
}
