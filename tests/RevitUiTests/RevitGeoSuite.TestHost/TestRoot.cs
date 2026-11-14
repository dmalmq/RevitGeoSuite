using System;
using System.IO;

namespace RevitGeoSuite.TestHost;

/// <summary>
/// The only folder the test host opens models from and writes into. Tests run on detached copies
/// under it and never on an original or central model, so anything outside it is refused.
/// </summary>
internal static class TestRoot
{
    public const string Variable = "REVITGEOSUITE_TESTHOST_ROOT";
    public const string Default = @"C:\dev\revit-test";

    public static string Resolve()
    {
        string? configured = Environment.GetEnvironmentVariable(Variable);
        return Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Default : configured);
    }

    public static string Require(string root, string path)
    {
        string full = Path.GetFullPath(path);
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"'{full}' is outside the test folder {root}. The test host only works on copies there.");
        }

        return full;
    }
}
