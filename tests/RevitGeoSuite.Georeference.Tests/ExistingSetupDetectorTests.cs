using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class ExistingSetupDetectorTests
{
    [Fact]
    public void Detect_returns_false_for_default_project_position_without_stored_metadata()
    {
        ExistingSetupDetector detector = new ExistingSetupDetector();
        ProjectPositionSnapshot snapshot = new ProjectPositionSnapshot();

        ExistingSetupDetectionResult result = detector.Detect(snapshot, hasStoredGeoInfo: false);

        Assert.False(result.Detected);
        Assert.Equal(string.Empty, result.Message);
    }

    [Fact]
    public void Detect_returns_true_for_non_default_offsets_or_stored_metadata()
    {
        ExistingSetupDetector detector = new ExistingSetupDetector();
        ProjectPositionSnapshot snapshot = new ProjectPositionSnapshot { EastWestFeet = 10.0, AngleRadians = 0.25 };

        ExistingSetupDetectionResult result = detector.Detect(snapshot, hasStoredGeoInfo: true);

        Assert.True(result.Detected);
        Assert.Contains("east/west offset", result.Message);
        Assert.Contains("true north angle", result.Message);
        Assert.Contains("stored geo metadata", result.Message);
    }
}
