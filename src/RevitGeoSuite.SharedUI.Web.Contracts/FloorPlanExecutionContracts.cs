using System.Collections.Generic;

namespace RevitGeoSuite.SharedUI.Web.Contracts;

[TsExport]
public sealed class ExecutionProgressInitialStateRequest
{
}

[TsExport]
public sealed class ExecutionProgressInitialStateResponse
{
    public string Language { get; set; } = "english";

    public ExecutionProgressPayload Progress { get; set; } = new();
}

[TsExport]
public sealed class ExecutionProgressPayload
{
    public string StatusText { get; set; } = string.Empty;

    public int CompletedSteps { get; set; }

    public int TotalSteps { get; set; } = 1;

    public bool IsCancelling { get; set; }

    public string StartedAtUtc { get; set; } = string.Empty;
}

[TsExport]
public sealed class ExecutionActionResponse
{
    public bool Success { get; set; }

    public string? Error { get; set; }
}

[TsExport]
public sealed class ExportResultInitialStateRequest
{
}

[TsExport]
public sealed class ExportResultInitialStateResponse
{
    public string Language { get; set; } = "english";

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string OutputDirectory { get; set; } = string.Empty;

    public bool CanOpenOutputDirectory { get; set; }

    public ExportResultSummaryPayload Summary { get; set; } = new();

    public List<ExportResultFilePayload> Files { get; set; } = new();

    public List<string> Warnings { get; set; } = new();

    public List<string> Changes { get; set; } = new();

    public List<string> PackageLines { get; set; } = new();

    public List<ExportResultTimingPayload> Timings { get; set; } = new();
}

[TsExport]
public sealed class ExportResultSummaryPayload
{
    public int ViewCount { get; set; }

    public int ArtifactCount { get; set; }

    public int WrittenArtifactCount { get; set; }

    public int ReusedArtifactCount { get; set; }

    public int FeatureCount { get; set; }

    public int WarningCount { get; set; }

    public int PackageErrorCount { get; set; }

    public int PackageWarningCount { get; set; }
}

[TsExport]
public sealed class ExportResultFilePayload
{
    public string ViewName { get; set; } = string.Empty;

    public string LevelName { get; set; } = string.Empty;

    public string FeatureType { get; set; } = string.Empty;

    public int FeatureCount { get; set; }

    public string OutputFilePath { get; set; } = string.Empty;
}

[TsExport]
public sealed class ExportResultTimingPayload
{
    public string PhaseName { get; set; } = string.Empty;

    public long DurationMilliseconds { get; set; }

    public string DurationText { get; set; } = string.Empty;
}
