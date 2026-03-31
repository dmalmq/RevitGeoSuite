using System;
using System.IO;

namespace RevitGeoSuite.Core.Tests;

internal static class TestPaths
{
    public static string GetRepoPath(string relativePath)
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
