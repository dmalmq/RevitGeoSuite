using System.Collections.Generic;

namespace RevitGeoSuite.SharedUI.Web.Contracts;

[TsExport]
public sealed class HelpInitialStateRequest
{
}

[TsExport]
public sealed class HelpInitialStateResponse
{
    public string ProductName { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string ContextLabel { get; set; } = string.Empty;

    public string Language { get; set; } = "english";

    public string CurrentTopic { get; set; } = string.Empty;

    public List<HelpTopicOption> Topics { get; set; } = new();

    public HelpDocumentPayload Document { get; set; } = new();
}

[TsExport]
public sealed class HelpOpenTopicRequest
{
    public string Topic { get; set; } = string.Empty;

    public string Language { get; set; } = "english";
}

[TsExport]
public sealed class HelpTopicOption
{
    public string Topic { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
}

[TsExport]
public sealed class HelpDocumentPayload
{
    public string Topic { get; set; } = string.Empty;

    public string Language { get; set; } = "english";

    public string Title { get; set; } = string.Empty;

    public string Html { get; set; } = string.Empty;

    public bool IsFallback { get; set; }
}
