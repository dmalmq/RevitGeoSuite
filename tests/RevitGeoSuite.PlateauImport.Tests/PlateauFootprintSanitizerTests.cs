using System.Linq;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauFootprintSanitizerTests
{
    [Fact]
    public void Sanitize_removes_short_segments_and_duplicate_closing_points()
    {
        PlateauFootprintSanitizer sanitizer = new PlateauFootprintSanitizer();

        var sanitized = sanitizer.Sanitize(
            new (double XFeet, double YFeet)[]
            {
                (0d, 0d),
                (0.001d, 0.001d),
                (20d, 0d),
                (20d, 15d),
                (0d, 15d),
                (0d, 0d)
            },
            0.01d).ToArray();

        Assert.Equal(4, sanitized.Length);
        Assert.Equal((0d, 0d), sanitized[0]);
        Assert.Equal((20d, 0d), sanitized[1]);
        Assert.Equal((20d, 15d), sanitized[2]);
        Assert.Equal((0d, 15d), sanitized[3]);
    }

    [Fact]
    public void Sanitize_returns_empty_when_polygon_collapses_below_tolerance()
    {
        PlateauFootprintSanitizer sanitizer = new PlateauFootprintSanitizer();

        var sanitized = sanitizer.Sanitize(
            new (double XFeet, double YFeet)[]
            {
                (0d, 0d),
                (0.001d, 0.001d),
                (0.002d, 0.001d),
                (0d, 0d)
            },
            0.01d);

        Assert.Empty(sanitized);
    }
}
