using System;

namespace RevitGeoSuite.SharedUI.Web.Contracts;

/// <summary>Payload of the <c>dialog.openFolder</c> request.</summary>
[TsExport]
public sealed class DialogOpenFolderRequest
{
    public string? InitialPath { get; set; }

    public string? Title { get; set; }
}

/// <summary>Response of the <c>dialog.openFolder</c> request.</summary>
[TsExport]
public sealed class DialogOpenFolderResponse
{
    public string? Path { get; set; }

    public string? Error { get; set; }
}

/// <summary>Payload of the <c>dialog.openFile</c> request.</summary>
[TsExport]
public sealed class DialogOpenFileRequest
{
    public string? InitialPath { get; set; }

    public string? Title { get; set; }
}

/// <summary>Response of the <c>dialog.openFile</c> request.</summary>
[TsExport]
public sealed class DialogOpenFileResponse
{
    public string? Path { get; set; }

    public string[] Paths { get; set; } = Array.Empty<string>();

    public string? Error { get; set; }
}
