using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>One Revit-owned field someone changed in the GDB, as Kanizu records it.</summary>
public sealed class GisDriftRow
{
    public GisDriftRow(
        string revitKey,
        string uniqueId,
        string layer,
        string field,
        string parameter,
        string guid,
        string binding,
        string label,
        string revit,
        string gdb,
        string @base,
        bool conflict,
        string gdbCategory = "",
        string gdbRestriction = "")
    {
        RevitKey = revitKey;
        UniqueId = uniqueId;
        Layer = layer;
        Field = field;
        Parameter = parameter;
        Guid = guid;
        Binding = binding;
        Label = label;
        Revit = revit;
        Gdb = gdb;
        Base = @base;
        Conflict = conflict;
        GdbCategory = gdbCategory ?? string.Empty;
        GdbRestriction = gdbRestriction ?? string.Empty;
    }

    public string RevitKey { get; }

    public string UniqueId { get; }

    /// <summary>unit, fixture, level, ...</summary>
    public string Layer { get; }

    public string Field { get; }

    /// <summary>The station field's own parameter, empty for a core field.</summary>
    public string Parameter { get; }

    public string Guid { get; }

    /// <summary>"instance" or "type" for a station field's parameter.</summary>
    public string Binding { get; }

    public string Label { get; }

    /// <summary>The value the export wrote.</summary>
    public string Revit { get; }

    /// <summary>The value the GDB holds now.</summary>
    public string Gdb { get; }

    /// <summary>The value at the last apply.</summary>
    public string Base { get; }

    /// <summary>True when Revit changed the field too since the last apply.</summary>
    public bool Conflict { get; }

    /// <summary>
    /// For a unit's category: the GDB's station code (walkway_sta, store) as the Revit category it
    /// comes from. Kanizu works it out from its station code table, so this tool keeps no copy.
    /// </summary>
    public string GdbCategory { get; }

    /// <summary>With <see cref="GdbCategory"/>: the floor type restriction the code needs (rachi_nai, rachi_gai), or empty.</summary>
    public string GdbRestriction { get; }

    public string Describe() => $"{Layer} {(Label.Length > 0 ? Label : UniqueId)} {Field}";
}

public sealed class GisDriftFile
{
    public GisDriftFile(
        string model,
        string appliedVersion,
        IReadOnlyList<GisDriftRow> rows,
        int epsg = 0,
        IReadOnlyList<GisDriftShape>? shapes = null)
    {
        Model = model;
        AppliedVersion = appliedVersion;
        Rows = rows;
        Epsg = epsg;
        Shapes = shapes ?? Array.Empty<GisDriftShape>();
    }

    public string Model { get; }

    public string AppliedVersion { get; }

    public IReadOnlyList<GisDriftRow> Rows { get; }

    /// <summary>The CRS of every <see cref="GisDriftShape.Outline"/>: the export's, so the model's project CRS. 0 when Kanizu knew none, and then no shape has an outline.</summary>
    public int Epsg { get; }

    /// <summary>The units whose shape the GDB changed, with the GDB's outline.</summary>
    public IReadOnlyList<GisDriftShape> Shapes { get; }
}

/// <summary>
/// One unit whose shape someone changed in the GDB, as Kanizu records it. The rows of one split or
/// merge share a <see cref="Group"/>, the key of the element that keeps its row; in a split, every
/// piece still carries that key until Revit makes the new floors and the next apply writes theirs.
/// </summary>
public sealed class GisDriftShape
{
    public GisDriftShape(
        string edit,
        string revitKey,
        string uniqueId,
        string layer,
        string label,
        string levelId,
        string group,
        bool keeps,
        IReadOnlyList<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> outline)
    {
        Edit = edit;
        RevitKey = revitKey;
        UniqueId = uniqueId;
        Layer = layer;
        Label = label;
        LevelId = levelId;
        Group = group;
        Keeps = keeps;
        Outline = outline;
    }

    /// <summary>One of <see cref="GisDrift.Reshaped"/>, <see cref="GisDrift.Deleted"/>, <see cref="GisDrift.Moved"/>, <see cref="GisDrift.HandDrawn"/>, <see cref="GisDrift.Split"/> or <see cref="GisDrift.Merged"/>.</summary>
    public string Edit { get; }

    /// <summary>Empty for a unit drawn by hand in the GDB.</summary>
    public string RevitKey { get; }

    public string UniqueId { get; }

    public string Layer { get; }

    public string Label { get; }

    /// <summary>The level of the GDB floor the outline is on; empty when no level of the model is paired with it.</summary>
    public string LevelId { get; }

    /// <summary>For a split or merge: the key of the element that keeps its row. Empty otherwise.</summary>
    public string Group { get; }

    /// <summary>For a split or merge: this row keeps the element (and its key). The other pieces of a split become new floors; the other units of a merge are deleted.</summary>
    public bool Keeps { get; }

    /// <summary>The GDB outline in <see cref="GisDriftFile.Epsg"/>: polygons, each a list of rings (the outer one first), each point X easting and Y northing whatever axis order the EPSG code names. Empty for a deleted unit and for the units a merge took in.</summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> Outline { get; }

    public string Describe() => $"{Layer} {(Label.Length > 0 ? Label : UniqueId)} {Edit}";
}

/// <summary>One parameter to set on one element: the GDB's value, accepted into Revit.</summary>
public sealed class GisDriftChange
{
    public GisDriftChange(GisDriftRow row, string parameter, string binding, string oldValue, string newValue)
    {
        Row = row;
        Parameter = parameter;
        Binding = binding;
        OldValue = oldValue;
        NewValue = newValue;
    }

    public GisDriftRow Row { get; }

    public string UniqueId => Row.UniqueId;

    public string Parameter { get; }

    /// <summary><see cref="GisDrift.Instance"/> or <see cref="GisDrift.Type"/>.</summary>
    public string Binding { get; }

    public string OldValue { get; }

    public string NewValue { get; }

    public bool Conflict => Row.Conflict;

    public string Label()
    {
        string text = $"{Row.Describe()}: {Parameter} {(OldValue.Length > 0 ? OldValue : "(empty)")} to {(NewValue.Length > 0 ? NewValue : "(empty)")}";
        if (Binding == GisDrift.Type)
        {
            text += " (type parameter: every element of the type changes)";
        }

        if (Conflict)
        {
            text += " (also changed in Revit since the last apply)";
        }

        return text;
    }
}

/// <summary>
/// Review GDB edits: the values the station's GDB holds that this model's export does not. Kanizu
/// writes <c>gis-drift/&lt;model&gt;.gis-drift.json</c> into the station folder every time it applies
/// this model's GIS export to the GDB; each row is a Revit-owned field someone changed in the GDB.
/// Accepting a row writes the GDB's value into the Revit parameter the field comes from, so the
/// next export agrees and the GDB edit survives it. Rows the model already agrees with are left
/// out, so reading the file twice changes nothing the second time. The file also lists the units
/// whose shape the GDB changed (<see cref="GisDriftFile.Shapes"/>); <see cref="GisShapeEdits"/> plans
/// the ones the page applies. The format is described in docs/kanizu-link-contract.md.
/// </summary>
public static class GisDrift
{
    public const string Schema = "kanizu.gis-drift";
    public const int Version = 1;
    public const string Folder = "gis-drift";
    public const string Instance = "instance";
    public const string Type = "type";

    /// <summary>Shape edits (<see cref="GisDriftShape.Edit"/>): the GDB changed the outline.</summary>
    public const string Reshaped = "reshaped";

    /// <summary>The GDB deleted the unit.</summary>
    public const string Deleted = "deleted";

    /// <summary>The GDB moved the unit to another floor.</summary>
    public const string Moved = "moved";

    /// <summary>A unit drawn in the GDB that no element has.</summary>
    public const string HandDrawn = "handDrawn";

    /// <summary>The GDB cut the unit into several, one row per piece.</summary>
    public const string Split = "split";

    /// <summary>The GDB merged units into one: the one that keeps its row, then the ones it took in.</summary>
    public const string Merged = "merged";

    /// <summary>The floor type parameter that says which side of the ticket gates a unit is on.</summary>
    public const string RestrictionParameter = "gis_restriction";

    /// <summary>
    /// The Revit parameter behind each core field, for rows whose field has no parameter of its own
    /// in the field list. Fields Revit works out (geometry, ids, the level, the detail, opening and
    /// fixture categories, the level's name) are not here: a GDB edit to them can only be made again
    /// in Revit by hand. Kanizu reads a fixture's type as the GDB's category and a level's level_name
    /// as its name, so rows name those fields as the GDB does. A fixture's category only says it is a
    /// fixture (a ticket gate), so writing a GDB category into it would make the gate a unit. Nor is
    /// unit restrict: the GDB holds IMDF restrictions there (employeesonly), while the floor type's
    /// gis_restriction says which side of the ticket gates it is on and picks the station category
    /// code, so writing one into the other would change the unit's category.
    /// </summary>
    public static IReadOnlyDictionary<(string Layer, string Field), (string Parameter, string Binding)> CoreParameters { get; } =
        new Dictionary<(string, string), (string, string)>
        {
            [("unit", "name")] = ("name", Instance),
            [("unit", "alt_name")] = ("short_name", Instance),
            [("unit", "category")] = ("category", Instance),
            [("unit", "direction")] = ("gis_direction", Instance),
            [("fixture", "name")] = ("name", Instance),
            [("fixture", "alt_name")] = ("short_name", Instance),
            [("level", "short_name")] = ("short_name", Instance),
            [("level", "ordinal")] = ("ordinal", Instance),
        };

    /// <summary>The drift file's name for a model id, as Kanizu writes it.</summary>
    public static string FileName(string model)
    {
        StringBuilder safe = new();
        foreach (char character in model ?? string.Empty)
        {
            bool asciiLetterOrDigit = character < 128 && char.IsLetterOrDigit(character);
            safe.Append(asciiLetterOrDigit || character == '-' ? character : '_');
        }

        return safe + ".gis-drift.json";
    }

    /// <summary>The model key Kanizu files the drift under: the model id in lower case.</summary>
    public static string ModelKey(string? modelId) => GisToolStandard.Normalize(modelId).ToLowerInvariant();

    /// <summary>The drift file from its JSON text. Throws <see cref="FormatException"/> for another schema or version.</summary>
    public static GisDriftFile Parse(string text)
    {
        JToken data;
        try
        {
            // Dates stay text: a label or version that looks like a date must read back as written.
            using JsonTextReader reader = new(new StringReader(text ?? string.Empty)) { DateParseHandling = DateParseHandling.None };
            data = JToken.ReadFrom(reader);
        }
        catch (JsonException error)
        {
            throw new FormatException($"The drift file is not JSON: {error.Message}", error);
        }

        if (data is not JObject root || (string?)(root["schema"] as JValue) != Schema)
        {
            throw new FormatException($"The file is not a Kanizu drift file (schema {Schema}).");
        }

        JToken? version = root["version"];
        // Kanizu writes the version as an integer (a u32); a number equal to it, such as 1.0, is the same version.
        if (version is not JValue { Type: JTokenType.Integer or JTokenType.Float } number || Convert.ToDouble(number.Value, CultureInfo.InvariantCulture) != Version)
        {
            throw new FormatException($"The drift file is version {version}; this tool reads version {Version}. Update RevitGeoSuite.");
        }

        List<GisDriftRow> rows = new();
        if (root["rows"] is JArray entries)
        {
            foreach (JObject entry in entries.OfType<JObject>())
            {
                string revitKey = Text(entry["revitKey"]);
                string uniqueId = Text(entry["uniqueId"]);
                rows.Add(new GisDriftRow(
                    revitKey,
                    uniqueId.Length > 0 ? uniqueId : UniqueIdOf(revitKey),
                    Text(entry["layer"]),
                    Text(entry["field"]),
                    Text(entry["parameter"]),
                    Text(entry["guid"]),
                    Text(entry["binding"]),
                    Text(entry["label"]),
                    Text(entry["revit"]),
                    Text(entry["gdb"]),
                    Text(entry["base"]),
                    Truthy(entry["conflict"]),
                    Text(entry["gdbCategory"]),
                    Text(entry["gdbRestriction"])));
            }
        }

        List<GisDriftShape> shapes = new();
        if (root["shapes"] is JArray shapeEntries)
        {
            foreach (JObject entry in shapeEntries.OfType<JObject>())
            {
                string revitKey = Text(entry["revitKey"]);
                string uniqueId = Text(entry["uniqueId"]);
                shapes.Add(new GisDriftShape(
                    Text(entry["edit"]),
                    revitKey,
                    uniqueId.Length > 0 ? uniqueId : UniqueIdOf(revitKey),
                    Text(entry["layer"]),
                    Text(entry["label"]),
                    Text(entry["levelId"]),
                    Text(entry["group"]),
                    Truthy(entry["keeps"]),
                    Polygons(entry["outline"])));
            }
        }

        int epsg = root["epsg"] is JValue { Type: JTokenType.Integer } code ? Convert.ToInt32(code.Value, CultureInfo.InvariantCulture) : 0;
        return new GisDriftFile(Text(root["model"]), Text(root["appliedVersion"]), rows, epsg, shapes);
    }

    /// <summary>Reads a drift file, with or without a byte order mark.</summary>
    public static GisDriftFile Read(string path)
    {
        using StreamReader reader = new(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>The Revit parameter and binding a row writes, or null when no parameter holds the field.</summary>
    public static (string Parameter, string Binding)? Target(GisDriftRow row)
    {
        if (row.Parameter.Length > 0)
        {
            return (row.Parameter, row.Binding == Type ? Type : Instance);
        }

        return CoreParameters.TryGetValue((row.Layer, row.Field), out (string Parameter, string Binding) target) ? target : null;
    }

    /// <summary>
    /// The changes accepting the drift would make, and the rows that cannot be written.
    /// <paramref name="current"/>(uniqueId, parameter, binding) returns the parameter's text in the
    /// model, or null when the element or the parameter is missing. A row the model already agrees
    /// with is left out. Conflicts come with the rest; the page leaves them unticked.
    /// </summary>
    public static (IReadOnlyList<GisDriftChange> Changes, IReadOnlyList<string> Problems) Plan(
        GisDriftFile driftFile,
        Func<string, string, string, string?> current,
        GisToolStandard standard)
    {
        List<GisDriftChange> changes = new();
        List<string> problems = new();
        HashSet<(string, string, string)> seen = new();
        foreach (GisDriftRow row in driftFile.Rows)
        {
            (string Parameter, string Binding)? target = Target(row);
            if (target is null)
            {
                problems.Add($"{row.Describe()}: no Revit parameter holds this field; change it in Revit by hand (GDB: {(row.Gdb.Length > 0 ? row.Gdb : "(empty)")}).");
                continue;
            }

            (string parameter, string binding) = target.Value;
            string gdb = row.Gdb;
            if (row.GdbCategory.Length > 0)
            {
                gdb = row.GdbCategory;
                if (row.GdbRestriction.Length > 0)
                {
                    // Spelled as the export spells it, so RACHI_GAI agrees with rachi_gai.
                    string restriction = ImdfRestrictionNormalizer.FloorTypeRestriction(current(row.UniqueId, RestrictionParameter, Type)) ?? string.Empty;
                    if (restriction != row.GdbRestriction)
                    {
                        problems.Add(
                            $"{row.Describe()}: the GDB's {row.Gdb} needs a floor type with {RestrictionParameter} {row.GdbRestriction} " +
                            $"(this one has {(restriction.Length > 0 ? restriction : "none")}); change the floor type in Revit by hand.");
                        continue;
                    }
                }
            }

            (string? value, string? problem) = ParameterTable.CheckValue(parameter, gdb, standard);
            if (problem != null)
            {
                problems.Add($"{row.Describe()}: {problem}");
                continue;
            }

            string? old = current(row.UniqueId, parameter, binding);
            if (old is null)
            {
                problems.Add($"{row.Describe()}: the element or its {parameter} parameter is not in this model.");
                continue;
            }

            if (Same(parameter, old, value!))
            {
                continue;
            }

            if (!seen.Add((row.UniqueId, parameter, binding)))
            {
                continue;
            }

            changes.Add(new GisDriftChange(row, parameter, binding, old, value!));
        }

        return (changes, problems);
    }

    /// <summary>
    /// The GDB edits that wait for this model: the value changes still planned, the rows that
    /// must be changed by hand (<see cref="Plan"/> lists each row at most once, as a change or a
    /// problem), and the shapes the model does not match yet. Zero once the model agrees with the file.
    /// </summary>
    public static int Waiting(IReadOnlyList<GisDriftChange> changes, IReadOnlyList<string> problems, ShapePlan shapes) =>
        changes.Count + problems.Count + shapes.Waiting;

    /// <summary>
    /// The planned changes the user ticked, matched by element, parameter, binding and value. A
    /// ticked change the plan no longer holds (the model or the drift file changed since the
    /// preview) is listed, not written.
    /// </summary>
    public static (IReadOnlyList<GisDriftChange> Changes, IReadOnlyList<string> Skipped) Accept(
        IReadOnlyList<GisDriftChange> planned,
        IEnumerable<(string UniqueId, string Parameter, string Binding, string NewValue, string Label)> accepted)
    {
        Dictionary<(string, string, string, string), GisDriftChange> byKey = new();
        foreach (GisDriftChange change in planned)
        {
            byKey[(change.UniqueId, change.Parameter, change.Binding, change.NewValue)] = change;
        }

        List<GisDriftChange> changes = new();
        List<string> skipped = new();
        HashSet<(string, string, string, string)> seen = new();
        foreach ((string uniqueId, string parameter, string binding, string newValue, string label) in accepted)
        {
            (string, string, string, string) key = (uniqueId, parameter, binding, newValue);
            if (!seen.Add(key))
            {
                continue;
            }

            if (byKey.TryGetValue(key, out GisDriftChange? change))
            {
                changes.Add(change);
            }
            else
            {
                skipped.Add($"Skipped {label}: the model or the drift file changed since the preview.");
            }
        }

        return (changes, skipped);
    }

    private static bool Same(string parameter, string old, string value)
    {
        if (ParameterTable.IntegerColumns.Contains(parameter))
        {
            return int.TryParse(old.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int left) &&
                   int.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int right) &&
                   left == right;
        }

        return GisToolStandard.Normalize(old) == GisToolStandard.Normalize(value);
    }

    /// <summary>A GeoJSON Polygon or MultiPolygon as polygons of rings; anything else as none.</summary>
    private static IReadOnlyList<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> Polygons(JToken? geometry)
    {
        static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Rings(JToken? rings) =>
            (rings as JArray)?.OfType<JArray>()
                .Select(ring => (IReadOnlyList<(double X, double Y)>)ring.OfType<JArray>()
                    .Where(point => point.Count >= 2)
                    .Select(point => ((double)point[0], (double)point[1]))
                    .ToList())
                .ToList()
            ?? new List<IReadOnlyList<(double X, double Y)>>();

        if (geometry is not JObject shape)
        {
            return Array.Empty<IReadOnlyList<IReadOnlyList<(double X, double Y)>>>();
        }

        return Text(shape["type"]) switch
        {
            "Polygon" => new[] { Rings(shape["coordinates"]) },
            "MultiPolygon" => (shape["coordinates"] as JArray)?.Select(Rings).ToList()
                ?? new List<IReadOnlyList<IReadOnlyList<(double X, double Y)>>>(),
            _ => Array.Empty<IReadOnlyList<IReadOnlyList<(double X, double Y)>>>(),
        };
    }

    private static string UniqueIdOf(string revitKey)
    {
        string[] parts = revitKey.Split('|');
        return parts.Length >= 3 ? parts[1] : string.Empty;
    }

    private static string Text(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null)
        {
            return string.Empty;
        }

        return token.Type switch
        {
            JTokenType.String => (string)token!,
            JTokenType.Integer or JTokenType.Float or JTokenType.Boolean => Convert.ToString(((JValue)token).Value, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => token.ToString(Formatting.None),
        };
    }

    private static bool Truthy(JToken? token) => token?.Type switch
    {
        JTokenType.Boolean => (bool)token,
        JTokenType.Integer => (long)token != 0,
        JTokenType.String => ((string)token!).Length > 0,
        _ => false,
    };
}
