using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class FixturePackTests
{
    [Theory]
    [InlineData("tests/Fixtures/Crs/japan-presets.json")]
    [InlineData("tests/Fixtures/Transforms/latlon-to-projected.json")]
    [InlineData("tests/Fixtures/Mesh/japan-mesh-samples.json")]
    [InlineData("tests/Fixtures/Storage/geo-project-info-roundtrip.json")]
    [InlineData("tests/Fixtures/Storage/geo-project-info-v0.json")]
    public void Fixture_file_exists_and_is_valid_json(string relativePath)
    {
        string path = GetRepoPath(relativePath);

        Assert.True(File.Exists(path), $"Missing fixture: {path}");
        Assert.NotNull(JToken.Parse(File.ReadAllText(path)));
    }

    [Fact]
    public void Manual_revit_fixture_placeholder_exists()
    {
        string path = GetRepoPath("tests/Fixtures/Revit/manual-test-files.md");

        Assert.True(File.Exists(path));
        Assert.Contains("Revit 2024", File.ReadAllText(path));
    }

    private static string GetRepoPath(string relativePath)
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RevitGeoSuite.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root from test output directory.");
    }
}
