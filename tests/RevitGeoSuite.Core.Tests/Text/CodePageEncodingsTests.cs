using System.Text;
using RevitGeoSuite.Core.Text;
using Xunit;

namespace RevitGeoSuite.Core.Tests.Text;

public sealed class CodePageEncodingsTests
{
    // "ホーム" as Japanese Excel saves it in CP932 (Shift_JIS).
    private static readonly byte[] HomeInCp932 = { 0x83, 0x7A, 0x81, 0x5B, 0x83, 0x80 };

    [Fact]
    public void Cp932_round_trips_japanese_text_on_every_revit_runtime()
    {
        Encoding cp932 = CodePageEncodings.Cp932(EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        Assert.Equal(932, cp932.CodePage);
        Assert.Equal("ホーム", cp932.GetString(HomeInCp932));
        Assert.Equal(HomeInCp932, cp932.GetBytes("ホーム"));
    }

    [Fact]
    public void Registration_makes_shift_jis_available_by_name_for_libraries_such_as_shapefile_readers()
    {
        CodePageEncodings.EnsureRegistered();
        CodePageEncodings.EnsureRegistered();

        Assert.Equal(932, Encoding.GetEncoding("shift_jis").CodePage);
        Assert.Equal("ホーム", Encoding.GetEncoding(932).GetString(HomeInCp932));
    }
}
