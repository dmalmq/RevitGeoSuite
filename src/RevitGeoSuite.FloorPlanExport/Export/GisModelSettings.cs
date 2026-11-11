using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.FloorPlanExport.Export;

/// <summary>
/// The model's <c>gis-model.json</c> as the export sees it: found through the station folder and
/// model id kept in the model, read before an export, and written after one.
/// </summary>
public sealed class GisModelSettings
{
    private GisModelSettings(string? path, GisModelFile? file)
    {
        Path = path;
        File = file;
    }

    /// <summary>Where the file is or would be, or null when the model has no station folder or id yet.</summary>
    public string? Path { get; }

    /// <summary>The file as read, or null when there is none (or it could not be read).</summary>
    public GisModelFile? File { get; }

    public StairDrawingSettings StairDrawingSettings =>
        (File ?? GisModelFile.CreateDefault()).ToStairDrawingSettings();

    /// <summary>The settings for <paramref name="document"/>. Never throws; problems become warnings.</summary>
    public static GisModelSettings Load(Document document, ICollection<string>? warnings)
    {
        (string? path, _, _) = ResolvePath(document);
        if (path == null)
        {
            return new GisModelSettings(null, null);
        }

        try
        {
            return new GisModelSettings(path, GisModelFile.TryLoad(path));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
        {
            warnings?.Add($"The model's GIS settings file {path} could not be read ({ex.Message}). The export uses the standard's settings.");
            return new GisModelSettings(path, null);
        }
    }

    /// <summary>
    /// The views to preselect when nobody picked any: those the model's <c>gis-model.json</c>
    /// lists, else the <c>GIS_</c> views, else all of <paramref name="views"/>.
    /// </summary>
    public static IReadOnlyList<ViewPlan> ChooseDefaultViews(IReadOnlyList<ViewPlan> views)
    {
        Document? document = views.FirstOrDefault(view => view != null)?.Document;
        GisModelFile? file = document == null ? null : Load(document, warnings: null).File;
        return GisModelFile.ChooseDefaultViews(views, view => view.UniqueId, view => view.Name ?? string.Empty, file);
    }

    /// <summary>
    /// Writes what this export used into the model's <c>gis-model.json</c>: identity, levels, the
    /// exported views and versions. Does nothing when the model has no station folder. Never throws.
    /// </summary>
    public static void RecordExport(
        Document document,
        IReadOnlyList<ViewPlan> exportedViews,
        int? crsEpsg,
        ICollection<string>? warnings)
    {
        (string? path, string stationFolder, string modelId) = ResolvePath(document);
        if (path == null)
        {
            return;
        }

        try
        {
            GisModelFile file = GisModelFile.TryLoad(path) ?? GisModelFile.CreateFromTemplate();
            Fill(file, document, stationFolder, modelId, exportedViews, crsEpsg);
            file.Save(path);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
        {
            warnings?.Add($"The model's GIS settings file {path} could not be written ({ex.Message}).");
        }
    }

    private static (string? Path, string StationFolder, string ModelId) ResolvePath(Document document)
    {
        (string?, string, string) none = (null, string.Empty, string.Empty);
        if (document is null || document.IsFamilyDocument)
        {
            return none;
        }

        GeoProjectInfo? info;
        try
        {
            info = new GeoProjectInfoStorage().Load(new RevitDocumentHandle(document));
        }
        catch (Exception)
        {
            return none;
        }

        if (info == null || string.IsNullOrWhiteSpace(info.GisStationFolder) || string.IsNullOrWhiteSpace(info.ModelId))
        {
            return none;
        }

        string stationFolder = info.GisStationFolder!.Trim();
        string modelId = info.ModelId!.Trim();
        try
        {
            return (GisModelFile.PathFor(stationFolder, modelId), stationFolder, modelId);
        }
        catch (ArgumentException)
        {
            return none;
        }
    }

    private static void Fill(
        GisModelFile file,
        Document document,
        string stationFolder,
        string modelId,
        IReadOnlyList<ViewPlan> exportedViews,
        int? crsEpsg)
    {
        file.Identity.ModelId = modelId;
        file.Identity.Station = new DirectoryInfo(stationFolder.TrimEnd('\\', '/')).Name;
        file.Identity.Building = DocumentProjectKeyBuilder.CreateDisplayName(document);
        if (crsEpsg.HasValue && crsEpsg.Value > 0)
        {
            file.Identity.CrsEpsg = crsEpsg;
        }

        List<ViewPlan> views = (exportedViews ?? Array.Empty<ViewPlan>()).Where(view => view != null).ToList();
        if (views.Count > 0)
        {
            file.Views = views
                .Select(view => new GisModelView { UniqueId = view.UniqueId, Name = view.Name ?? string.Empty })
                .ToList();
        }

        file.Levels = ReadLevels(document, views);
        file.Versions.Standard = ReadStandardVersion(document);
        file.Versions.LastExportRevitGeoSuite = typeof(GisModelSettings).Assembly.GetName().Version?.ToString() ?? string.Empty;
        file.Versions.LastExportUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    /// <summary>The levels with a GIS <c>level_id</c>, and the levels of the exported views, by elevation.</summary>
    private static List<GisModelLevel> ReadLevels(Document document, IReadOnlyList<ViewPlan> views)
    {
        HashSet<long> exportedLevelIds = new(views
            .Where(view => view.GenLevel != null)
            .Select(view => view.GenLevel.Id.Value));
        return new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Select(level => new
            {
                Level = level,
                LevelId = ReadString(level, GisSharedParameters.LevelId),
            })
            .Where(entry => entry.LevelId.Length > 0 || exportedLevelIds.Contains(entry.Level.Id.Value))
            .OrderBy(entry => entry.Level.Elevation)
            .Select(entry => new GisModelLevel
            {
                UniqueId = entry.Level.UniqueId,
                Name = entry.Level.Name ?? string.Empty,
                LevelId = entry.LevelId,
                Ordinal = ReadInteger(entry.Level, GisSharedParameters.Ordinal),
                ShortName = ReadString(entry.Level, GisSharedParameters.ShortName),
            })
            .ToList();
    }

    private static int? ReadStandardVersion(Document document)
    {
        string text = ReadString(document.ProjectInformation, GisSharedParameters.GisStandardVersion);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version) ? version : null;
    }

    private static string ReadString(Element? element, string parameterName)
    {
        Parameter? parameter = element?.LookupParameter(parameterName);
        if (parameter == null || !parameter.HasValue)
        {
            return string.Empty;
        }

        string? value = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
        return value?.Trim() ?? string.Empty;
    }

    private static int? ReadInteger(Element element, string parameterName)
    {
        Parameter? parameter = element.LookupParameter(parameterName);
        if (parameter == null || !parameter.HasValue)
        {
            return null;
        }

        if (parameter.StorageType == StorageType.Integer)
        {
            return parameter.AsInteger();
        }

        string? text = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
        return int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
    }
}
