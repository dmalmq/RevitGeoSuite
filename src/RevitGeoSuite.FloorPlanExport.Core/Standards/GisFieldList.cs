using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Schema;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>
/// The field list: the one contract for every GIS field. Revit binds a parameter for each
/// parameter row, RevitGeoSuite exports a column for it, and Kanizu creates and fills the GDB field
/// with the alias and pick list given here.
/// </summary>
/// <remarks>
/// <c>standards/fields.json</c> is the standard list, generated from <see cref="CreateStandard"/>.
/// A station can keep its own list on the share (named by <c>fields</c> in a model's
/// <c>gis-model.json</c>): a copy of the standard with rows added. Adding a parameter is then one
/// row in that file, with no code change. The standard's <c>core</c> rows always win over a
/// station list's, so a station list cannot change what the export itself writes.
/// </remarks>
public sealed class GisFieldList
{
    public const string SchemaName = "revitgeosuite.gis-fields";
    public const int CurrentVersion = 1;
    public const string FileName = "fields.json";

    /// <summary>The group extra shared parameters are written under.</summary>
    public const string ExtraParameterGroupName = "Kanizu GIS fields";

    public static IReadOnlyList<string> Layers { get; } = new[] { "unit", "detail", "opening", "fixture", "level" };

    private static readonly Regex FieldNamePattern = new("^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant);

    public GisFieldList(IReadOnlyList<GisField> fields)
    {
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public IReadOnlyList<GisField> Fields { get; }

    /// <summary>The standard list shipped in <c>standards/fields.json</c>.</summary>
    public static GisFieldList CreateStandard()
    {
        string[] direction = { "up", "down", "both" };
        string[] restriction = { "rachi_nai", "rachi_gai" };
        List<GisField> fields = new()
        {
            Core("unit", "id", "ID", "Stable export id of the element."),
            Core("unit", RevitKey.AttributeName, "Revitキー", "Model, element UniqueId and part. Matches rows between exports."),
            Core("unit", "category", "カテゴリ", "Unit category: the floor's category, else its type's gis_category.", values: FloorTypeStandard.KnownCategories),
            Core("unit", "restrict", "制限", "Restriction, such as rachi_nai.", values: restriction),
            Core("unit", "name", "名称", "Name of the unit."),
            Core("unit", "alt_name", "略称", "Short name of the unit."),
            Core("unit", "level_id", "レベルID", "The level the unit is on."),
            Core("unit", VerticalCirculation.DirectionAttributeName, "方向", "Travel direction of a stair, escalator or moving walkway.", values: direction),
            Core("unit", VerticalCirculation.ToLevelAttributeName, "行先レベル", "The level a stair or escalator leads to."),
            Core("unit", "source", "ソース", "What the unit was made from."),
            Core("unit", "display_point", "表示点", "Label point."),
            Core("unit", "preview_fill_color", "プレビュー色", "Fill colour in the preview."),
            Core("detail", "id", "ID", "Stable export id."),
            Core("detail", RevitKey.AttributeName, "Revitキー", "Model, element UniqueId and part."),
            Core("detail", DetailCategory.AttributeName, "カテゴリ", "What the line shows.", values: new[] { DetailCategory.Steps, DetailCategory.Line }),
            Core("detail", RevitKey.ParentAttributeName, "親キー", "revit_key of the stair, escalator or line style the line was drawn from."),
            Core("detail", "level_id", "レベルID", "The level the line is on."),
            Core("detail", "element_id", "要素ID", "Revit element id.", type: GisFieldType.Integer),
            Core("opening", "id", "ID", "Stable export id."),
            Core("opening", RevitKey.AttributeName, "Revitキー", "Model, element UniqueId and part."),
            Core("opening", "category", "カテゴリ", "Opening category."),
            Core("opening", "level_id", "レベルID", "The level the opening is on."),
            Core("opening", "element_id", "要素ID", "Revit element id.", type: GisFieldType.Integer),
            Core("fixture", "id", "ID", "Stable export id."),
            Core("fixture", RevitKey.AttributeName, "Revitキー", "Model, element UniqueId and part."),
            Core("fixture", "type", "種別", "Fixture type."),
            Core("fixture", "name", "名称", "Name of the fixture."),
            Core("fixture", "alt_name", "略称", "Short name of the fixture."),
            Core("fixture", "level_id", "レベルID", "The level the fixture is on."),
            Core("fixture", "source", "ソース", "What the fixture was made from."),
            Core("fixture", "display_point", "表示点", "Label point."),
            Core("level", "id", "ID", "Stable export id of the level."),
            Core("level", RevitKey.AttributeName, "Revitキー", "Model, level UniqueId and part."),
            Core("level", "level_name", "レベル名", "Revit level name."),
            Core("level", "ordinal", "階数", "Floor number.", type: GisFieldType.Integer),
            Core("level", "elevation_m", "標高", "Elevation in metres.", type: GisFieldType.Real),
        };

        return new GisFieldList(fields);
    }

    /// <summary>The standard list as embedded in this assembly.</summary>
    public static GisFieldList LoadStandard()
    {
        return Parse(GisStandardFiles.ReadFieldList(), FileName);
    }

    /// <summary>
    /// A station's list: its own rows, with the standard's core rows put back over any it changed
    /// or dropped. <paramref name="problems"/> lists rows that were left out and why.
    /// </summary>
    public static GisFieldList LoadStation(string path, ICollection<string> problems)
    {
        GisFieldList station = Parse(File.ReadAllText(path, Encoding.UTF8), path, problems);
        return MergeOntoStandard(CreateStandard(), station, problems);
    }

    public static GisFieldList MergeOntoStandard(GisFieldList standard, GisFieldList station, ICollection<string> problems)
    {
        List<GisField> merged = new(standard.Fields);
        HashSet<string> core = new(standard.Fields.Select(field => field.Key), StringComparer.Ordinal);
        foreach (GisField field in station.Fields)
        {
            if (core.Contains(field.Key))
            {
                continue;
            }

            if (field.Source.Kind == GisFieldSourceKind.Core)
            {
                problems?.Add($"{field.Layer}.{field.Name}: 'core' is only for fields the export itself writes; the row is left out.");
                continue;
            }

            merged.Add(field);
        }

        return new GisFieldList(merged);
    }

    public static GisFieldList Parse(string json, string sourceLabel, ICollection<string>? problems = null)
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

        if (!string.Equals(root.Value<string>("schema"), SchemaName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{sourceLabel} is not a GIS field list (schema '{root.Value<string>("schema")}').");
        }

        int version = root.Value<int?>("version") ?? 0;
        if (version < 1 || version > CurrentVersion)
        {
            throw new InvalidDataException($"{sourceLabel} has version {version}; this RevitGeoSuite reads version {CurrentVersion}. Update RevitGeoSuite.");
        }

        List<GisField> fields = new();
        HashSet<string> keys = new(StringComparer.Ordinal);
        Dictionary<string, Guid> parameterGuids = new(StringComparer.Ordinal);
        foreach (JToken token in root["fields"] as JArray ?? new JArray())
        {
            GisField? field = token.ToObject<GisField>();
            string? problem = field == null ? "an empty row" : Validate(field, parameterGuids);
            if (problem == null && !keys.Add(field!.Key))
            {
                problem = $"{field.Key} appears twice";
            }

            if (problem != null)
            {
                if (problems == null)
                {
                    throw new InvalidDataException($"{sourceLabel}: {problem}.");
                }

                problems.Add($"{sourceLabel}: {problem}; the row is left out.");
                continue;
            }

            fields.Add(field!);
        }

        return new GisFieldList(fields);
    }

    public string ToJson()
    {
        JObject root = new()
        {
            ["schema"] = SchemaName,
            ["version"] = CurrentVersion,
            ["note"] = "One row per GIS field. Add a field by adding a row in a station copy of this file; see standards/README.md.",
            ["fields"] = JArray.FromObject(Fields, JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore })),
        };
        return root.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";
    }

    /// <summary>The shared parameters the list adds to the standard's, from its parameter rows.</summary>
    public IReadOnlyList<GisParameterDefinition> ExtraParameters()
    {
        Dictionary<string, GisParameterDefinition> extra = new(StringComparer.Ordinal);
        foreach (GisField field in Fields.Where(field => field.Source.Kind == GisFieldSourceKind.Parameter))
        {
            string name = field.Source.Parameter!;
            if (GisSharedParameters.TryGet(name, out _) || extra.ContainsKey(name))
            {
                continue;
            }

            extra[name] = new GisParameterDefinition(
                name,
                field.Source.Guid!.Value,
                field.Type == GisFieldType.Integer ? GisParameterDataType.Integer : GisParameterDataType.Text,
                field.Source.Binding == GisFieldBinding.Type ? GisParameterBinding.Type : GisParameterBinding.Instance,
                field.Source.Categories!,
                string.IsNullOrWhiteSpace(field.Description) ? $"GIS field {field.Layer}.{field.Name}." : field.Description);
        }

        return extra.Values.ToList();
    }

    /// <summary>The columns the export adds for the list's Revit-owned parameter and derived rows.</summary>
    public IReadOnlyList<CustomAttributeMapping> ToMappings()
    {
        return Fields
            .Where(field => field.Owner == GisFieldOwner.Revit)
            .Where(field => field.Source.Kind == GisFieldSourceKind.Parameter || field.Source.Kind == GisFieldSourceKind.Derived)
            .Select(field => new CustomAttributeMapping
            {
                Layer = ToLayerType(field.Layer),
                FieldName = field.Name,
                SourceKind = field.Source.Kind == GisFieldSourceKind.Parameter
                    ? CustomAttributeSourceKind.RevitParameter
                    : CustomAttributeSourceKind.DerivedValue,
                SourceKey = field.Source.Kind == GisFieldSourceKind.Parameter ? field.Source.Parameter! : field.Source.Derived!,
                TargetType = field.Type switch
                {
                    GisFieldType.Integer => ExportAttributeType.Integer,
                    GisFieldType.Real => ExportAttributeType.Real,
                    _ => ExportAttributeType.Text,
                },
            })
            .ToList();
    }

    /// <summary>
    /// <paramref name="profile"/> with the list's columns added. A column the profile already maps
    /// for that layer keeps the profile's mapping.
    /// </summary>
    public SchemaProfile ApplyTo(SchemaProfile? profile)
    {
        SchemaProfile result = profile?.Clone() ?? SchemaProfile.CreateCoreProfile();
        HashSet<string> present = new(
            result.Mappings.Select(mapping => mapping.Layer + "." + mapping.FieldName),
            StringComparer.OrdinalIgnoreCase);
        foreach (CustomAttributeMapping mapping in ToMappings())
        {
            if (present.Add(mapping.Layer + "." + mapping.FieldName))
            {
                result.Mappings.Add(mapping);
            }
        }

        return result;
    }

    private static SchemaLayerType ToLayerType(string layer) => layer switch
    {
        "detail" => SchemaLayerType.Detail,
        "opening" => SchemaLayerType.Opening,
        "fixture" => SchemaLayerType.Fixture,
        "level" => SchemaLayerType.Level,
        _ => SchemaLayerType.Unit,
    };

    private static string? Validate(GisField field, IDictionary<string, Guid> parameterGuids)
    {
        field.Source ??= new GisFieldSource();
        field.Values ??= new List<string>();
        if (!Layers.Contains(field.Layer))
        {
            return $"layer '{field.Layer}' is not one of {string.Join(", ", Layers)}";
        }

        if (!FieldNamePattern.IsMatch(field.Name ?? string.Empty))
        {
            return $"{field.Layer}: field name '{field.Name}' must be lower case letters, digits and _, starting with a letter, at most 63 characters";
        }

        if (field.Length.HasValue && (field.Length.Value < 1 || field.Length.Value > 2000))
        {
            return $"{field.Key}: length {field.Length} is outside 1 to 2000";
        }

        switch (field.Source.Kind)
        {
            case GisFieldSourceKind.Parameter:
                return ValidateParameter(field, parameterGuids);
            case GisFieldSourceKind.Derived:
                return Enum.TryParse(field.Source.Derived, ignoreCase: true, out SchemaDerivedValue _)
                    ? null
                    : $"{field.Key}: derived value '{field.Source.Derived}' is not one of {string.Join(", ", Enum.GetNames(typeof(SchemaDerivedValue)))}";
            default:
                return null;
        }
    }

    private static string? ValidateParameter(GisField field, IDictionary<string, Guid> parameterGuids)
    {
        GisFieldSource source = field.Source;
        if (string.IsNullOrWhiteSpace(source.Parameter))
        {
            return $"{field.Key}: a parameter row needs 'parameter'";
        }

        if (GisSharedParameters.TryGet(source.Parameter!, out GisParameterDefinition standard))
        {
            if (source.Guid.HasValue && source.Guid.Value != standard.Guid)
            {
                return $"{field.Key}: {source.Parameter} is a standard parameter with GUID {standard.Guid:D}, not {source.Guid:D}";
            }

            return null;
        }

        if (!source.Guid.HasValue || source.Guid.Value == Guid.Empty)
        {
            return $"{field.Key}: parameter {source.Parameter} needs a fixed 'guid' so every PC binds the same definition";
        }

        if (GisSharedParameters.All.Any(definition => definition.Guid == source.Guid.Value))
        {
            return $"{field.Key}: GUID {source.Guid:D} belongs to a standard parameter";
        }

        if (parameterGuids.TryGetValue(source.Parameter!, out Guid known) && known != source.Guid.Value)
        {
            return $"{field.Key}: parameter {source.Parameter} has two GUIDs in this list";
        }

        if (parameterGuids.Any(entry => entry.Value == source.Guid.Value && entry.Key != source.Parameter))
        {
            return $"{field.Key}: GUID {source.Guid:D} is used by another parameter in this list";
        }

        if (source.Categories == null || source.Categories.Count == 0 || source.Categories.Any(category => !category.StartsWith("OST_", StringComparison.Ordinal)))
        {
            return $"{field.Key}: parameter {source.Parameter} needs 'categories', Revit category names such as OST_Floors";
        }

        if (field.Type == GisFieldType.Real)
        {
            return $"{field.Key}: a parameter field is text or integer";
        }

        parameterGuids[source.Parameter!] = source.Guid.Value;
        return null;
    }

    private static GisField Core(
        string layer,
        string name,
        string aliasJa,
        string description,
        GisFieldType type = GisFieldType.Text,
        IReadOnlyList<string>? values = null)
    {
        return new GisField
        {
            Layer = layer,
            Name = name,
            AliasJa = aliasJa,
            Type = type,
            Values = values?.ToList() ?? new List<string>(),
            Source = new GisFieldSource { Kind = GisFieldSourceKind.Core },
            Owner = GisFieldOwner.Revit,
            Description = description,
        };
    }
}

[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter), typeof(Newtonsoft.Json.Serialization.CamelCaseNamingStrategy))]
public enum GisFieldType
{
    Text,
    Integer,
    Real,
}

[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter), typeof(Newtonsoft.Json.Serialization.CamelCaseNamingStrategy))]
public enum GisFieldOwner
{
    /// <summary>Revit is the source: every export writes the value.</summary>
    Revit,

    /// <summary>The GDB is the source: the export never writes it, and Kanizu carries it across by revit_key.</summary>
    Gdb,
}

[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter), typeof(Newtonsoft.Json.Serialization.CamelCaseNamingStrategy))]
public enum GisFieldSourceKind
{
    /// <summary>Written by the export itself; listed so Kanizu knows its alias and pick list.</summary>
    Core,

    /// <summary>A Revit parameter, bound by RevitGeoSuite from this row.</summary>
    Parameter,

    /// <summary>A value the export works out (<see cref="SchemaDerivedValue"/>).</summary>
    Derived,

    /// <summary>No Revit source: a GDB-only field.</summary>
    None,
}

[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter), typeof(Newtonsoft.Json.Serialization.CamelCaseNamingStrategy))]
public enum GisFieldBinding
{
    Instance,
    Type,
}

public sealed class GisField
{
    [JsonProperty("layer")]
    public string Layer { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("aliasJa")]
    public string AliasJa { get; set; } = string.Empty;

    [JsonProperty("type")]
    public GisFieldType Type { get; set; } = GisFieldType.Text;

    /// <summary>Text length in the GDB. Null leaves it to Kanizu (255).</summary>
    [JsonProperty("length", NullValueHandling = NullValueHandling.Ignore)]
    public int? Length { get; set; }

    /// <summary>The allowed values: a pick list (coded-value domain) in the GDB. Empty means free.</summary>
    [JsonProperty("values")]
    public List<string> Values { get; set; } = new();

    [JsonProperty("source")]
    public GisFieldSource Source { get; set; } = new();

    [JsonProperty("owner")]
    public GisFieldOwner Owner { get; set; } = GisFieldOwner.Revit;

    [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
    public string? Description { get; set; }

    [JsonIgnore]
    public string Key => Layer + "." + Name;
}

public sealed class GisFieldSource
{
    [JsonProperty("kind")]
    public GisFieldSourceKind Kind { get; set; } = GisFieldSourceKind.None;

    /// <summary>For a parameter row: the Revit parameter name.</summary>
    [JsonProperty("parameter", NullValueHandling = NullValueHandling.Ignore)]
    public string? Parameter { get; set; }

    /// <summary>For a new parameter: its fixed shared parameter GUID.</summary>
    [JsonProperty("guid", NullValueHandling = NullValueHandling.Ignore)]
    public Guid? Guid { get; set; }

    [JsonProperty("binding", NullValueHandling = NullValueHandling.Ignore)]
    public GisFieldBinding? Binding { get; set; }

    /// <summary>For a new parameter: the Revit categories to bind it to, such as OST_Floors.</summary>
    [JsonProperty("categories", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? Categories { get; set; }

    /// <summary>For a derived row: the <see cref="SchemaDerivedValue"/> name.</summary>
    [JsonProperty("derived", NullValueHandling = NullValueHandling.Ignore)]
    public string? Derived { get; set; }
}
