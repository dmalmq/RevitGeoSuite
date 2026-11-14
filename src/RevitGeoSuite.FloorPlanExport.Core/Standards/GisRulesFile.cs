using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using RevitGeoSuite.FloorPlanExport.Core.Assignments;
using RevitGeoSuite.FloorPlanExport.Core.Geometry;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Schema;
using RevitGeoSuite.FloorPlanExport.Core.Validation;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>
/// <c>gis-rules.json</c>: the rules and export profiles one station shares, kept at
/// <c>&lt;station&gt;/gis-rules.json</c>. Any PC running RevitGeoSuite writes it, as per-key
/// operations (<see cref="GisRulesOperation"/>); Kanizu only reads it.
/// </summary>
/// <remarks>
/// An unknown field is kept as it is when the file is written again, so a newer RevitGeoSuite's
/// fields survive a write from an older one.
/// </remarks>
public sealed class GisRulesFile
{
    public const string SchemaName = "revitgeosuite.gis-rules";
    public const int CurrentVersion = 1;
    public const string FileName = "gis-rules.json";

    internal static readonly JsonSerializer Serializer = CreateSerializer();

    [JsonProperty("schema")]
    public string Schema { get; set; } = SchemaName;

    [JsonProperty("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonProperty("categoryRules")]
    public List<GisRulesRule> CategoryRules { get; set; } = new();

    [JsonProperty("profiles")]
    public List<GisRulesProfile> Profiles { get; set; } = new();

    [JsonProperty("changed")]
    public GisRulesChanged Changed { get; set; } = new();

    [JsonExtensionData]
    private IDictionary<string, JToken>? UnknownFields { get; set; }

    public static string PathFor(string stationFolder)
    {
        if (string.IsNullOrWhiteSpace(stationFolder))
        {
            throw new ArgumentException("A station folder is required.", nameof(stationFolder));
        }

        return Path.Combine(stationFolder.Trim(), FileName);
    }

    public static GisRulesFile Parse(string json, string sourceLabel)
    {
        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{sourceLabel} is not valid JSON: {ex.Message}", ex);
        }

        string? schema = root.Value<string>("schema");
        if (!string.Equals(schema, SchemaName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{sourceLabel} is not a GIS rules file (schema '{schema}').");
        }

        int version = root.Value<int?>("version") ?? 0;
        if (version < 1 || version > CurrentVersion)
        {
            throw new InvalidDataException(
                $"{sourceLabel} has version {version}; this RevitGeoSuite reads version {CurrentVersion}. Update RevitGeoSuite.");
        }

        GisRulesFile file;
        try
        {
            file = root.ToObject<GisRulesFile>(Serializer) ?? new GisRulesFile();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{sourceLabel} could not be read: {ex.Message}", ex);
        }

        file.CategoryRules = (file.CategoryRules ?? new List<GisRulesRule>())
            .Where(rule => rule != null && rule.Match.Trim().Length > 0)
            .ToList();
        file.Profiles = (file.Profiles ?? new List<GisRulesProfile>())
            .Where(profile => profile != null && profile.Name.Trim().Length > 0)
            .ToList();
        file.Changed ??= new GisRulesChanged();
        return file;
    }

    /// <summary>The file at <paramref name="path"/>, or null when there is none.</summary>
    public static GisRulesFile? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return Parse(File.ReadAllText(path, Encoding.UTF8), path);
    }

    public string ToJson()
    {
        using StringWriter writer = new(new StringBuilder(), System.Globalization.CultureInfo.InvariantCulture);
        using (JsonTextWriter json = new(writer) { Formatting = Formatting.Indented, Indentation = 2 })
        {
            Serializer.Serialize(json, this);
        }

        return writer.ToString().Replace("\r\n", "\n") + "\n";
    }

    public ProjectMappingRules ToMappingRules()
    {
        return ProjectMappingRules.FromRules(CategoryRules.Select(rule => rule.ToMappingRule()));
    }

    public GisRulesProfile? FindProfile(string name)
    {
        return Profiles.FirstOrDefault(profile => string.Equals(profile.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    internal JObject ToToken()
    {
        return JObject.FromObject(this, Serializer);
    }

    private static JsonSerializer CreateSerializer()
    {
        JsonSerializer serializer = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            ContractResolver = new DefaultContractResolver(),
        };
        serializer.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy()));
        return serializer;
    }
}

/// <summary>One category rule: a <see cref="MappingRule"/> as the file writes it.</summary>
public sealed class GisRulesRule
{
    [JsonProperty("type")]
    public MappingRuleType Type { get; set; }

    [JsonProperty("match")]
    public string Match { get; set; } = string.Empty;

    /// <summary>The category the match resolves to; absent for an accepted opening family.</summary>
    [JsonProperty("value")]
    public string? Value { get; set; }

    [JsonExtensionData]
    private IDictionary<string, JToken>? UnknownFields { get; set; }

    public static GisRulesRule From(MappingRule rule)
    {
        MappingRule normalized = rule.Normalize();
        return new GisRulesRule { Type = normalized.RuleType, Match = normalized.MatchValue, Value = normalized.ResolvedValue };
    }

    public MappingRule ToMappingRule()
    {
        return new MappingRule { RuleType = Type, MatchValue = Match, ResolvedValue = Value };
    }
}

public sealed class GisRulesChanged
{
    [JsonProperty("utc")]
    public string Utc { get; set; } = string.Empty;

    [JsonProperty("user")]
    public string User { get; set; } = string.Empty;

    [JsonProperty("revitGeoSuite")]
    public string RevitGeoSuite { get; set; } = string.Empty;

    [JsonExtensionData]
    private IDictionary<string, JToken>? UnknownFields { get; set; }
}

/// <summary>
/// A named export profile as the station shares it: only the settings that shape the output.
/// What stays per PC (output folder, language, side outputs, post-export actions, debug views)
/// is not here.
/// </summary>
public sealed class GisRulesProfile
{
    /// <summary>The feature type names <see cref="FeatureTypes"/> may hold.</summary>
    public static readonly IReadOnlyList<string> AllFeatureTypes = new[] { "unit", "detail", "opening", "level", "fixture" };

    /// <summary>The CRS a new profile exports to (JGD2011 / Japan Plane Rectangular CS IX).</summary>
    public const int DefaultTargetEpsg = 6677;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("targetEpsg")]
    public int TargetEpsg { get; set; } = DefaultTargetEpsg;

    [JsonProperty("coordinateMode")]
    public CoordinateExportMode CoordinateMode { get; set; } = CoordinateExportMode.SharedCoordinates;

    [JsonProperty("featureTypes")]
    public List<string> FeatureTypes { get; set; } = AllFeatureTypes.ToList();

    [JsonProperty("unitGeometrySource")]
    public UnitGeometrySource UnitGeometrySource { get; set; } = UnitGeometrySource.Unset;

    [JsonProperty("unitAttributeSource")]
    public UnitAttributeSource UnitAttributeSource { get; set; } = UnitAttributeSource.Unset;

    [JsonProperty("roomCategoryParameter")]
    public string RoomCategoryParameter { get; set; } = "Name";

    [JsonProperty("includeLinkedModels")]
    public bool IncludeLinkedModels { get; set; }

    [JsonProperty("selectedLinkInstanceIds")]
    public List<long> SelectedLinkInstanceIds { get; set; } = new();

    [JsonProperty("unitCategories")]
    public List<string> UnitCategories { get; set; } = new();

    [JsonProperty("simplifyStairUnits")]
    public bool SimplifyStairUnits { get; set; }

    [JsonProperty("simplifyEscalatorUnits")]
    public bool SimplifyEscalatorUnits { get; set; }

    [JsonProperty("use3DSectionBox")]
    public bool Use3DSectionBox { get; set; }

    [JsonProperty("sectionBoxAboveFloorMeters")]
    public double SectionBoxAboveFloorMeters { get; set; } = 1.2;

    [JsonProperty("sectionBoxBelowFloorMeters")]
    public double SectionBoxBelowFloorMeters { get; set; }

    [JsonProperty("geometryRepair")]
    public GeometryRepairOptions GeometryRepair { get; set; } = new();

    [JsonProperty("schemaProfiles")]
    public List<SchemaProfile> SchemaProfiles { get; set; } = new() { SchemaProfile.CreateCoreProfile() };

    [JsonProperty("activeSchemaProfile")]
    public string ActiveSchemaProfile { get; set; } = SchemaProfile.CoreProfileName;

    [JsonProperty("validationPolicies")]
    public List<ValidationPolicyProfile> ValidationPolicies { get; set; } = ValidationPolicyProfile.NormalizeProfiles(null);

    [JsonProperty("activeValidationPolicy")]
    public string ActiveValidationPolicy { get; set; } = ValidationPolicyProfile.RecommendedProfileName;

    [JsonExtensionData]
    private IDictionary<string, JToken>? UnknownFields { get; set; }

    public GisRulesProfile Clone()
    {
        return JObject.FromObject(this, GisRulesFile.Serializer).ToObject<GisRulesProfile>(GisRulesFile.Serializer)!;
    }

    /// <summary>
    /// Each setting that shapes the output and differs between <paramref name="yours"/> and
    /// <paramref name="station"/>, as <c>setting: yours -&gt; station</c>. Of the schema profiles and
    /// validation policies only the active ones are compared, since only they shape an export.
    /// </summary>
    public static IReadOnlyList<string> Differences(GisRulesProfile yours, GisRulesProfile station)
    {
        Dictionary<string, JToken?> mine = Flatten(yours.Effective());
        Dictionary<string, JToken?> theirs = Flatten(station.Effective());
        return mine.Keys
            .Concat(theirs.Keys.Where(key => !mine.ContainsKey(key)))
            .Where(key => key != "name")
            .Select(key => (Key: key, Mine: mine.TryGetValue(key, out JToken? a) ? a : null, Theirs: theirs.TryGetValue(key, out JToken? b) ? b : null))
            .Where(entry => !JToken.DeepEquals(entry.Mine, entry.Theirs))
            .Select(entry => $"{entry.Key}: {Show(entry.Mine)} -> {Show(entry.Theirs)}")
            .ToList();
    }

    private GisRulesProfile Effective()
    {
        GisRulesProfile effective = Clone();
        SchemaProfile schema = SchemaProfile.ResolveActive(SchemaProfiles, ActiveSchemaProfile);
        effective.SchemaProfiles = new List<SchemaProfile> { schema };
        effective.ActiveSchemaProfile = schema.Name;
        List<ValidationPolicyProfile> policies = ValidationPolicyProfile.NormalizeProfiles(ValidationPolicies);
        string policyName = ValidationPolicyProfile.ResolveActiveName(policies, ActiveValidationPolicy);
        effective.ValidationPolicies = policies
            .Where(policy => string.Equals(policy.Name, policyName, StringComparison.OrdinalIgnoreCase))
            .Take(1)
            .ToList();
        effective.ActiveValidationPolicy = policyName;
        return effective;
    }

    private static Dictionary<string, JToken?> Flatten(GisRulesProfile profile)
    {
        Dictionary<string, JToken?> leaves = new(StringComparer.Ordinal);
        Flatten(profile.ToToken(), string.Empty, leaves);
        return leaves;
    }

    private static void Flatten(JToken token, string path, Dictionary<string, JToken?> leaves)
    {
        if (token is JObject obj)
        {
            foreach (JProperty property in obj.Properties())
            {
                string name = path.Length == 0 ? property.Name : path + "." + property.Name;
                Flatten(property.Value, name switch
                {
                    "schemaProfiles" => "schemaProfile",
                    "validationPolicies" => "validationPolicy",
                    _ => name,
                }, leaves);
            }
        }
        else if (token is JArray array && array.Any(item => item is JContainer))
        {
            bool single = path == "schemaProfile" || path == "validationPolicy";
            for (int index = 0; index < array.Count; index++)
            {
                Flatten(array[index], single ? path : $"{path}[{index}]", leaves);
            }
        }
        else
        {
            leaves[path] = token;
        }
    }

    private static string Show(JToken? value)
    {
        return value switch
        {
            null => "(none)",
            JArray array => array.Count == 0 ? "(none)" : string.Join(", ", array.Select(Show)),
            JValue { Type: JTokenType.String } text => (string)text!,
            _ => value.ToString(Formatting.None),
        };
    }

    internal JObject ToToken()
    {
        return JObject.FromObject(this, GisRulesFile.Serializer);
    }
}
