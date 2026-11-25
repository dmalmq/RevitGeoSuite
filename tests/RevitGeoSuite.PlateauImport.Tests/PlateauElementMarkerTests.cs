using RevitGeoSuite.PlateauImport;
using RevitGeoSuite.PlateauImport.Online;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauElementMarkerTests
{
    [Theory]
    [InlineData("RevitGeoSuite.PlateauImport", true)]
    [InlineData("KanizuRevit.PlateauImport", true)]
    [InlineData("RevitGeoSuite.PlateauImport.Online", false)]
    [InlineData("OtherAddin", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PlateauContextImporter_RecognizesLegacyAndCurrentApplicationIds(string? applicationId, bool expected)
    {
        Assert.Equal(expected, PlateauContextImporter.IsOwnedApplicationId(applicationId));
    }

    [Theory]
    [InlineData("RevitGeoSuite.PlateauImport.Online", true)]
    [InlineData("KanizuRevit.PlateauImport.Online", true)]
    [InlineData("RevitGeoSuite.PlateauImport", false)]
    [InlineData("OtherAddin", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PlateauTilesImporter_RecognizesLegacyAndCurrentApplicationIds(string? applicationId, bool expected)
    {
        Assert.Equal(expected, PlateauTilesImporter.IsOwnedApplicationId(applicationId));
    }

    [Theory]
    [InlineData("RevitGeoSuite.PlateauGround", true)]
    [InlineData("KanizuRevit.PlateauGround", true)]
    [InlineData("OtherGround", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PlateauTopographyImporter_RecognizesLegacyAndCurrentGroundMarkers(string? comments, bool expected)
    {
        Assert.Equal(expected, PlateauTopographyImporter.IsOwnedGroundMarker(comments));
    }
}
