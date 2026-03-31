using RevitGeoSuite.Core.Plateau.Schema;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests;

public sealed class PlateauSchemaHelperTests
{
    [Theory]
    [InlineData("EPSG:6677", 6677)]
    [InlineData("urn:ogc:def:crs:EPSG::6677", 6677)]
    [InlineData("http://www.opengis.net/def/crs/EPSG/0/6677", 6677)]
    public void TryExtractEpsgCode_handles_common_srs_formats(string srsName, int expectedEpsg)
    {
        bool success = PlateauSchemaHelper.TryExtractEpsgCode(srsName, out int actualEpsg);

        Assert.True(success);
        Assert.Equal(expectedEpsg, actualEpsg);
    }

    [Fact]
    public void TryExtractTileIdFromPath_reads_tertiary_mesh_code_from_file_name()
    {
        string? tileId = PlateauSchemaHelper.TryExtractTileIdFromPath(@"C:\samples\plateau_53394611_bldg.gml");

        Assert.Equal("53394611", tileId);
    }
}
