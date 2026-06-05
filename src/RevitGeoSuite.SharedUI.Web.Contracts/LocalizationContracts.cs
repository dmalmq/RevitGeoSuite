using System.Collections.Generic;

namespace RevitGeoSuite.SharedUI.Web.Contracts;

/// <summary>Empty payload of the <c>localization.getAll</c> request.</summary>
[TsExport]
public sealed class LocalizationGetAllRequest
{
}

/// <summary>
/// Response of <c>localization.getAll</c> and payload of the <c>localization.changed</c> event.
/// Contains the active language identifier and a flat key→string dictionary that the Svelte
/// <c>i18n</c> module feeds to its <c>t()</c> function.
/// </summary>
[TsExport]
public sealed class LocalizationStrings
{
    public string Language { get; set; } = "english";

    public Dictionary<string, string> Strings { get; set; } = new();
}

/// <summary>Payload of the <c>localization.setLanguage</c> request.</summary>
[TsExport]
public sealed class LocalizationSetLanguageRequest
{
    public string Language { get; set; } = "english";
}

/// <summary>Response of the <c>localization.setLanguage</c> request.</summary>
[TsExport]
public sealed class LocalizationSetLanguageResponse
{
    public bool Success { get; set; }

    public string Language { get; set; } = "english";

    public Dictionary<string, string> Strings { get; set; } = new();

    public string? Error { get; set; }
}
