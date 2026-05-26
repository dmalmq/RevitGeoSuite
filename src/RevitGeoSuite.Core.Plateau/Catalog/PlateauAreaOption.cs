using System;
using System.Collections.Generic;

namespace RevitGeoSuite.Core.Plateau.Catalog;

public sealed class PlateauAreaOption
{
    public PlateauAreaOption(string code, IReadOnlyList<string> aliases, string label, string pref, string city, string ward)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Aliases = aliases ?? Array.Empty<string>();
        Label = label ?? string.Empty;
        Pref = pref ?? string.Empty;
        City = city ?? string.Empty;
        Ward = ward ?? string.Empty;
    }

    public string Code { get; }

    public IReadOnlyList<string> Aliases { get; }

    public string Label { get; }

    public string Pref { get; }

    public string City { get; }

    public string Ward { get; }

    public bool MatchesCode(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return false;
        if (string.Equals(candidate, Code, StringComparison.Ordinal)) return true;
        foreach (string alias in Aliases)
        {
            if (string.Equals(candidate, alias, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
