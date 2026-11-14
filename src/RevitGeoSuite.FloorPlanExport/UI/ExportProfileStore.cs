using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.Schema;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.Utilities;
using RevitGeoSuite.FloorPlanExport.Core.Validation;

namespace RevitGeoSuite.FloorPlanExport.UI;

public sealed class ExportProfileStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    private readonly string _settingsFilePath;
    private readonly string? _legacySettingsFilePath;
    private readonly GisRulesSource? _rules;

    /// <summary>
    /// <paramref name="rules"/> decides where project profiles live: the station's rules file when it
    /// has one, else this PC's profiles file. Global profiles always stay on this PC.
    /// </summary>
    public ExportProfileStore(string? settingsFilePath = null, string? legacySettingsFilePath = null, GisRulesSource? rules = null)
    {
        _rules = rules is { IsStation: true } ? rules : null;
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            _settingsFilePath = FloorPlanExportAppDataPaths.CurrentProfilesFilePath;
            _legacySettingsFilePath = string.IsNullOrWhiteSpace(legacySettingsFilePath)
                ? FloorPlanExportAppDataPaths.LegacyProfilesFilePath
                : legacySettingsFilePath!.Trim();
        }
        else
        {
            _settingsFilePath = settingsFilePath!.Trim();
            _legacySettingsFilePath = string.IsNullOrWhiteSpace(legacySettingsFilePath)
                ? null
                : legacySettingsFilePath!.Trim();
        }
    }

    /// <summary>The line a dialog shows after a profile was saved to the station's file; null for this PC's profiles.</summary>
    public string? SavedStatus => _rules?.SavedStatus;

    public LoadResult<IReadOnlyList<ExportProfile>> LoadWithDiagnostics(string projectKey)
    {
        LoadResult<ExportProfileDocument> result = JsonFileLoadHelper.Load(
            ResolveLoadPath(),
            createDefaultValue: () => new ExportProfileDocument(),
            deserialize: json => JsonConvert.DeserializeObject<ExportProfileDocument>(json),
            documentLabel: "Export profiles");
        List<ExportProfile> profiles = NormalizeProfiles(result.Value, projectKey);
        List<string> warnings = result.Warnings.ToList();
        if (_rules != null)
        {
            try
            {
                List<ExportProfile> station = (_rules.LoadStationFile()?.Profiles ?? new List<GisRulesProfile>())
                    .Select(GisRulesProfileMapper.ToExportProfile)
                    .Select(NormalizeProfile)
                    .ToList();
                profiles.RemoveAll(profile => profile.Scope == ExportProfileScope.Project &&
                    station.Any(shared => string.Equals(shared.Name, profile.Name, StringComparison.OrdinalIgnoreCase)));
                profiles.AddRange(station);
                profiles = profiles
                    .OrderBy(profile => profile.Scope)
                    .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                warnings.Add($"The station's profiles could not be read ({ex.Message}).");
            }
        }

        return new LoadResult<IReadOnlyList<ExportProfile>>(profiles, warnings);
    }

    /// <summary>
    /// Adds this PC's project profiles for the model to the station's rules file, together with this PC's
    /// category rules (see <see cref="GisRulesSource.MoveLocalIntoStation"/>), and sets them aside here
    /// once the station's write is confirmed. Does nothing for a model with no station file.
    /// </summary>
    public void MoveLocalToStation(string projectKey, GisRulesSource rules, ICollection<string> warnings)
    {
        if (!rules.IsStation)
        {
            return;
        }

        ExportProfileDocument document = LoadDocument();
        List<ExportProfile> local = document.ProjectProfiles != null &&
            document.ProjectProfiles.TryGetValue(projectKey, out List<ExportProfile>? projectProfiles)
                ? projectProfiles.Select(NormalizeProfile).Where(profile => profile.Name.Length > 0).ToList()
                : new List<ExportProfile>();
        List<GisRulesProfile> localProfiles = local
            .Select(profile => GisRulesProfileMapper.FromSettings(profile.Name, profile.ToSettings()))
            .ToList();
        if (!rules.MoveLocalIntoStation(localProfiles, warnings) || local.Count == 0)
        {
            return;
        }

        string backup = _settingsFilePath + "." + ShortHash(projectKey) + ".moved-to-station";
        File.WriteAllText(backup, JsonConvert.SerializeObject(local, JsonSettings));
        document.ProjectProfiles!.Remove(projectKey);
        SaveDocument(document);
    }

    public void SaveProfile(string projectKey, ExportProfile profile)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        ExportProfileDocument document = LoadDocument();
        ExportProfile normalizedProfile = NormalizeProfile(profile);
        if (normalizedProfile.Scope == ExportProfileScope.Project && _rules != null)
        {
            MoveLocalToStation(projectKey, _rules, new List<string>());
            _rules.ApplyToStation(new[]
            {
                GisRulesOperation.UpsertProfile(GisRulesProfileMapper.FromSettings(normalizedProfile.Name, normalizedProfile.ToSettings())),
            });
            return;
        }

        if (normalizedProfile.Scope == ExportProfileScope.Project)
        {
            List<ExportProfile> profiles = GetProjectProfiles(document, projectKey);
            UpsertProfile(profiles, normalizedProfile);
        }
        else
        {
            UpsertProfile(document.GlobalProfiles, normalizedProfile);
        }

        SaveDocument(document);
    }

    public void DeleteProfile(string projectKey, ExportProfile profile)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        ExportProfileDocument document = LoadDocument();
        string normalizedName = NormalizeName(profile.Name);
        if (profile.Scope == ExportProfileScope.Project && _rules != null)
        {
            MoveLocalToStation(projectKey, _rules, new List<string>());
            _rules.ApplyToStation(new[] { GisRulesOperation.DeleteProfile(normalizedName) });
            return;
        }

        if (profile.Scope == ExportProfileScope.Project)
        {
            List<ExportProfile> profiles = GetProjectProfiles(document, projectKey);
            profiles.RemoveAll(candidate => string.Equals(candidate.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            document.GlobalProfiles.RemoveAll(candidate => string.Equals(candidate.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        }

        SaveDocument(document);
    }

    public void ReplaceProfiles(string projectKey, ExportProfileScope scope, IEnumerable<ExportProfile> profiles)
    {
        ExportProfileDocument document = LoadDocument();
        List<ExportProfile> normalizedProfiles = (profiles ?? Array.Empty<ExportProfile>())
            .Where(profile => profile != null)
            .Select(NormalizeProfile)
            .Where(profile => profile.Name.Length > 0 && profile.Scope == scope)
            .ToList();

        if (scope == ExportProfileScope.Project && _rules != null)
        {
            MoveLocalToStation(projectKey, _rules, new List<string>());
            List<GisRulesOperation> operations = normalizedProfiles
                .Select(profile => GisRulesOperation.UpsertProfile(GisRulesProfileMapper.FromSettings(profile.Name, profile.ToSettings())))
                .ToList();
            foreach (GisRulesProfile shared in _rules.LoadStationFile()?.Profiles ?? new List<GisRulesProfile>())
            {
                if (!normalizedProfiles.Any(profile => string.Equals(profile.Name, shared.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    operations.Add(GisRulesOperation.DeleteProfile(shared.Name));
                }
            }

            _rules.ApplyToStation(operations);
            return;
        }

        if (scope == ExportProfileScope.Project)
        {
            List<ExportProfile> projectProfiles = GetProjectProfiles(document, projectKey);
            projectProfiles.Clear();
            projectProfiles.AddRange(normalizedProfiles);
        }
        else
        {
            document.GlobalProfiles.Clear();
            document.GlobalProfiles.AddRange(normalizedProfiles);
        }

        SaveDocument(document);
    }

    private ExportProfileDocument LoadDocument()
    {
        return JsonFileLoadHelper.Load(
            ResolveLoadPath(),
            createDefaultValue: () => new ExportProfileDocument(),
            deserialize: json => JsonConvert.DeserializeObject<ExportProfileDocument>(json),
            documentLabel: "Export profiles").Value;
    }

    private void SaveDocument(ExportProfileDocument document)
    {
        string? directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonConvert.SerializeObject(document, JsonSettings);
        File.WriteAllText(_settingsFilePath, json);
    }

    private static string ShortHash(string value)
    {
        using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
        return string.Concat(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value)).Take(4).Select(b => b.ToString("x2")));
    }

    private static List<ExportProfile> NormalizeProfiles(ExportProfileDocument document, string projectKey)
    {
        List<ExportProfile> profiles = new();
        profiles.AddRange((document.GlobalProfiles ?? new List<ExportProfile>())
            .Select(NormalizeProfile)
            .Where(profile => profile.Name.Length > 0));

        if (document.ProjectProfiles != null &&
            document.ProjectProfiles.TryGetValue(projectKey, out List<ExportProfile>? projectProfiles))
        {
            profiles.AddRange(projectProfiles
                .Select(NormalizeProfile)
                .Where(profile => profile.Name.Length > 0));
        }

        return profiles
            .OrderBy(profile => profile.Scope)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ExportProfile NormalizeProfile(ExportProfile profile)
    {
        return new ExportProfile
        {
            Name = NormalizeName(profile.Name),
            Scope = profile.Scope,
            OutputDirectory = profile.OutputDirectory?.Trim() ?? string.Empty,
            TargetEpsg = profile.TargetEpsg > 0 ? profile.TargetEpsg : ProjectInfo.DefaultTargetEpsg,
            FeatureTypes = profile.FeatureTypes == RevitGeoSuite.FloorPlanExport.Export.ExportFeatureType.None
                ? RevitGeoSuite.FloorPlanExport.Export.ExportFeatureType.All
                : profile.FeatureTypes,
            SelectedViewIds = (profile.SelectedViewIds ?? new List<long>()).Distinct().OrderBy(id => id).ToList(),
            IncrementalExportMode = profile.IncrementalExportMode,
            GenerateDiagnosticsReport = profile.GenerateDiagnosticsReport,
            GeneratePackageOutput = profile.GeneratePackageOutput,
            IncludePackageLegend = profile.IncludePackageLegend,
            PackagingMode = profile.PackagingMode,
            ValidateAfterWrite = profile.ValidateAfterWrite,
            GenerateQgisArtifacts = profile.GenerateQgisArtifacts,
            PostExportActions = profile.PostExportActions?.Clone() ?? new RevitGeoSuite.FloorPlanExport.Export.PostExportActionOptions(),
            GeometryRepairOptions = profile.GeometryRepairOptions?.Clone() ?? new RevitGeoSuite.FloorPlanExport.Core.Geometry.GeometryRepairOptions(),
            UiLanguage = profile.UiLanguage,
            CoordinateMode = profile.CoordinateMode,
            UnitSource = profile.UnitSource,
            UnitGeometrySource = profile.UnitGeometrySource,
            UnitAttributeSource = profile.UnitAttributeSource,
            RoomCategoryParameterName = string.IsNullOrWhiteSpace(profile.RoomCategoryParameterName) ? "Name" : profile.RoomCategoryParameterName.Trim(),
            LinkExportOptions = profile.LinkExportOptions?.Clone() ?? new RevitGeoSuite.FloorPlanExport.Export.LinkExportOptions(),
            UnitCategories = (profile.UnitCategories ?? new List<string>())
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Select(category => category.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SchemaProfiles = SchemaProfile.NormalizeProfiles(profile.SchemaProfiles).Select(schema => schema.Clone()).ToList(),
            ActiveSchemaProfileName = SchemaProfile.ResolveActiveName(profile.SchemaProfiles, profile.ActiveSchemaProfileName),
            ValidationPolicyProfiles = ValidationPolicyProfile.NormalizeProfiles(profile.ValidationPolicyProfiles).Select(policy => policy.Clone()).ToList(),
            ActiveValidationPolicyProfileName = ValidationPolicyProfile.ResolveActiveName(profile.ValidationPolicyProfiles, profile.ActiveValidationPolicyProfileName),
        };
    }

    private static void UpsertProfile(List<ExportProfile> profiles, ExportProfile profile)
    {
        int index = profiles.FindIndex(candidate => string.Equals(candidate.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            profiles[index] = profile;
        }
        else
        {
            profiles.Add(profile);
        }
    }

    private static List<ExportProfile> GetProjectProfiles(ExportProfileDocument document, string projectKey)
    {
        document.ProjectProfiles ??= new Dictionary<string, List<ExportProfile>>(StringComparer.Ordinal);
        if (!document.ProjectProfiles.TryGetValue(projectKey, out List<ExportProfile>? profiles))
        {
            profiles = new List<ExportProfile>();
            document.ProjectProfiles[projectKey] = profiles;
        }

        return profiles;
    }

    private static string NormalizeName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? string.Empty : name!.Trim();
    }

    private string ResolveLoadPath()
    {
        if (File.Exists(_settingsFilePath))
        {
            return _settingsFilePath;
        }

        if (!string.IsNullOrWhiteSpace(_legacySettingsFilePath) && File.Exists(_legacySettingsFilePath))
        {
            return _legacySettingsFilePath!;
        }

        return _settingsFilePath;
    }

    private sealed class ExportProfileDocument
    {
        public List<ExportProfile> GlobalProfiles { get; set; } = new();

        public Dictionary<string, List<ExportProfile>>? ProjectProfiles { get; set; }
    }
}
