using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlValidationReport
{
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

    public bool HasErrors => Errors.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    public IReadOnlyList<string> AllMessages => Errors.Concat(Warnings).ToArray();
}

