using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>
/// Pure-C# project template (<c>.rte</c>) seed resolver for Kanizu Starter Kit <c>TemplateBuilder</c>.
/// Supports both English and Japanese metric Revit project template directories (<c>US Metric</c>,
/// <c>English</c>, <c>Metric</c>, <c>Japan</c>, <c>Japanese</c>) and filenames, and explicitly
/// excludes Imperial directories (<c>English_I</c>, <c>US Imperial</c>, <c>*Imperial*</c>) so a
/// metric Starter Kit build can never silently seed from an Imperial template.
/// </summary>
public static class StarterKitProjectTemplateResolver
{
    private static readonly string[] EnglishMetricCandidates =
    {
        "DefaultMetric.rte",
        "Architectural-DefaultMetric.rte",
        "Default-Metric.rte",
        "Metric Default.rte",
        "Construction-DefaultMetric.rte",
        "Systems-DefaultMetric.rte",
        "Structural Analysis-DefaultMetric.rte",
    };

    private static readonly string[] JapaneseMetricCandidates =
    {
        "意匠(メートル単位).rte",
        "建築(メートル単位).rte",
        "意匠テンプレート(メートル単位).rte",
        "DefaultJPNJPN.rte",
        "デフォルト(メートル単位).rte",
        "施工(メートル単位).rte",
        "構造(メートル単位).rte",
    };

    private static readonly string[] AllCandidates = EnglishMetricCandidates
        .Concat(JapaneseMetricCandidates)
        .ToArray();

    private static readonly string[] EnglishPreferredSubdirectories =
    {
        string.Empty,
        "US Metric",
        "English",
        "Metric",
        "Japan",
        "Japanese",
        "Generic",
    };

    private static readonly string[] JapanesePreferredSubdirectories =
    {
        string.Empty,
        "Japan",
        "Japanese",
        "US Metric",
        "English",
        "Metric",
        "Generic",
    };

    private const int MaxRecursiveSearchDepth = 2;

    /// <summary>Returns ordered candidate metric project template (<c>.rte</c>) filenames across EN and JA Revit installs.</summary>
    public static IReadOnlyList<string> GetCandidateProjectTemplateFileNames(bool preferJapanese = false)
    {
        if (!preferJapanese)
        {
            return Array.AsReadOnly(AllCandidates);
        }

        return Array.AsReadOnly(JapaneseMetricCandidates.Concat(EnglishMetricCandidates).ToArray());
    }

    /// <summary>True when <paramref name="fileName"/> matches a known Japanese metric Revit template name.</summary>
    public static bool IsJapaneseProjectTemplateFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string name = Path.GetFileName(fileName!.Trim());
        return JapaneseMetricCandidates.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True when a directory name indicates an Imperial Revit template folder (such as <c>English_I</c>
    /// or <c>US Imperial</c>) that must never be used to seed a metric Starter Kit <c>.rte</c>.
    /// </summary>
    public static bool IsImperialTemplateDirectoryName(string? directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return false;
        }

        string name = directoryName!.Trim();
        return string.Equals(name, "English_I", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "English-Imperial", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "US Imperial", StringComparison.OrdinalIgnoreCase)
            || name.IndexOf("Imperial", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Resolves a metric seed <c>.rte</c> project template path.
    /// Checks <paramref name="explicitSeedTemplatePath"/> first, then scans <paramref name="projectTemplatesDirectory"/>
    /// across standard EN/JA metric locale subdirectories (<c>US Metric</c>, <c>English</c>, <c>Metric</c>,
    /// <c>Japan</c>, <c>Japanese</c>, <c>Generic</c>) plus bounded nested metric folders while excluding
    /// Imperial folders (<c>English_I</c>, <c>US Imperial</c>, <c>*Imperial*</c>).
    /// </summary>
    public static string ResolveProjectTemplatePath(
        string? projectTemplatesDirectory,
        string? explicitSeedTemplatePath = null,
        bool preferJapanese = false)
    {
        if (!string.IsNullOrWhiteSpace(explicitSeedTemplatePath))
        {
            string fullExplicit = Path.GetFullPath(explicitSeedTemplatePath!.Trim());
            if (!File.Exists(fullExplicit))
            {
                throw new FileNotFoundException(
                    $"Configured project seed template .rte was not found at '{fullExplicit}'.",
                    fullExplicit);
            }

            if (!fullExplicit.EndsWith(".rte", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Project seed template must be a .rte file, found '{fullExplicit}'.");
            }

            return fullExplicit;
        }

        if (string.IsNullOrWhiteSpace(projectTemplatesDirectory))
        {
            throw new ArgumentException(
                "Either an explicit project seed .rte path or a project templates directory is required.",
                nameof(projectTemplatesDirectory));
        }

        string rootDir = Path.GetFullPath(projectTemplatesDirectory!.Trim());
        if (!Directory.Exists(rootDir))
        {
            throw new DirectoryNotFoundException(
                $"Project templates directory was not found at '{rootDir}'.");
        }

        IReadOnlyList<string> candidates = GetCandidateProjectTemplateFileNames(preferJapanese);
        string[] preferredSubdirs = preferJapanese
            ? JapanesePreferredSubdirectories
            : EnglishPreferredSubdirectories;

        HashSet<string> searchedDirs = new(StringComparer.OrdinalIgnoreCase);

        foreach (string subdir in preferredSubdirs)
        {
            string searchDir = string.IsNullOrEmpty(subdir) ? rootDir : Path.Combine(rootDir, subdir);
            if (!Directory.Exists(searchDir) || !searchedDirs.Add(searchDir))
            {
                continue;
            }

            string? match = ProbeDirectoryForMetricCandidates(searchDir, candidates);
            if (match != null)
            {
                return match;
            }
        }

        // Bounded recursive fallback for non-standard metric nesting (e.g. Templates/2024/US Metric/),
        // strictly skipping Imperial directories such as English_I or US Imperial.
        foreach (string nestedDir in EnumerateNonImperialSubdirectories(rootDir, MaxRecursiveSearchDepth))
        {
            if (!searchedDirs.Add(nestedDir))
            {
                continue;
            }

            string? match = ProbeDirectoryForMetricCandidates(nestedDir, candidates);
            if (match != null)
            {
                return match;
            }
        }

        throw new FileNotFoundException(
            $"Could not find a metric Revit project seed template (.rte) in '{rootDir}'. " +
            $"Tried EN/JA metric candidates: {string.Join(", ", candidates)} (excluding Imperial directories).");
    }

    private static string? ProbeDirectoryForMetricCandidates(string directory, IReadOnlyList<string> candidates)
    {
        foreach (string fileName in candidates)
        {
            string candidatePath = Path.Combine(directory, fileName);
            if (File.Exists(candidatePath))
            {
                return Path.GetFullPath(candidatePath);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateNonImperialSubdirectories(string rootDir, int maxDepth)
    {
        Queue<(string Path, int Depth)> queue = new();
        queue.Enqueue((rootDir, 0));

        while (queue.Count > 0)
        {
            (string current, int depth) = queue.Dequeue();
            if (depth >= maxDepth)
            {
                continue;
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(current);
            }
            catch (Exception)
            {
                continue;
            }

            Array.Sort(children, StringComparer.OrdinalIgnoreCase);
            foreach (string child in children)
            {
                string dirName = Path.GetFileName(child);
                if (IsImperialTemplateDirectoryName(dirName))
                {
                    continue;
                }

                yield return child;
                queue.Enqueue((child, depth + 1));
            }
        }
    }
}
