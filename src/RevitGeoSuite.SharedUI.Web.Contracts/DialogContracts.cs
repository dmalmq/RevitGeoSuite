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
