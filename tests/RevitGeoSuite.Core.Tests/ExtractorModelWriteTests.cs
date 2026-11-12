using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

/// <summary>
/// The floor-plan extractors run while the views are prepared, with no Revit transaction open.
/// A stair that ended on a level outside the exported views once asked for that level's id
/// through <c>GetLevelMetadata</c>, which creates a missing id; Revit refused the write
/// ("Attempt to modify the model outside of transaction") and the whole export failed. The
/// extractors cannot run outside Revit, so this reads their source: a level they only refer to
/// goes through the read-only <c>GetReferencedLevelId</c>.
/// </summary>
public sealed class ExtractorModelWriteTests
{
    private static readonly string[] CreatingCalls =
    {
        ".GetLevelMetadata(", "GetOrCreateLevelId(", "GetOrCreateElementId(", "RegenerateElementId(",
    };

    [Fact]
    public void Extractors_DoNotCreateLevelIds()
    {
        string folder = TestPaths.GetRepoPath(Path.Combine("src", "RevitGeoSuite.FloorPlanExport", "Extractors"));
        List<string> problems = new();
        foreach (string file in Directory.GetFiles(folder, "*.cs"))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (CreatingCalls.Any(call => lines[i].Contains(call)))
                {
                    problems.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(problems.Count == 0, "Extractors must not create ids, which writes to the model:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void PersistentProvider_ReadsAReferencedLevelIdWithoutCreatingIt()
    {
        string source = File.ReadAllText(TestPaths.GetRepoPath(Path.Combine(
            "src", "RevitGeoSuite.FloorPlanExport", "Export", "PersistentExportMetadataProvider.cs")));
        int start = source.IndexOf("public string? GetReferencedLevelId(", System.StringComparison.Ordinal);
        Assert.True(start >= 0, "GetReferencedLevelId is missing.");
        int end = source.IndexOf("\n    }", start, System.StringComparison.Ordinal);
        string body = source.Substring(start, end - start);
        Assert.Contains("GetOptionalStringParameter", body);
        Assert.DoesNotContain("GetOrCreate", body);
    }
}
