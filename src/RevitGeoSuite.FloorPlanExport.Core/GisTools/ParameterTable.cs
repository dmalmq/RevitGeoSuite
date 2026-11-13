using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>One cell the edited table changes.</summary>
public sealed class ParameterTableChange
{
    public ParameterTableChange(string elementId, string kind, string column, string oldValue, string newValue)
    {
        ElementId = elementId;
        Kind = kind;
        Column = column;
        OldValue = oldValue;
        NewValue = newValue;
    }

    public string ElementId { get; }

    /// <summary>floor, room, stair, family or level.</summary>
    public string Kind { get; }

    public string Column { get; }

    public string OldValue { get; }

    public string NewValue { get; }

    public string Label() =>
        $"{Kind} {ElementId}: {Column} {(OldValue.Length > 0 ? OldValue : "(empty)")} to {(NewValue.Length > 0 ? NewValue : "(empty)")}";
}

/// <summary>
/// Parameters: the GIS parameters of a model as one table, written to CSV and read back. A person
/// fills names, short names and categories for many elements in Excel, then reads the file back.
/// Only the cells that changed are written, only in the columns that kind of element owns, and only
/// after the person has seen the list. Element ids tie a row to its element, so rows can be sorted,
/// filtered or deleted in Excel freely.
/// </summary>
public static class ParameterTable
{
    public static IReadOnlyList<string> Columns { get; } = new[]
    {
        "element_id", "kind", "level", "family_type", "gis_key",
        "name", "short_name", "category", "gis_direction", "ordinal",
    };

    /// <summary>The columns a person may change, by kind of element. The rest are there to read.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Editable { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        ["floor"] = new[] { "name", "short_name", "category" },
        ["room"] = new[] { "name", "short_name", "category" },
        ["stair"] = new[] { "name", "short_name", "category", "gis_direction" },
        ["family"] = new[] { "name", "short_name", "category", "gis_direction" },
        ["level"] = new[] { "short_name", "ordinal" },
    };

    public static IReadOnlyList<string> Directions { get; } = new[] { "up", "down", "both" };

    public static IReadOnlyList<string> IntegerColumns { get; } = new[] { "ordinal" };

    /// <summary>Columns that describe the element. Edits to them are ignored, so a sorted or annotated sheet still reads back.</summary>
    public static IReadOnlyList<string> ReadOnlyColumns { get; } = new[] { "element_id", "kind", "level", "family_type", "gis_key" };

    private static readonly Dictionary<string, string> DirectionWords = new()
    {
        ["上り"] = "up",
        ["下り"] = "down",
        ["両方向"] = "both",
    };

    /// <summary>One element's row. <paramref name="values"/> holds the parameter columns that apply to it.</summary>
    public static Dictionary<string, string> Row(
        long elementId,
        string kind,
        string level = "",
        string familyType = "",
        string gisKey = "",
        IEnumerable<KeyValuePair<string, string>>? values = null)
    {
        Dictionary<string, string> entry = Columns.ToDictionary(column => column, _ => string.Empty, StringComparer.Ordinal);
        entry["element_id"] = elementId.ToString(CultureInfo.InvariantCulture);
        entry["kind"] = kind;
        entry["level"] = level ?? string.Empty;
        entry["family_type"] = familyType ?? string.Empty;
        entry["gis_key"] = gisKey ?? string.Empty;
        foreach (KeyValuePair<string, string> value in values ?? Array.Empty<KeyValuePair<string, string>>())
        {
            if (!Columns.Contains(value.Key))
            {
                throw new ArgumentException($"Unknown column {value.Key}", nameof(values));
            }

            entry[value.Key] = value.Value ?? string.Empty;
        }

        return entry;
    }

    public static string ToCsvText(IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        StringBuilder text = new();
        text.Append(string.Join(",", Columns)).Append("\r\n");
        foreach (IReadOnlyDictionary<string, string> entry in rows)
        {
            text.Append(string.Join(",", Columns.Select(column => Quote(entry.TryGetValue(column, out string? value) ? value : string.Empty))));
            text.Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// Rows keyed by the header. Accepts what Excel writes: quoted cells, CRLF, commas and line
    /// breaks inside quotes. Blank lines are skipped. Throws <see cref="FormatException"/> for a file
    /// without an element_id column.
    /// </summary>
    public static IReadOnlyList<Dictionary<string, string>> ParseCsvText(string text)
    {
        List<List<string>> records = ParseRecords(text ?? string.Empty)
            .Where(record => record.Any(cell => cell.Trim().Length > 0))
            .ToList();
        if (records.Count == 0)
        {
            return Array.Empty<Dictionary<string, string>>();
        }

        List<string> header = records[0].Select(cell => cell.Trim().TrimStart('﻿')).ToList();
        if (!header.Contains("element_id"))
        {
            throw new FormatException("The file has no element_id column.");
        }

        List<Dictionary<string, string>> rows = new();
        foreach (List<string> record in records.Skip(1))
        {
            Dictionary<string, string> entry = new(StringComparer.Ordinal);
            for (int index = 0; index < header.Count; index++)
            {
                entry[header[index]] = index < record.Count ? record[index] : string.Empty;
            }

            rows.Add(entry);
        }

        return rows;
    }

    /// <summary>The CSV bytes: UTF-8 with a byte order mark, so Excel opens Japanese text correctly.</summary>
    public static byte[] ToCsvBytes(IEnumerable<IReadOnlyDictionary<string, string>> rows) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(new UTF8Encoding(false).GetBytes(ToCsvText(rows)))
            .ToArray();

    public static void WriteCsv(string path, IEnumerable<IReadOnlyDictionary<string, string>> rows) =>
        File.WriteAllBytes(path, ToCsvBytes(rows));

    /// <summary>
    /// The text of a CSV file. A UTF-8 byte order mark is skipped; the bytes are then decoded as
    /// strict UTF-8 (with or without the mark), and only when that fails as strict CP932, which is
    /// what Japanese Excel saves as "CSV". Japanese text in CP932 is almost never valid UTF-8, so
    /// the order does not misread it. Bytes valid in neither throw <see cref="FormatException"/>
    /// rather than reading back as replacement characters.
    /// </summary>
    public static string DecodeCsv(byte[] data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        int start = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data, start, data.Length - start);
        }
        catch (DecoderFallbackException)
        {
        }

        // Not every runtime honours the exception fallback for CP932 (Mono swaps in '?'), so the text
        // must also encode back to the same bytes. Excel writes the canonical CP932 bytes, so a file
        // it saved always round-trips.
        Encoding cp932 = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        try
        {
            string text = cp932.GetString(data);
            if (cp932.GetBytes(text).SequenceEqual(data))
            {
                return text;
            }
        }
        catch (DecoderFallbackException)
        {
        }
        catch (EncoderFallbackException)
        {
        }

        throw new FormatException("The file is neither UTF-8 nor CP932 (Shift_JIS) text. Save it from Excel as CSV UTF-8 and read it again.");
    }

    public static IReadOnlyList<Dictionary<string, string>> ReadCsv(string path) => ParseCsvText(DecodeCsv(File.ReadAllBytes(path)));

    /// <summary>
    /// The changes the edited rows make, and one line for each edit that is not written. A row whose
    /// element_id is not in the model is skipped whole. A change in a column its kind does not own,
    /// or a value the standard does not accept, skips only that cell; the row's other cells still
    /// apply. An edit to a column that only describes the element is ignored and listed.
    /// </summary>
    public static (IReadOnlyList<ParameterTableChange> Changes, IReadOnlyList<string> Problems) Diff(
        IEnumerable<IReadOnlyDictionary<string, string>> currentRows,
        IEnumerable<IReadOnlyDictionary<string, string>> editedRows,
        GisToolStandard standard)
    {
        Dictionary<string, IReadOnlyDictionary<string, string>> current = new(StringComparer.Ordinal);
        foreach (IReadOnlyDictionary<string, string> entry in currentRows)
        {
            current[entry["element_id"]] = entry;
        }

        List<ParameterTableChange> changes = new();
        List<string> problems = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        int number = 1;
        foreach (IReadOnlyDictionary<string, string> edited in editedRows)
        {
            number++;
            string elementId = GisToolStandard.Normalize(edited.TryGetValue("element_id", out string? id) ? id : null);
            if (elementId.Length == 0)
            {
                continue;
            }

            if (!seen.Add(elementId))
            {
                problems.Add($"Line {number}: element {elementId} appears twice; only the first row is read.");
                continue;
            }

            if (!current.TryGetValue(elementId, out IReadOnlyDictionary<string, string>? entry))
            {
                problems.Add($"Line {number}: element {elementId} is not in the model; the row is skipped.");
                continue;
            }

            string kind = entry["kind"];
            foreach (string column in Columns)
            {
                if (!edited.TryGetValue(column, out string? editedValue))
                {
                    continue;
                }

                string old = entry.TryGetValue(column, out string? oldValue) ? oldValue : string.Empty;
                string value = GisToolStandard.Normalize(editedValue);
                if (value == old)
                {
                    continue;
                }

                if (ReadOnlyColumns.Contains(column))
                {
                    problems.Add($"Line {number}: {column} only describes the element; the edit is ignored.");
                    continue;
                }

                if (!Editable.TryGetValue(kind, out IReadOnlyList<string>? owned) || !owned.Contains(column))
                {
                    problems.Add($"Line {number}: {column} cannot be set on a {kind} here; only this cell is skipped.");
                    continue;
                }

                (string? accepted, string? problem) = CheckValue(column, value, standard);
                if (problem != null)
                {
                    problems.Add($"Line {number}: {problem} Only this cell is skipped.");
                    continue;
                }

                if (accepted != old)
                {
                    changes.Add(new ParameterTableChange(elementId, kind, column, old, accepted!));
                }
            }
        }

        return (changes, problems);
    }

    /// <summary>
    /// The accepted changes that still apply to the model as it is now, and one line for each that
    /// does not: its element is gone, its column is not the kind's, the standard no longer takes the
    /// value, or the cell changed in Revit since the preview.
    /// </summary>
    public static (IReadOnlyList<ParameterTableChange> Changes, IReadOnlyList<string> Skipped) Accept(
        IEnumerable<IReadOnlyDictionary<string, string>> currentRows,
        IEnumerable<ParameterTableChange> accepted,
        GisToolStandard standard)
    {
        Dictionary<string, IReadOnlyDictionary<string, string>> current = new(StringComparer.Ordinal);
        foreach (IReadOnlyDictionary<string, string> entry in currentRows)
        {
            current[entry["element_id"]] = entry;
        }

        List<ParameterTableChange> changes = new();
        List<string> skipped = new();
        HashSet<(string, string)> seen = new();
        foreach (ParameterTableChange change in accepted)
        {
            if (!seen.Add((change.ElementId, change.Column)))
            {
                continue;
            }

            if (!current.TryGetValue(change.ElementId, out IReadOnlyDictionary<string, string>? entry))
            {
                skipped.Add($"Skipped {change.Label()}: the element is no longer in the model.");
                continue;
            }

            string kind = entry["kind"];
            if (!Editable.TryGetValue(kind, out IReadOnlyList<string>? owned) || !owned.Contains(change.Column))
            {
                skipped.Add($"Skipped {change.Label()}: {change.Column} cannot be set on a {kind} here.");
                continue;
            }

            string now = entry.TryGetValue(change.Column, out string? value) ? value : string.Empty;
            if (now != change.OldValue)
            {
                skipped.Add($"Skipped {change.Label()}: the value changed in Revit since the preview.");
                continue;
            }

            (string? checkedValue, string? problem) = CheckValue(change.Column, change.NewValue, standard);
            if (problem != null)
            {
                skipped.Add($"Skipped {change.Label()}: {problem}");
                continue;
            }

            changes.Add(new ParameterTableChange(change.ElementId, kind, change.Column, now, checkedValue!));
        }

        return (changes, skipped);
    }

    /// <summary>The value to write for a cell, or a problem when the standard does not accept it.</summary>
    public static (string? Value, string? Problem) CheckValue(string column, string value, GisToolStandard standard)
    {
        value ??= string.Empty;
        if (column == "category" && value.Length > 0 && !standard.IsKnownCategory(value))
        {
            return (null, $"'{value}' is not a unit category.");
        }

        if (column == "gis_direction" && value.Length > 0)
        {
            string direction = DirectionWords.TryGetValue(value, out string? word) ? word : value.ToLowerInvariant();
            return Directions.Contains(direction)
                ? (direction, null)
                : (null, $"gis_direction '{value}' is not one of {string.Join(", ", Directions)}.");
        }

        if (IntegerColumns.Contains(column))
        {
            if (value.Length == 0)
            {
                return (null, $"{column} cannot be emptied here; type a number.");
            }

            return int.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out int number)
                ? (number.ToString(CultureInfo.InvariantCulture), null)
                : (null, $"{column} '{value}' is not a whole number.");
        }

        return (value, null);
    }

    private static string Quote(string value)
    {
        value ??= string.Empty;
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 || value != value.Trim())
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private static List<List<string>> ParseRecords(string text)
    {
        List<List<string>> records = new();
        List<string> record = new();
        StringBuilder cell = new();
        bool quoted = false;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (quoted)
            {
                if (current == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        cell.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    cell.Append(current);
                }
            }
            else if (current == '"')
            {
                quoted = true;
            }
            else if (current == ',')
            {
                record.Add(cell.ToString());
                cell.Clear();
            }
            else if (current is '\r' or '\n')
            {
                if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                record.Add(cell.ToString());
                records.Add(record);
                record = new List<string>();
                cell.Clear();
            }
            else
            {
                cell.Append(current);
            }
        }

        if (cell.Length > 0 || record.Count > 0)
        {
            record.Add(cell.ToString());
            records.Add(record);
        }

        return records;
    }
}
