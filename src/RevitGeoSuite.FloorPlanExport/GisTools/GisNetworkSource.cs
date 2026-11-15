using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;

namespace RevitGeoSuite.FloorPlanExport.GisTools;

/// <summary>The network file Kanizu writes for this model, read and drawn in this model's coordinates.</summary>
public sealed class GisNetworkView
{
    public GisNetworkView(string path, DateTime writtenAt, GisNetworkFile? file, NetworkPicture? picture, string problem)
    {
        Path = path;
        WrittenAt = writtenAt;
        File = file;
        Picture = picture;
        Problem = problem;
    }

    /// <summary>Where the file is, or would be; empty when the model has no id or station folder.</summary>
    public string Path { get; }

    /// <summary>The file's last write time, to tell when Kanizu wrote it again; MinValue when there is no file.</summary>
    public DateTime WrittenAt { get; }

    public GisNetworkFile? File { get; }

    public NetworkPicture? Picture { get; }

    /// <summary>Why the file could not be read; empty when it was read or is not there.</summary>
    public string Problem { get; }
}

public static class GisNetworkSource
{
    /// <summary>
    /// Revit levels by their level_id value (the export's GIS level_id), as Kanizu names floors in the
    /// drift and network files. A level_id two levels share names neither.
    /// </summary>
    public static Dictionary<string, Level> LevelsByLevelId(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Select(level => (Key: level.LookupParameter(SharedParameterManager.ImdfLevelIdParameterName)?.AsString() ?? string.Empty, Level: level))
            .Where(pair => pair.Key.Length > 0)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Level, StringComparer.Ordinal);

    /// <summary><c>&lt;station&gt;/gis-network/&lt;model&gt;.gis-network.json</c>, or empty when the model has no id or station folder.</summary>
    public static string Locate(Document document)
    {
        (string modelKey, string station, _) = GisDriftSource.Locate(document);
        if (modelKey.Length == 0 || station.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return System.IO.Path.Combine(station, GisNetwork.Folder, GisNetwork.FileName(modelKey));
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    public static DateTime WrittenAt(string path)
    {
        try
        {
            return path.Length > 0 && System.IO.File.Exists(path) ? System.IO.File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>Reads this model's network file and draws it at this model's levels. Never throws for a bad file: a file the reader cannot take is a problem, not an error.</summary>
    public static GisNetworkView Read(Document document)
    {
        string path = Locate(document);
        DateTime writtenAt = WrittenAt(path);
        if (writtenAt == DateTime.MinValue)
        {
            return new GisNetworkView(path, writtenAt, null, null, string.Empty);
        }

        try
        {
            GisNetworkFile file = GisNetwork.Read(path);
            Dictionary<string, NetworkLevel> levels = LevelsByLevelId(document)
                .ToDictionary(pair => pair.Key, pair => new NetworkLevel(pair.Value.Id.Value, pair.Value.ProjectElevation), StringComparer.Ordinal);
            NetworkPicture picture = GisNetwork.Draw(file, new SharedCoordinateProjector(document.ActiveProjectLocation).Transform, ShapeEditWork.ModelEpsg(document), levels);
            return new GisNetworkView(path, writtenAt, file, picture, string.Empty);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            return new GisNetworkView(path, writtenAt, null, null, error.Message);
        }
    }

    /// <summary>The network findings for the GIS standard audit; a file that cannot be read is one warning, never an error that fails the Standard tab.</summary>
    public static IReadOnlyList<GisAuditFinding> Audit(Document document)
    {
        string path = Locate(document);
        if (WrittenAt(path) == DateTime.MinValue)
        {
            return Array.Empty<GisAuditFinding>();
        }

        try
        {
            return GisNetwork.Audit(GisNetwork.Read(path), uniqueId => document.GetElement(uniqueId)?.Id.Value);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            return new[] { new GisAuditFinding(GisAuditSeverity.Warning, GisNetwork.Topic, $"The walking network file from Kanizu could not be read: {error.Message}") };
        }
    }
}
