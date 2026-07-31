using System;
using System.IO;

namespace RevitGeoSuite.PlateauImport.Tests;

internal static class TestPathHelper
{
    public static string GetFixturePath(params string[] segments)
    {
        string path = FindRepoRoot();
        foreach (string segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
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
