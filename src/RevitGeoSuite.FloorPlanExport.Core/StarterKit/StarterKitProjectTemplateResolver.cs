using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>
/// Pure-C# project template (<c>.rte</c>) seed resolver for Kanizu Starter Kit <c>TemplateBuilder</c>.
/// Supports both English and Japanese Revit project template directories and filenames, preferring
/// metric architectural/default templates over imperial templates.
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
        "Default.rte",
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

    private static readonly string[] LocaleSubdirectories =
    {
        string.Empty,
        "English",
        "English_I",
        "Japanese",
        "Metric",
        "Generic",
    };

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
    /// Resolves a metric seed <c>.rte</c> project template path.
    /// Checks <paramref name="explicitSeedTemplatePath"/> first, then scans <paramref name="projectTemplatesDirectory"/>
    /// (including standard EN/JA locale subdirectories) for known English and Japanese metric <c>.rte</c> files.
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
        string[] subdirs = preferJapanese
            ? new[] { string.Empty, "Japanese", "English", "Metric", "Generic", "English_I" }
            : LocaleSubdirectories;

        List<string> probed = new();
        foreach (string subdir in subdirs)
        {
            string searchDir = string.IsNullOrEmpty(subdir) ? rootDir : Path.Combine(rootDir, subdir);
            if (!Directory.Exists(searchDir))
            {
                continue;
            }

            foreach (string fileName in candidates)
            {
                string candidatePath = Path.Combine(searchDir, fileName);
                probed.Add(candidatePath);
                if (File.Exists(candidatePath))
                {
                    return Path.GetFullPath(candidatePath);
                }
            }
        }

        throw new FileNotFoundException(
            $"Could not find a metric Revit project seed template (.rte) in '{rootDir}'. " +
            $"Tried EN/JA metric candidates: {string.Join(", ", candidates)}.");
    }
}
