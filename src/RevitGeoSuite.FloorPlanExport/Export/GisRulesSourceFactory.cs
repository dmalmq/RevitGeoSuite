using System;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Export;

/// <summary>
/// Decides where a model's category rules and shared profiles live: the station's
/// <c>gis-rules.json</c> when the model has a station folder and model id, the same test
/// <see cref="GisModelSettings"/> makes for <c>gis-model.json</c>, else this PC's own stores.
/// </summary>
public static class GisRulesSourceFactory
{
    // Revit API calls run on one thread, so a scope opened in a Revit callback covers exactly that callback's reads.
    [ThreadStatic]
    private static string? stationOverride;

    public static GisRulesSource For(Document document)
    {
        string stationFolder = stationOverride ?? GisModelSettings.ReadStation(document).StationFolder;
        return GisRulesSource.Resolve(stationFolder, DocumentProjectKeyBuilder.Create(document));
    }

    /// <summary>
    /// Until the returned scope is disposed, <see cref="For"/> on this thread reads the rules of
    /// <paramref name="stationFolder"/> instead of the folder the model records: an audit before a
    /// send to another station checks that station's rules, which the send will export with.
    /// </summary>
    public static IDisposable ReadFrom(string stationFolder)
    {
        string? previous = stationOverride;
        stationOverride = stationFolder.Trim();
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string? previous;
        private bool disposed;

        public Scope(string? previous) => this.previous = previous;

        public void Dispose()
        {
            if (!disposed)
            {
                stationOverride = previous;
                disposed = true;
            }
        }
    }
}
