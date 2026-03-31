using System;
using System.Globalization;
using System.IO;
using System.Linq;
using RevitGeoSuite.Core.Plateau.Tiles;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests;

public sealed class PlateauTileIndexTests
{
    [Fact]
    public void GetCandidateTiles_returns_primary_and_neighbor_meshes_from_fixture_sample()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Samples", "tile-index-samples.csv");
        string[] lines = File.ReadAllLines(fixturePath);
        string[] values = lines[1].Split(',');
        double latitude = double.Parse(values[1], CultureInfo.InvariantCulture);
        double longitude = double.Parse(values[2], CultureInfo.InvariantCulture);
        string expectedMesh = values[3];

        PlateauTileIndex tileIndex = new PlateauTileIndex();
        var candidates = tileIndex.GetCandidateTiles(latitude, longitude);

        Assert.Equal(9, candidates.Count);
        Assert.Contains(candidates, candidate => candidate.IsPrimary && string.Equals(candidate.TileId, expectedMesh, StringComparison.Ordinal));
        Assert.Equal(9, candidates.Select(candidate => candidate.TileId).Distinct(StringComparer.Ordinal).Count());
    }
}
