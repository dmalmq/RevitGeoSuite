using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

/// <summary>
/// Guards every hand-written text file in the repository against mojibake. A save through a
/// Shift_JIS editor once turned 18 Japanese zone names in ZoneCatalog into U+FFFD replacement
/// characters, so floors named ラチ内コンコース or 待合室 silently lost their category. A file must
/// be valid UTF-8 and must not contain U+FFFD. The scan reads the files git tracks, so ignored
/// local caches and build output never fail it; without git it walks the tree instead.
/// </summary>
public sealed class SourceEncodingTests
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".props", ".targets", ".sln", ".config", ".addin",
        ".json", ".xml", ".xaml", ".resx", ".yml", ".yaml",
        ".ts", ".js", ".svelte", ".css", ".html",
        ".mjs", ".cjs", ".c", ".cpp", ".h", ".hpp", ".cmake",
        ".md", ".txt", ".csv", ".gml", ".template", ".iss", ".ps1", ".psm1", ".py",
        ".gitignore", ".gitattributes",
    };

    // Fallback walk only: folders .gitignore ignores at any depth.
    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".claude", ".cursor", ".dotnet", "bin", "obj", "node_modules", "dist",
        "packages", "TestResults",
    };

    // Fallback walk only: folders .gitignore ignores at one path. A bare "build" name is not
    // skipped, because src/RevitGeoSuite.SharedUI/build holds tracked MSBuild targets.
    private static readonly HashSet<string> SkippedDirectoryPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "native/build", "install/output", "install/dist",
        "src/RevitGeoSuite.SharedUI/web-contracts", "src/RevitGeoSuite.SharedUI/web-contracts-generator",
    };

    [Fact]
    public void SourceFiles_AreValidUtf8WithoutReplacementCharacters()
    {
        string repoRoot = TestPaths.GetRepoPath(string.Empty);
        UTF8Encoding strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        List<string> problems = new();
        int scanned = 0;

        foreach (string file in EnumerateCandidateFiles(repoRoot).Where(IsTextFile))
        {
            scanned++;
            string relative = file.Substring(repoRoot.Length).TrimStart(Path.DirectorySeparatorChar);
            string text;
            try
            {
                text = strictUtf8.GetString(File.ReadAllBytes(file));
            }
            catch (DecoderFallbackException)
            {
                problems.Add($"{relative}: not valid UTF-8");
                continue;
            }

            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf('\uFFFD') >= 0)
                {
                    problems.Add($"{relative}:{i + 1}: U+FFFD replacement character");
                }
            }
        }

        Assert.True(scanned > 100, $"Only {scanned} text files found under {repoRoot}; the scan is not looking at the repository.");
        Assert.True(problems.Count == 0, "Broken text encoding:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    private static bool IsTextFile(string file)
    {
        string extension = Path.GetExtension(file);
        return TextExtensions.Contains(extension.Length > 0 ? extension : Path.GetFileName(file));
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string repoRoot)
    {
        IReadOnlyList<string>? tracked = TryListTrackedFiles(repoRoot);
        if (tracked is not null)
        {
            return tracked
                .Select(relative => Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar)))
                .Where(File.Exists);
        }

        return WalkTree(repoRoot, repoRoot);
    }

    private static IReadOnlyList<string>? TryListTrackedFiles(string repoRoot)
    {
        try
        {
            ProcessStartInfo startInfo = new("git", "-c core.quotepath=off ls-files -z")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
            };

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return null;
            }

            return output.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
        {
            return null;
        }
    }

    private static IEnumerable<string> WalkTree(string repoRoot, string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            yield return file;
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            string relative = child.Substring(repoRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (SkippedDirectoryNames.Contains(Path.GetFileName(child)) || SkippedDirectoryPaths.Contains(relative))
            {
                continue;
            }

            foreach (string file in WalkTree(repoRoot, child))
            {
                yield return file;
            }
        }
    }
}
