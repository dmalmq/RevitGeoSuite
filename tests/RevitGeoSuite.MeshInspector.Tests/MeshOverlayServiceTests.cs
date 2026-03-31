using System.Text.RegularExpressions;
using RevitGeoSuite.Core.Mesh;
using Xunit;

namespace RevitGeoSuite.MeshInspector.Tests;

public sealed class MeshOverlayServiceTests
{
    [Fact]
    public void CreateGeoJson_emits_primary_and_neighbor_features()
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();
        MeshCode primary = new MeshCode { Value = "53394611" };
        MeshNeighborResolver neighborResolver = new MeshNeighborResolver(calculator);
        MeshOverlayService service = new MeshOverlayService(calculator);

        string geoJson = service.CreateGeoJson(primary, neighborResolver.GetNeighbors(primary));

        Assert.Equal(9, Regex.Matches(geoJson, "\\\"type\\\":\\\"Feature\\\"").Count);
        Assert.Contains("\"meshCode\":\"53394611\"", geoJson);
        Assert.Contains("\"isPrimary\":true", geoJson);
    }
}
