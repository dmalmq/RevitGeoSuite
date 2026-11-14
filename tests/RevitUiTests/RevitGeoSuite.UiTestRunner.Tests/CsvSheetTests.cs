using System.Text;
using Xunit;

namespace RevitGeoSuite.UiTestRunner.Tests;

public class CsvSheetTests
{
    private static byte[] Utf8Bom(string text) => new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();

    [Fact]
    public void Reads_the_written_file_with_its_byte_order_mark()
    {
        CsvSheet sheet = CsvSheet.Read(Utf8Bom("element_id,kind,name\r\n101,floor,北改札\r\n102,level,\r\n"));

        Assert.Equal(new[] { "element_id", "kind", "name" }, sheet.Header);
        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal("北改札", sheet.Rows[0]["name"]);
        Assert.Equal(string.Empty, sheet.Rows[1]["name"]);
    }

    [Fact]
    public void Writes_what_Japanese_Excel_saves_shift_jis_without_a_byte_order_mark()
    {
        CsvSheet sheet = CsvSheet.Read(Utf8Bom("element_id,name\r\n101,北改札内コンコース\r\n"));

        byte[] data = sheet.Write(CsvSheet.ExcelJapanese);

        Assert.False(CsvSheet.StartsWithUtf8Bom(data));
        Assert.Equal("element_id,name\r\n101,北改札内コンコース\r\n", CsvSheet.ExcelJapanese.GetString(data));
        Assert.Throws<DecoderFallbackException>(() => new UTF8Encoding(false, true).GetString(data));
        Assert.Equal("北改札内コンコース", CsvSheet.Read(data).Rows[0]["name"]);
    }

    [Fact]
    public void Quotes_commas_quotes_and_line_breaks_and_reads_them_back()
    {
        var sheet = new CsvSheet(new[] { "a", "b" }, new List<Dictionary<string, string>>
        {
            new() { ["a"] = "x, y", ["b"] = "say \"hi\"\r\nnext" },
        });

        byte[] data = sheet.Write(CsvSheet.Utf8WithBom);

        Assert.True(CsvSheet.StartsWithUtf8Bom(data));
        Assert.Equal("a,b\r\n\"x, y\",\"say \"\"hi\"\"\r\nnext\"\r\n", Encoding.UTF8.GetString(data, 3, data.Length - 3));
        Dictionary<string, string> row = Assert.Single(CsvSheet.Read(data).Rows);
        Assert.Equal("x, y", row["a"]);
        Assert.Equal("say \"hi\"\r\nnext", row["b"]);
    }

    [Fact]
    public void Copy_edits_do_not_touch_the_original()
    {
        CsvSheet original = CsvSheet.Read(Utf8Bom("id,name\r\n1,a\r\n"));

        CsvSheet copy = original.Copy();
        copy.Rows[0]["name"] = "b";
        copy.Rows.Clear();

        Assert.Equal("a", Assert.Single(original.Rows)["name"]);
    }
}
