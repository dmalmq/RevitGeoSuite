using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class RevitApiCompatibilityTests
{
    /// <summary>
    /// Element ids are 64-bit from Revit 2024 on. The add-in builds for Revit 2024 and 2027 from one
    /// source, so it reads ids only through <c>ElementId.Value</c> (long), which both years have.
    /// <c>IntegerValue</c> is deprecated since Revit 2024 and truncates ids above int.MaxValue.
    /// </summary>
    [Fact]
    public void Source_reads_element_ids_as_64_bit()
    {
        Regex integerValue = new(@"\.IntegerValue\b");
        string[] offenders = Directory
            .EnumerateFiles(TestPaths.GetRepoPath("src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(TestPaths.GetRepoPath(Path.Combine("tests", "RevitUiTests")), "*.cs", SearchOption.AllDirectories))
            .Where(path => path.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) < 0)
            .Where(path => integerValue.IsMatch(File.ReadAllText(path)))
            .ToArray();

        Assert.Empty(offenders);
    }
}
