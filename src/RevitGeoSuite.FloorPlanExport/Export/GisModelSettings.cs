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

    /// <summary>
    /// The field list this model exports with: the standard's, or the station's own list that
    /// <c>fields</c> names (a path, relative to the station folder or absolute). Never throws; a list
    /// that cannot be read falls back to the standard with a warning, and bad rows are left out.
    /// </summary>
    public GisFieldList LoadFieldList(ICollection<string>? warnings)
    {
        string pointer = (File?.Fields ?? GisModelFile.StandardFields).Trim();
        if (pointer.Length == 0 || string.Equals(pointer, GisModelFile.StandardFields, StringComparison.OrdinalIgnoreCase))
        {
            return GisFieldList.LoadStandard();
        }

        string stationFolder = Path == null ? string.Empty : System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(Path) ?? string.Empty) ?? string.Empty;
        string listPath = System.IO.Path.IsPathRooted(pointer) ? pointer : System.IO.Path.Combine(stationFolder, pointer);
        try
        {
            List<string> problems = new();
            GisFieldList list = GisFieldList.LoadStation(listPath, problems);
            foreach (string problem in problems)
            {
                warnings?.Add($"Field list: {problem}");
            }

            return list;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is ArgumentException || ex is NotSupportedException || ex is Newtonsoft.Json.JsonException)
        {
            warnings?.Add($"The field list {listPath} that gis-model.json names could not be read ({ex.Message}). The export uses the standard field list.");
            return GisFieldList.LoadStandard();
        }
    }

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
    /// Writes what this export used into the model's <c>gis-model.json</c>: identity, levels,
    /// versions, the linked models it selected and, when <paramref name="exportedViews"/> holds any,
    /// the views. Does nothing when the model has no station folder. Never throws.
    /// </summary>
    public static void RecordExport(
        Document document,
        IReadOnlyList<ViewPlan> exportedViews,
        int? crsEpsg,
        LinkExportOptions? linkOptions,
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
            if (linkOptions != null)
            {
                file.Links = (linkOptions.SelectedLinkInstanceIds ?? new List<long>())
                    .Distinct()
                    .Select(id => document.GetElement(new ElementId(id)) as RevitLinkInstance)
                    .Where(instance => instance != null)
                    .Select(instance => new GisModelLink { UniqueId = instance!.UniqueId, Name = instance.Name ?? string.Empty })
                    .ToList();
            }

            file.Save(path);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
        {
            warnings?.Add($"The model's GIS settings file {path} could not be written ({ex.Message}).");
        }
    }

    /// <summary>
    /// <paramref name="linkOptions"/> with the linked models the model's <c>gis-model.json</c> lists
    /// in place of its own selection, found by link instance UniqueId; a listed link that is gone
    /// is skipped with a warning. Unchanged when the file lists none.
    /// </summary>
    public static LinkExportOptions ApplyRecordedLinks(Document document, LinkExportOptions? linkOptions, ICollection<string> warnings)
    {
        LinkExportOptions options = linkOptions?.Clone() ?? new LinkExportOptions();
        GisModelFile? file = Load(document, warnings: null).File;
        if (file?.Links == null)
        {
            return options;
        }

        List<RevitLinkInstance> instances = new FilteredElementCollector(document)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .ToList();
        (IReadOnlyList<RevitLinkInstance> found, IReadOnlyList<GisModelLink> missing) = file.MatchLinks(instances, instance => instance.UniqueId);
        if (missing.Count > 0)
        {
            warnings.Add(
                $"The model's gis-model.json lists {file.Links.Count} linked models, but {missing.Count} no longer exist " +
                $"({string.Join(", ", missing.Select(link => string.IsNullOrEmpty(link.Name) ? link.UniqueId : link.Name))}); this export skips them.");
        }

        options.SelectedLinkInstanceIds = found.Select(instance => instance.Id.Value).ToList();
        return options;
    }

    /// <summary>The station folder and model id kept in the model, or empty strings when it has either not yet.</summary>
    internal static (string StationFolder, string ModelId) ReadStation(Document document)
    {
        if (document is null || document.IsFamilyDocument)
        {
            return (string.Empty, string.Empty);
        }

        GeoProjectInfo? info;
        try
        {
            info = new GeoProjectInfoStorage().Load(new RevitDocumentHandle(document));
        }
        catch (Exception)
        {
            return (string.Empty, string.Empty);
        }

        if (info == null || string.IsNullOrWhiteSpace(info.GisStationFolder) || string.IsNullOrWhiteSpace(info.ModelId))
        {
            return (string.Empty, string.Empty);
        }

        return (info.GisStationFolder!.Trim(), info.ModelId!.Trim());
    }

    private static (string? Path, string StationFolder, string ModelId) ResolvePath(Document document)
    {
        (string?, string, string) none = (null, string.Empty, string.Empty);
        (string stationFolder, string modelId) = ReadStation(document);
        if (stationFolder.Length == 0)
        {
            return none;
        }

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

        // Without views of its own (a one-off dialog export) the levels follow the views the
        // file already lists, so such an export does not drop the station export's levels.
        IReadOnlyList<ViewPlan> levelViews = views.Count > 0 ? views : ListedViews(document, file);
        file.Levels = ReadLevels(document, levelViews);
        file.Versions.Standard = ReadStandardVersion(document);
        file.Versions.LastExportRevitGeoSuite = typeof(GisModelSettings).Assembly.GetName().Version?.ToString() ?? string.Empty;
        file.Versions.LastExportUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static List<ViewPlan> ListedViews(Document document, GisModelFile file)
    {
        return (file.Views ?? new List<GisModelView>())
            .Where(view => !string.IsNullOrEmpty(view.UniqueId))
            .Select(view => document.GetElement(view.UniqueId) as ViewPlan)
            .Where(view => view != null)
            .Select(view => view!)
            .ToList();
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
