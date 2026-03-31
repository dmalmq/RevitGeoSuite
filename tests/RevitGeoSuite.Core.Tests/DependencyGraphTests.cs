using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class DependencyGraphTests
{
    [Fact]
    public void Project_references_match_current_dependency_rules()
    {
        AssertProjectReferences("src/RevitGeoSuite.Core/RevitGeoSuite.Core.csproj");
        AssertProjectReferences(
            "src/RevitGeoSuite.Core.Plateau/RevitGeoSuite.Core.Plateau.csproj",
            "RevitGeoSuite.Core.csproj");
        AssertProjectReferences(
            "src/RevitGeoSuite.RevitInterop/RevitGeoSuite.RevitInterop.csproj",
            "RevitGeoSuite.Core.csproj");
        AssertProjectReferences(
            "src/RevitGeoSuite.SharedUI/RevitGeoSuite.SharedUI.csproj",
            "RevitGeoSuite.Core.csproj");
        AssertProjectReferences(
            "src/RevitGeoSuite.Georeference/RevitGeoSuite.Georeference.csproj",
            "RevitGeoSuite.Core.csproj",
            "RevitGeoSuite.RevitInterop.csproj",
            "RevitGeoSuite.SharedUI.csproj");
        AssertProjectReferences(
            "src/RevitGeoSuite.MeshInspector/RevitGeoSuite.MeshInspector.csproj",
            "RevitGeoSuite.Core.csproj",
            "RevitGeoSuite.RevitInterop.csproj",
            "RevitGeoSuite.SharedUI.csproj");
        AssertProjectReferences(
            "src/RevitGeoSuite.Validation/RevitGeoSuite.Validation.csproj",
            "RevitGeoSuite.Core.csproj",
            "RevitGeoSuite.RevitInterop.csproj",
            "RevitGeoSuite.SharedUI.csproj");
        AssertProjectReferences(
            "src/RevitGeoSuite.PlateauImport/RevitGeoSuite.PlateauImport.csproj",
            "RevitGeoSuite.Core.csproj",
            "RevitGeoSuite.Core.Plateau.csproj",
            "RevitGeoSuite.RevitInterop.csproj",
            "RevitGeoSuite.SharedUI.csproj");
        AssertProjectReferences(
            "src/RevitGeoSuite.Shell/RevitGeoSuite.Shell.csproj",
            "RevitGeoSuite.Core.csproj",
            "RevitGeoSuite.Georeference.csproj",
            "RevitGeoSuite.MeshInspector.csproj",
            "RevitGeoSuite.PlateauImport.csproj",
            "RevitGeoSuite.RevitInterop.csproj",
            "RevitGeoSuite.SharedUI.csproj",
            "RevitGeoSuite.Validation.csproj");
    }

    private static void AssertProjectReferences(string relativeProjectPath, params string[] expectedProjectFiles)
    {
        string path = GetRepoPath(relativeProjectPath);
        XDocument document = XDocument.Load(path);

        string[] actual = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Path.GetFileName)
            .OrderBy(value => value)
            .ToArray()!;

        string[] expected = expectedProjectFiles.OrderBy(value => value).ToArray();
        Assert.Equal(expected, actual);
    }

    private static string GetRepoPath(string relativeProjectPath)
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));
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
