using System.Text;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The parameters CSV as Excel sees it: a header row and data rows. The runner makes the edits a
/// person makes in Excel and saves them the way Excel does.
/// </summary>
internal sealed class CsvSheet
{
    static CsvSheet() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public CsvSheet(IReadOnlyList<string> header, List<Dictionary<string, string>> rows)
    {
        Header = header;
        Rows = rows;
    }

    public IReadOnlyList<string> Header { get; }

    public List<Dictionary<string, string>> Rows { get; }

    /// <summary>Japanese Excel's "CSV (Comma delimited)": Shift-JIS (code page 932), CRLF, no byte order mark.</summary>
    public static Encoding ExcelJapanese => Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    /// <summary>What Write CSV produces and Excel opens without garbling: UTF-8 with a byte order mark.</summary>
    public static Encoding Utf8WithBom => new UTF8Encoding(true);

    public static bool StartsWithUtf8Bom(byte[] data) => data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF;

    public static CsvSheet Read(byte[] data)
    {
        string text = StartsWithUtf8Bom(data)
            ? new UTF8Encoding(false, true).GetString(data, 3, data.Length - 3)
            : DecodeWithoutBom(data);
        List<List<string>> records = ParseRecords(text);
        if (records.Count == 0)
        {
            throw new FormatException("The CSV is empty.");
        }

        List<string> header = records[0];
        var rows = records.Skip(1)
            .Where(record => record.Any(cell => cell.Length > 0))
            .Select(record => header.Select((column, index) => (column, value: index < record.Count ? record[index] : string.Empty))
                .ToDictionary(pair => pair.column, pair => pair.value, StringComparer.Ordinal))
            .ToList();
        return new CsvSheet(header, rows);
    }

    public byte[] Write(Encoding encoding)
    {
        var text = new StringBuilder();
        text.Append(string.Join(",", Header.Select(Quote))).Append("\r\n");
        foreach (Dictionary<string, string> row in Rows)
        {
            text.Append(string.Join(",", Header.Select(column => Quote(row.TryGetValue(column, out string? value) ? value : string.Empty)))).Append("\r\n");
        }

        byte[] preamble = encoding.GetPreamble();
        return preamble.Concat(encoding.GetBytes(text.ToString())).ToArray();
    }

    public CsvSheet Copy() => new(Header, Rows.Select(row => new Dictionary<string, string>(row, StringComparer.Ordinal)).ToList());

    private static string DecodeWithoutBom(byte[] data)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return ExcelJapanese.GetString(data);
        }
    }

    private static string Quote(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    // RFC 4180, as Excel writes it: quoted fields may hold commas, quotes ("") and line breaks.
    private static List<List<string>> ParseRecords(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (quoted)
            {
                if (current == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (current == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(current);
                }
            }
            else if (current == '"')
            {
                quoted = true;
            }
            else if (current == ',')
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (current == '\r' || current == '\n')
            {
                if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                record.Add(field.ToString());
                field.Clear();
                records.Add(record);
                record = new List<string>();
            }
            else
            {
                field.Append(current);
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }
}
