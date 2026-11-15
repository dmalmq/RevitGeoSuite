using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>How a RevitGeoSuite write reports what Revit said about it.</summary>
public static class RevitWriteText
{
    /// <summary>Each distinct warning once.</summary>
    public static IEnumerable<string> WarningLines(IEnumerable<string> warnings) =>
        warnings.Distinct(StringComparer.Ordinal).Select(text => "Revit warned: " + text);

    /// <summary>Why a write changed nothing: Revit's errors, then what the caller knows can cause them.</summary>
    public static string NotCommitted(string transactionName, IEnumerable<string> errors, IEnumerable<string> hints)
    {
        List<string> distinct = errors.Distinct(StringComparer.Ordinal).ToList();
        string why = distinct.Count > 0 ? ": " + string.Join(" ", distinct) : ".";
        string hint = string.Join(" ", hints);
        return $"Revit did not commit '{transactionName}', so nothing was changed{why}{(hint.Length > 0 ? " " + hint : string.Empty)}";
    }
}
