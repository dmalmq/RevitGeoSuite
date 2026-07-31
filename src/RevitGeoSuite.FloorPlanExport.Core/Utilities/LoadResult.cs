using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Utilities;

public sealed class LoadResult<T>
{
    public LoadResult(T value, IEnumerable<string>? warnings = null)
    {
        Value = value;
        Warnings = (warnings ?? Array.Empty<string>())
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim())
            .ToList();
    }

    public T Value { get; }

    public IReadOnlyList<string> Warnings { get; }
}
