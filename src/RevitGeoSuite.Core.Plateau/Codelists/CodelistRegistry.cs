using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.Core.Plateau.Codelists;

public sealed class CodelistRegistry
{
    private readonly IReadOnlyDictionary<string, CodelistEntry> entriesByCode;

    public CodelistRegistry(IReadOnlyCollection<CodelistEntry> entries)
    {
        if (entries is null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        entriesByCode = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Code))
            .GroupBy(entry => entry.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    public IReadOnlyCollection<CodelistEntry> GetAll()
    {
        return entriesByCode.Values.OrderBy(entry => entry.Code, StringComparer.Ordinal).ToArray();
    }

    public bool TryGetByCode(string code, out CodelistEntry? entry)
    {
        return entriesByCode.TryGetValue(code, out entry);
    }
}
