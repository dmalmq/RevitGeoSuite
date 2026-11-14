using System.Runtime.InteropServices;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// Opens a CSV in the real Excel and saves it again as Excel's own CSV, with no edits, the way a
/// person does in step 10. Needs Excel on the PC; the run notes it when Excel is missing.
/// </summary>
internal static class ExcelRoundTrip
{
    private const int CsvFormat = 6; // XlFileFormat.xlCSV: the code page of the PC's locale (Shift-JIS in Japan)

    public static string Save(string source, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Excel round trip skipped: not Windows.";
        }

        Type? type = Type.GetTypeFromProgID("Excel.Application");
        if (type == null)
        {
            return "Excel round trip skipped: Excel is not installed.";
        }

        dynamic excel = Activator.CreateInstance(type)!;
        try
        {
            excel.DisplayAlerts = false;
            dynamic book = excel.Workbooks.Open(source);
            File.Delete(target);
            book.SaveAs(target, CsvFormat);
            book.Close(false);
            return $"Excel opened {Path.GetFileName(source)} and saved it as {target}.";
        }
        finally
        {
            excel.Quit();
            Marshal.FinalReleaseComObject(excel);
        }
    }
}
