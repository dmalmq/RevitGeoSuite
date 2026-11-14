using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Assignments;
using RevitGeoSuite.FloorPlanExport.Core.Utilities;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>What a station's rules file holds for one export, as the manifest's <c>gis.rules</c> says it.</summary>
public sealed class GisRulesStamp
{
    public const string StationKind = "station";
    public const string LocalKind = "local";

    public GisRulesStamp(string source, string profile, string hash)
    {
        Source = source;
        Profile = profile;
        Hash = hash;
    }

    /// <summary><see cref="StationKind"/> or <see cref="LocalKind"/>.</summary>
    public string Source { get; }

    public string Profile { get; }

    public string Hash { get; }
}

/// <summary>
/// Where one model's category rules and shared profiles live: the station's
/// <c>gis-rules.json</c> when the model has a station folder, else this PC's per-project files.
/// Every reader and writer of those rules goes through here, so the choice is made in one place.
/// </summary>
public sealed class GisRulesSource
{
    private readonly string _projectKey;
    private readonly MappingRuleStore _perPcRules;
    private readonly GisRulesStore? _station;

    private GisRulesSource(string projectKey, MappingRuleStore perPcRules, GisRulesStore? station, string stationFolder)
    {
        _projectKey = projectKey;
        _perPcRules = perPcRules;
        _station = station;
        StationFolder = stationFolder;
    }

    public bool IsStation => _station != null;

    /// <summary>The station folder, also when this source fell back to this PC's rules; empty when there is none.</summary>
    public string StationFolder { get; }

    public string StationName => StationFolder.Length == 0
        ? string.Empty
        : new DirectoryInfo(StationFolder.TrimEnd('\\', '/')).Name;

    public string StationFilePath => StationFolder.Length == 0 ? string.Empty : GisRulesFile.PathFor(StationFolder);

    /// <summary>The line a dialog shows after a save that went to the station's file; null for this PC's rules.</summary>
    public string? SavedStatus => IsStation ? $"Saved for station {StationName}" : null;

    /// <summary>
    /// The source for a model: the station's file when the model has a station folder that exists,
    /// else this PC's rules. A model with a station folder that cannot be reached is read from this
    /// PC; <see cref="ForExport"/> says so.
    /// </summary>
    public static GisRulesSource Resolve(string? stationFolder, string projectKey, MappingRuleStore? perPcRules = null, GisRulesStoreOptions? options = null)
    {
        MappingRuleStore perPc = perPcRules ?? new MappingRuleStore();
        string folder = (stationFolder ?? string.Empty).Trim();
        if (folder.Length > 0 && Directory.Exists(folder))
        {
            return new GisRulesSource(projectKey, perPc, new GisRulesStore(GisRulesFile.PathFor(folder), options), folder);
        }

        return new GisRulesSource(projectKey, perPc, null, folder);
    }

    /// <summary>
    /// The source an export runs on. When the station's file cannot be read as it is or the station
    /// cannot be written to, the export uses this PC's rules, adds a warning and renames nothing;
    /// an unreadable version of the file stops the export, since its rules would be skipped.
    /// </summary>
    public GisRulesSource ForExport(ICollection<string> warnings)
    {
        if (_station == null)
        {
            if (StationFolder.Length > 0)
            {
                warnings.Add($"The station folder {StationFolder} cannot be reached. This export uses this PC's rules, not the station's, and its package says so.");
            }

            return this;
        }

        bool readable = true;
        try
        {
            _station.TryLoad();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            readable = false;
        }

        if (!readable || !_station.CanWrite())
        {
            warnings.Add($"The station folder {StationFolder} is read-only or cannot be reached. This export uses this PC's rules, not the station's, and its package says so.");
            return new GisRulesSource(_projectKey, _perPcRules, null, StationFolder);
        }

        return this;
    }

    public LoadResult<ProjectMappingRules> Load()
    {
        if (_station == null)
        {
            return _perPcRules.LoadWithDiagnostics(_projectKey);
        }

        try
        {
            GisRulesFile? file = _station.TryLoad();
            return new LoadResult<ProjectMappingRules>(file?.ToMappingRules() ?? ProjectMappingRules.Empty);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
        {
            return new LoadResult<ProjectMappingRules>(
                ProjectMappingRules.Empty,
                new[] { $"The station's rules file {_station.Path} could not be read ({ex.Message}). No station rules apply." });
        }
    }

    /// <summary>The station's file, or null when the source is this PC's or the file does not exist yet.</summary>
    public GisRulesFile? LoadStationFile()
    {
        return _station?.TryLoad();
    }

    /// <summary>
    /// Saves the change from <paramref name="before"/> (what the caller loaded) to
    /// <paramref name="after"/>. The station's file gets one operation per changed rule, so a rule
    /// another PC changed meanwhile stays; this PC's rules are replaced whole as before.
    /// </summary>
    public void Save(ProjectMappingRules before, ProjectMappingRules after)
    {
        if (_station == null)
        {
            _perPcRules.Save(_projectKey, after);
            return;
        }

        Dictionary<(MappingRuleType, string), MappingRule> was = before.Rules.ToDictionary(rule => (rule.RuleType, rule.MatchValue));
        Dictionary<(MappingRuleType, string), MappingRule> now = after.Rules.ToDictionary(rule => (rule.RuleType, rule.MatchValue));
        List<GisRulesOperation> operations = new();
        foreach (MappingRule rule in after.Rules)
        {
            if (!was.TryGetValue((rule.RuleType, rule.MatchValue), out MappingRule? old) || !string.Equals(old.ResolvedValue, rule.ResolvedValue, StringComparison.Ordinal))
            {
                operations.Add(GisRulesOperation.UpsertRule(rule));
            }
        }

        foreach (MappingRule rule in before.Rules.Where(rule => !now.ContainsKey((rule.RuleType, rule.MatchValue))))
        {
            operations.Add(GisRulesOperation.DeleteRule(rule.RuleType, rule.MatchValue));
        }

        if (operations.Count > 0)
        {
            _station.Apply(operations);
        }
    }

    public void ApplyToStation(IReadOnlyList<GisRulesOperation> operations)
    {
        (_station ?? throw new InvalidOperationException("This model has no station rules file.")).Apply(operations);
    }

    /// <summary>
    /// Adds this PC's rules and project profiles for the model to the station's file where the station
    /// has no entry for the same key or name, then sets this PC's rules aside. A conflict keeps the
    /// station's value and becomes a warning. Throws when the station's write cannot be confirmed, and
    /// then nothing is renamed. Returns true when this PC's rules were set aside, so the caller can set
    /// its profiles aside too.
    /// </summary>
    public bool MoveLocalIntoStation(IReadOnlyList<GisRulesProfile> localProfiles, ICollection<string> warnings)
    {
        if (_station == null)
        {
            return false;
        }

        ProjectMappingRules local = _perPcRules.LoadWithDiagnostics(_projectKey).Value;
        if (local.Rules.Count == 0 && localProfiles.Count == 0)
        {
            return false;
        }

        GisRulesMigrationPlan plan = GisRulesMigrationPlan.Create(_station.TryLoad(), local, localProfiles);
        if (plan.Operations.Count > 0)
        {
            _station.Apply(plan.Operations);
        }

        foreach (string warning in plan.Warnings)
        {
            warnings.Add(warning);
        }

        _perPcRules.MarkMovedToStation(_projectKey);
        return true;
    }
}

/// <summary>What moving this PC's rules and project profiles into a station's file adds, and what conflicts.</summary>
public sealed class GisRulesMigrationPlan
{
    private GisRulesMigrationPlan(IReadOnlyList<GisRulesOperation> operations, IReadOnlyList<string> warnings)
    {
        Operations = operations;
        Warnings = warnings;
    }

    public IReadOnlyList<GisRulesOperation> Operations { get; }

    public IReadOnlyList<string> Warnings { get; }

    public static GisRulesMigrationPlan Create(GisRulesFile? station, ProjectMappingRules local, IReadOnlyList<GisRulesProfile> localProfiles)
    {
        GisRulesFile current = station ?? new GisRulesFile();
        List<GisRulesOperation> operations = new();
        List<string> warnings = new();
        foreach (MappingRule rule in local.Rules)
        {
            GisRulesRule? existing = current.CategoryRules.FirstOrDefault(candidate =>
                candidate.Type == rule.RuleType && string.Equals(candidate.Match, rule.MatchValue, StringComparison.Ordinal));
            if (existing == null)
            {
                operations.Add(GisRulesOperation.UpsertRule(rule));
            }
            else if (!string.Equals(existing.Value, rule.ResolvedValue, StringComparison.Ordinal))
            {
                warnings.Add(
                    $"{rule.RuleType} '{rule.MatchValue}' is '{rule.ResolvedValue}' on this PC and '{existing.Value}' in the station's rules. The station's value is kept.");
            }
        }

        foreach (GisRulesProfile profile in localProfiles)
        {
            GisRulesProfile? existing = current.FindProfile(profile.Name);
            if (existing == null)
            {
                operations.Add(GisRulesOperation.UpsertProfile(profile));
            }
            else if (!Newtonsoft.Json.Linq.JToken.DeepEquals(existing.ToToken(), profile.ToToken()))
            {
                warnings.Add($"The profile '{profile.Name}' differs on this PC and in the station's rules. The station's profile is kept.");
            }
        }

        return new GisRulesMigrationPlan(operations, warnings);
    }
}
