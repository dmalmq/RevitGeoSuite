using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

/// <summary>
/// Guards every hand-written text file in the repository against mojibake. A save through a
/// Shift_JIS editor once turned 18 Japanese zone names in ZoneCatalog into U+FFFD replacement
/// characters, so floors named ラチ内コンコース or 待合室 silently lost their category. A file must
/// be valid UTF-8 and must not contain U+FFFD.
/// </summary>
public sealed class SourceEncodingTests
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".props", ".targets", ".sln", ".config", ".addin",
        ".json", ".xml", ".xaml", ".resx", ".yml", ".yaml",
        ".ts", ".js", ".svelte", ".css", ".html",
        ".md", ".txt", ".iss", ".ps1", ".psm1", ".py",
    };

    // Generated or third-party output: minified bundles may carry U+FFFD on purpose.
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules", "dist",
    };

    [Fact]
    public void SourceFiles_AreValidUtf8WithoutReplacementCharacters()
    {
        string repoRoot = TestPaths.GetRepoPath(string.Empty);
        UTF8Encoding strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        List<string> problems = new();
        int scanned = 0;

        foreach (string file in EnumerateTextFiles(repoRoot))
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

    private static IEnumerable<string> EnumerateTextFiles(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            if (TextExtensions.Contains(Path.GetExtension(file)))
            {
                yield return file;
            }
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            if (SkippedDirectories.Contains(Path.GetFileName(child)))
            {
                continue;
            }

            foreach (string file in EnumerateTextFiles(child))
            {
                yield return file;
            }
        }
    }
}
