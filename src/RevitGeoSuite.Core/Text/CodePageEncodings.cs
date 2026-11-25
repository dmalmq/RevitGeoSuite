using System.Text;

namespace RevitGeoSuite.Core.Text;

/// <summary>
/// Windows code pages such as CP932 (Shift_JIS), which Japanese Excel CSV files and shapefile
/// .dbf attribute tables use. .NET Framework (Revit 2024) has them built in. .NET (Revit 2025
/// and later) throws <see cref="System.NotSupportedException"/> for them, and NetTopologySuite
/// silently falls back to another encoding, until the code pages provider is registered.
/// </summary>
public static class CodePageEncodings
{
    public const int Cp932CodePage = 932;

    /// <summary>
    /// Makes the Windows code pages available in this process. Safe to call any number of times:
    /// the runtime ignores a provider it already holds.
    /// </summary>
    public static void EnsureRegistered()
    {
#if !NETFRAMEWORK
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
    }

    public static Encoding Cp932() => Cp932(EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);

    public static Encoding Cp932(EncoderFallback encoderFallback, DecoderFallback decoderFallback)
    {
        EnsureRegistered();
        return Encoding.GetEncoding(Cp932CodePage, encoderFallback, decoderFallback);
    }
}
