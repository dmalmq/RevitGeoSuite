using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using RevitGeoSuite.FloorPlanExport.Core.Gis;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>How one layer's features changed between two sends.</summary>
public sealed class GisLayerChanges
{
    public GisLayerChanges(string layer, int added, int removed, int edited, int unchanged)
    {
        Layer = layer;
        Added = added;
        Removed = removed;
        Edited = edited;
        Unchanged = unchanged;
    }

    /// <summary>unit, detail, opening, fixture, level, ...</summary>
    public string Layer { get; }

    public int Added { get; }

    public int Removed { get; }

    public int Edited { get; }

    public int Unchanged { get; }

    public int Changed => Added + Removed + Edited;
}

/// <summary>
/// What changed in a model's GIS package since its previous version in the station folder, counted
/// by Revit element and by layer. Send to Kanizu reports it.
/// </summary>
/// <remarks>
/// A feature is matched by its <c>revit_key</c> and <c>level_id</c>, the same pair Kanizu matches
/// GDB rows by. A feature without a key is matched by its <c>id</c>, or else by its whole row. An
/// element is the model and UniqueId part of the key: it is added when none of its features were
/// in the previous version, removed when none are left, and edited when any feature was added,
/// removed or changed. A feature changed when its geometry or any attribute other than the row
/// number and <c>id</c> differs.
/// </remarks>
public sealed class GisPackageChanges
{
    private static readonly HashSet<string> IgnoredColumns = new(StringComparer.OrdinalIgnoreCase) { "id" };

    public GisPackageChanges(bool hasPrevious, IReadOnlyList<GisLayerChanges> layers, int elementsAdded, int elementsRemoved, int elementsEdited)
    {
        HasPrevious = hasPrevious;
        Layers = layers;
        ElementsAdded = elementsAdded;
        ElementsRemoved = elementsRemoved;
        ElementsEdited = elementsEdited;
    }

    /// <summary>False on the model's first send: every element counts as added.</summary>
    public bool HasPrevious { get; }

    /// <summary>Every layer of either version, by name.</summary>
    public IReadOnlyList<GisLayerChanges> Layers { get; }

    public int ElementsAdded { get; }

    public int ElementsRemoved { get; }

    public int ElementsEdited { get; }

    public int ElementsChanged => ElementsAdded + ElementsRemoved + ElementsEdited;

    /// <summary>
    /// Compares the GeoPackages of two <c>gis/</c> folders. <paramref name="previousGisFolder"/> is
    /// null or missing on a first send. Files are paired by name and layers by table name.
    /// </summary>
    public static GisPackageChanges Compare(string? previousGisFolder, string currentGisFolder)
    {
        bool hasPrevious = !string.IsNullOrEmpty(previousGisFolder) && Directory.Exists(previousGisFolder);
        Dictionary<string, Dictionary<string, Row>> before = hasPrevious ? ReadFolder(previousGisFolder!) : new(StringComparer.Ordinal);
        Dictionary<string, Dictionary<string, Row>> after = ReadFolder(currentGisFolder);

        var layers = new List<GisLayerChanges>();
        var elementStates = new Dictionary<string, ElementState>(StringComparer.Ordinal);
        foreach (string layer in before.Keys.Union(after.Keys, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal))
        {
            Dictionary<string, Row> old = before.TryGetValue(layer, out Dictionary<string, Row>? o) ? o : new(StringComparer.Ordinal);
            Dictionary<string, Row> now = after.TryGetValue(layer, out Dictionary<string, Row>? n) ? n : new(StringComparer.Ordinal);
            int added = 0, removed = 0, edited = 0, unchanged = 0;
            foreach (KeyValuePair<string, Row> entry in now)
            {
                ElementState state = StateOf(elementStates, entry.Value.Element);
                state.After = true;
                if (!old.TryGetValue(entry.Key, out Row? previous))
                {
                    added++;
                    state.Changed = true;
                }
                else if (!string.Equals(previous.Hash, entry.Value.Hash, StringComparison.Ordinal))
                {
                    edited++;
                    state.Changed = true;
                }
                else
                {
                    unchanged++;
                }
            }

            foreach (KeyValuePair<string, Row> entry in old)
            {
                ElementState state = StateOf(elementStates, entry.Value.Element);
                state.Before = true;
                if (!now.ContainsKey(entry.Key))
                {
                    removed++;
                    state.Changed = true;
                }
            }

            layers.Add(new GisLayerChanges(layer, added, removed, edited, unchanged));
        }

        int elementsAdded = elementStates.Values.Count(state => state.After && !state.Before);
        int elementsRemoved = elementStates.Values.Count(state => state.Before && !state.After);
        int elementsEdited = elementStates.Values.Count(state => state.Before && state.After && state.Changed);
        return new GisPackageChanges(hasPrevious, layers, elementsAdded, elementsRemoved, elementsEdited);
    }

    /// <summary>The number of features per <c>level_id</c> in a <c>gis/</c> folder's GeoPackages, every layer counted.</summary>
    public static IReadOnlyDictionary<string, int> FeaturesPerLevel(string gisFolder)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Dictionary<string, Row> layer in ReadFolder(gisFolder).Values)
        {
            foreach (Row row in layer.Values.Where(row => row.LevelId.Length > 0))
            {
                counts[row.LevelId] = counts.TryGetValue(row.LevelId, out int count) ? count + 1 : 1;
            }
        }

        return counts;
    }

    private static ElementState StateOf(Dictionary<string, ElementState> states, string element)
    {
        if (!states.TryGetValue(element, out ElementState? state))
        {
            state = new ElementState();
            states[element] = state;
        }

        return state;
    }

    /// <summary>Every feature of every GeoPackage in the folder, by layer and then by match key.</summary>
    private static Dictionary<string, Dictionary<string, Row>> ReadFolder(string gisFolder)
    {
        var layers = new Dictionary<string, Dictionary<string, Row>>(StringComparer.Ordinal);
        if (!Directory.Exists(gisFolder))
        {
            return layers;
        }

        foreach (string path in Directory.GetFiles(gisFolder, "*.gpkg").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string file = Path.GetFileName(path);
            using SqliteConnection connection = Open(path);
            foreach ((string table, string geometryColumn, _) in GeoPackageGeometryReader.ReadFeatureTables(connection).ToList())
            {
                if (!layers.TryGetValue(table, out Dictionary<string, Row>? rows))
                {
                    rows = new Dictionary<string, Row>(StringComparer.Ordinal);
                    layers[table] = rows;
                }

                foreach (Row row in ReadRows(connection, file, table, geometryColumn))
                {
                    // Keys are unique within a layer of one floor; a repeat would be an export bug, and
                    // the later row is then compared as its own feature rather than lost.
                    string key = row.Key;
                    for (int repeat = 2; rows.ContainsKey(key); repeat++)
                    {
                        key = row.Key + "#" + repeat.ToString(CultureInfo.InvariantCulture);
                    }

                    rows[key] = row;
                }
            }
        }

        return layers;
    }

    private static IEnumerable<Row> ReadRows(SqliteConnection connection, string file, string table, string geometryColumn)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {GeoPackageGeometryReader.QuoteIdentifier(table)};";
        using SqliteDataReader reader = command.ExecuteReader();
        var compared = new List<int>();
        int keyColumn = -1, levelColumn = -1, idColumn = -1;
        for (int i = 0; i < reader.FieldCount; i++)
        {
            string name = reader.GetName(i);
            if (string.Equals(name, RevitKey.AttributeName, StringComparison.OrdinalIgnoreCase))
            {
                keyColumn = i;
            }
            else if (string.Equals(name, GisSharedParameters.LevelId, StringComparison.OrdinalIgnoreCase))
            {
                levelColumn = i;
            }
            else if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
            {
                idColumn = i;
            }

            if (!IgnoredColumns.Contains(name) && !IsRowNumber(reader, i, geometryColumn))
            {
                compared.Add(i);
            }
        }

        compared.Sort((a, b) => string.CompareOrdinal(reader.GetName(a), reader.GetName(b)));
        while (reader.Read())
        {
            string hash = Hash(reader, compared);
            string revitKey = Text(reader, keyColumn);
            string levelId = Text(reader, levelColumn);
            string id = Text(reader, idColumn);
            string key;
            string element;
            if (revitKey.Length > 0)
            {
                key = "k|" + revitKey + "|" + levelId;
                int part = revitKey.LastIndexOf(RevitKey.Separator);
                element = part > 0 ? revitKey.Substring(0, part) : revitKey;
            }
            else if (id.Length > 0)
            {
                key = "i|" + id;
                element = table + "|" + id;
            }
            else
            {
                key = "h|" + file + "|" + hash;
                element = table + "|" + key;
            }

            yield return new Row(key, element, levelId, hash);
        }
    }

    /// <summary>The integer primary key, which SQLite numbers per file and so says nothing about the feature.</summary>
    private static bool IsRowNumber(SqliteDataReader reader, int ordinal, string geometryColumn)
    {
        string name = reader.GetName(ordinal);
        return !string.Equals(name, geometryColumn, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(name, "fid", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "ogc_fid", StringComparison.OrdinalIgnoreCase));
    }

    private static string Text(SqliteDataReader reader, int ordinal)
    {
        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return string.Empty;
        }

        return Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    private static string Hash(SqliteDataReader reader, List<int> columns)
    {
        using var sha = SHA256.Create();
        using var stream = new MemoryStream();
        foreach (int ordinal in columns)
        {
            byte[] name = Encoding.UTF8.GetBytes(reader.GetName(ordinal) + "=");
            stream.Write(name, 0, name.Length);
            byte[] value = reader.IsDBNull(ordinal)
                ? new byte[] { 0 }
                : reader.GetValue(ordinal) is byte[] blob
                    ? blob
                    : Encoding.UTF8.GetBytes("v" + Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture));
            stream.Write(value, 0, value.Length);
            stream.WriteByte(0x1f);
        }

        return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ConnectionString);
        connection.Open();
        return connection;
    }

    private sealed class Row
    {
        public Row(string key, string element, string levelId, string hash)
        {
            Key = key;
            Element = element;
            LevelId = levelId;
            Hash = hash;
        }

        public string Key { get; }

        public string Element { get; }

        public string LevelId { get; }

        public string Hash { get; }
    }

    private sealed class ElementState
    {
        public bool Before;
        public bool After;
        public bool Changed;
    }
}
