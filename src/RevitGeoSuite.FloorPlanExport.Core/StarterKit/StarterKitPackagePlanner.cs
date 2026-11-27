using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>
/// JSON-serializable configuration passed from <c>scripts/Build-StarterKit.ps1</c> to
/// <c>StarterKitBuildEntryPoint</c> inside Revit 2024 or Revit 2027.
/// </summary>
public sealed class StarterKitBuildConfig
{
    public const string DefaultConfigEnvironmentVariable = "KANIZU_STARTER_KIT_BUILD_CONFIG";

    public string RevitYear { get; set; } = "2024";

    public string StarterKitSpecDirectory { get; set; } = string.Empty;

    public string? StandardsDirectory { get; set; }

    public string FamilySeedTemplatesDirectory { get; set; } = string.Empty;

    public string? ProjectSeedTemplatePath { get; set; }

    public string? ProjectSeedTemplatesDirectory { get; set; }

    public string OutputDirectory { get; set; } = string.Empty;

    public string? SharedParametersFilePath { get; set; }

    public bool IncludeOptionalFamilies { get; set; } = true;

    public bool OverwriteExisting { get; set; } = true;

    public bool PreferJapaneseSeedTemplates { get; set; }

    /// <summary>
    /// <c>"WithinBudget"</c> (default for live Revit tessellation builds) or <c>"Exact"</c>.
    /// </summary>
    public string TriangleComparisonMode { get; set; } = "WithinBudget";

    public ManifestTriangleComparisonMode ResolveTriangleMode() =>
        string.Equals(TriangleComparisonMode, "Exact", StringComparison.OrdinalIgnoreCase)
            ? ManifestTriangleComparisonMode.Exact
            : ManifestTriangleComparisonMode.WithinBudget;

    public string ToJson(bool indented = true)
    {
        JObject root = new()
        {
            ["revitYear"] = RevitYear ?? string.Empty,
            ["starterKitSpecDirectory"] = StarterKitSpecDirectory ?? string.Empty,
            ["standardsDirectory"] = StandardsDirectory ?? string.Empty,
            ["familySeedTemplatesDirectory"] = FamilySeedTemplatesDirectory ?? string.Empty,
            ["projectSeedTemplatePath"] = ProjectSeedTemplatePath ?? string.Empty,
            ["projectSeedTemplatesDirectory"] = ProjectSeedTemplatesDirectory ?? string.Empty,
            ["outputDirectory"] = OutputDirectory ?? string.Empty,
            ["sharedParametersFilePath"] = SharedParametersFilePath ?? string.Empty,
            ["includeOptionalFamilies"] = IncludeOptionalFamilies,
            ["overwriteExisting"] = OverwriteExisting,
            ["preferJapaneseSeedTemplates"] = PreferJapaneseSeedTemplates,
            ["triangleComparisonMode"] = TriangleComparisonMode ?? "WithinBudget",
        };

        return root.ToString(indented ? Formatting.Indented : Formatting.None).Replace("\r\n", "\n") + (indented ? "\n" : string.Empty);
    }

    public static StarterKitBuildConfig FromJson(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        JObject root = JObject.Parse(json);
        return new StarterKitBuildConfig
        {
            RevitYear = (string?)root["revitYear"] ?? "2024",
            StarterKitSpecDirectory = (string?)root["starterKitSpecDirectory"] ?? string.Empty,
            StandardsDirectory = EmptyToNull((string?)root["standardsDirectory"]),
            FamilySeedTemplatesDirectory = (string?)root["familySeedTemplatesDirectory"] ?? string.Empty,
            ProjectSeedTemplatePath = EmptyToNull((string?)root["projectSeedTemplatePath"]),
            ProjectSeedTemplatesDirectory = EmptyToNull((string?)root["projectSeedTemplatesDirectory"]),
            OutputDirectory = (string?)root["outputDirectory"] ?? string.Empty,
            SharedParametersFilePath = EmptyToNull((string?)root["sharedParametersFilePath"]),
            IncludeOptionalFamilies = (bool?)root["includeOptionalFamilies"] ?? true,
            OverwriteExisting = (bool?)root["overwriteExisting"] ?? true,
            PreferJapaneseSeedTemplates = (bool?)root["preferJapaneseSeedTemplates"] ?? false,
            TriangleComparisonMode = (string?)root["triangleComparisonMode"] ?? "WithinBudget",
        };
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}

/// <summary>Compact per-family summary embedded inside <see cref="StarterKitBuildVerificationReport"/>.</summary>
public sealed class StarterKitFamilyVerificationSummary
{
    public StarterKitFamilyVerificationSummary(
        string familyId,
        string familyName,
        string relativePath,
        long fileSizeBytes,
        bool passed,
        int errorCount,
        int warningCount,
        int coarseTriangles,
        int mediumFineTriangles)
    {
        FamilyId = familyId ?? string.Empty;
        FamilyName = familyName ?? string.Empty;
        RelativePath = relativePath ?? string.Empty;
        FileSizeBytes = fileSizeBytes;
        Passed = passed;
        ErrorCount = errorCount;
        WarningCount = warningCount;
        CoarseTriangles = coarseTriangles;
        MediumFineTriangles = mediumFineTriangles;
    }

    public string FamilyId { get; }

    public string FamilyName { get; }

    public string RelativePath { get; }

    public long FileSizeBytes { get; }

    public bool Passed { get; }

    public int ErrorCount { get; }

    public int WarningCount { get; }

    public int CoarseTriangles { get; }

    public int MediumFineTriangles { get; }
}

/// <summary>
/// Structured verification gate report returned by <c>ManifestDumper</c> and <c>StarterKitBuildEntryPoint</c>,
/// serialized as <c>manifest-diff.json</c> / <c>build-report.json</c> and enforced by <c>Build-StarterKit.ps1</c>.
/// </summary>
public sealed class StarterKitBuildVerificationReport
{
    public const string SchemaName = "kanizu.starter-kit.verification-report";
    public const int SchemaVersion = 1;

    public StarterKitBuildVerificationReport(
        string kitVersion,
        string revitYear,
        string templateFileName,
        long templateFileSizeBytes,
        int maxRteFileSizeMb,
        string expectedManifestSha256,
        string actualManifestSha256,
        ManifestComparisonResult manifestComparison,
        IReadOnlyList<StarterKitFamilyVerificationSummary> familySummaries,
        bool zeroIdentityVerified,
        bool zeroWorksetsVerified,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<string>? log = null)
    {
        KitVersion = kitVersion ?? string.Empty;
        RevitYear = revitYear ?? string.Empty;
        TemplateFileName = templateFileName ?? string.Empty;
        TemplateFileSizeBytes = Math.Max(0L, templateFileSizeBytes);
        MaxRteFileSizeMb = Math.Max(1, maxRteFileSizeMb);
        ExpectedManifestSha256 = expectedManifestSha256 ?? string.Empty;
        ActualManifestSha256 = actualManifestSha256 ?? string.Empty;
        ManifestComparison = manifestComparison ?? throw new ArgumentNullException(nameof(manifestComparison));
        FamilySummaries = Array.AsReadOnly(
            (familySummaries ?? Array.Empty<StarterKitFamilyVerificationSummary>())
                .OrderBy(f => f.FamilyId, StringComparer.Ordinal)
                .ToArray());
        ZeroIdentityVerified = zeroIdentityVerified;
        ZeroWorksetsVerified = zeroWorksetsVerified;
        Errors = Array.AsReadOnly((errors ?? Array.Empty<string>()).ToArray());
        Log = Array.AsReadOnly((log ?? Array.Empty<string>()).ToArray());

        RfaLintErrorCount = FamilySummaries.Sum(f => f.ErrorCount);
        RfaLintWarningCount = FamilySummaries.Sum(f => f.WarningCount);
        FileSizeBudgetPassed = TemplateFileSizeBytes <= (long)MaxRteFileSizeMb * 1024L * 1024L;
        Succeeded = ManifestComparison.IsMatch
            && RfaLintErrorCount == 0
            && FamilySummaries.All(f => f.Passed)
            && FileSizeBudgetPassed
            && ZeroIdentityVerified
            && ZeroWorksetsVerified
            && Errors.Count == 0;
    }

    public string KitVersion { get; }

    public string RevitYear { get; }

    public string TemplateFileName { get; }

    public long TemplateFileSizeBytes { get; }

    public int MaxRteFileSizeMb { get; }

    public bool FileSizeBudgetPassed { get; }

    public string ExpectedManifestSha256 { get; }

    public string ActualManifestSha256 { get; }

    public ManifestComparisonResult ManifestComparison { get; }

    public int DiffCount => ManifestComparison.Findings.Count;

    public bool IsMatch => ManifestComparison.IsMatch;

    public IReadOnlyList<StarterKitFamilyVerificationSummary> FamilySummaries { get; }

    public int RfaLintErrorCount { get; }

    public int RfaLintWarningCount { get; }

    public bool ZeroIdentityVerified { get; }

    public bool ZeroWorksetsVerified { get; }

    public IReadOnlyList<string> Errors { get; }

    public IReadOnlyList<string> Log { get; }

    public bool Succeeded { get; }

    /// <summary>Serializes this verification report to canonical sorted UTF-8 JSON (LF line endings).</summary>
    public string ToCanonicalJson(bool indented = true)
    {
        JObject root = new()
        {
            ["schema"] = SchemaName,
            ["schemaVersion"] = SchemaVersion,
            ["succeeded"] = Succeeded,
            ["isMatch"] = IsMatch,
            ["kitVersion"] = KitVersion,
            ["revitYear"] = RevitYear,
            ["templateFileName"] = TemplateFileName,
            ["templateFileSizeBytes"] = TemplateFileSizeBytes,
            ["maxRteFileSizeMb"] = MaxRteFileSizeMb,
            ["fileSizeBudgetPassed"] = FileSizeBudgetPassed,
            ["zeroIdentityVerified"] = ZeroIdentityVerified,
            ["zeroWorksetsVerified"] = ZeroWorksetsVerified,
            ["expectedManifestSha256"] = ExpectedManifestSha256,
            ["actualManifestSha256"] = ActualManifestSha256,
            ["diffCount"] = DiffCount,
            ["rfaCount"] = FamilySummaries.Count,
            ["rfaLintErrorCount"] = RfaLintErrorCount,
            ["rfaLintWarningCount"] = RfaLintWarningCount,
            ["diffs"] = new JArray(ManifestComparison.Findings.Select(f => new JObject
            {
                ["kind"] = f.Kind.ToString(),
                ["section"] = f.Section,
                ["key"] = f.Key,
                ["property"] = f.Property,
                ["expected"] = f.ExpectedValue,
                ["actual"] = f.ActualValue,
                ["message"] = f.Message,
            })),
            ["families"] = new JArray(FamilySummaries.Select(f => new JObject
            {
                ["familyId"] = f.FamilyId,
                ["familyName"] = f.FamilyName,
                ["relativePath"] = f.RelativePath,
                ["fileSizeBytes"] = f.FileSizeBytes,
                ["passed"] = f.Passed,
                ["errorCount"] = f.ErrorCount,
                ["warningCount"] = f.WarningCount,
                ["coarseTriangles"] = f.CoarseTriangles,
                ["mediumFineTriangles"] = f.MediumFineTriangles,
            })),
            ["errors"] = new JArray(Errors),
        };

        JToken sorted = CanonicalJson.SortKeys(root);
        return sorted.ToString(indented ? Formatting.Indented : Formatting.None).Replace("\r\n", "\n") + (indented ? "\n" : string.Empty);
    }

    /// <summary>Parses a serialized <see cref="StarterKitBuildVerificationReport"/> JSON string.</summary>
    public static StarterKitBuildVerificationReport FromJson(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        JObject root = JObject.Parse(json);
        string schema = (string?)root["schema"] ?? string.Empty;
        if (!string.Equals(schema, SchemaName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected verification report schema '{SchemaName}', found '{schema}'.");
        }

        List<ManifestDiffFinding> diffs = new();
        if (root["diffs"] is JArray diffArray)
        {
            foreach (JToken token in diffArray)
            {
                string kindText = (string?)token["kind"] ?? "Mismatch";
                if (!Enum.TryParse(kindText, ignoreCase: true, out ManifestDiffKind kind))
                {
                    kind = ManifestDiffKind.Mismatch;
                }

                diffs.Add(new ManifestDiffFinding(
                    kind,
                    (string?)token["section"] ?? string.Empty,
                    (string?)token["key"] ?? string.Empty,
                    (string?)token["property"] ?? string.Empty,
                    (string?)token["expected"] ?? string.Empty,
                    (string?)token["actual"] ?? string.Empty,
                    (string?)token["message"] ?? string.Empty));
            }
        }

        List<StarterKitFamilyVerificationSummary> families = new();
        if (root["families"] is JArray famArray)
        {
            foreach (JToken token in famArray)
            {
                families.Add(new StarterKitFamilyVerificationSummary(
                    (string?)token["familyId"] ?? string.Empty,
                    (string?)token["familyName"] ?? string.Empty,
                    (string?)token["relativePath"] ?? string.Empty,
                    (long?)token["fileSizeBytes"] ?? 0L,
                    (bool?)token["passed"] ?? false,
                    (int?)token["errorCount"] ?? 0,
                    (int?)token["warningCount"] ?? 0,
                    (int?)token["coarseTriangles"] ?? 0,
                    (int?)token["mediumFineTriangles"] ?? 0));
            }
        }

        List<string> errors = new();
        if (root["errors"] is JArray errArray)
        {
            foreach (JToken token in errArray)
            {
                string text = (string?)token ?? string.Empty;
                if (text.Length > 0)
                {
                    errors.Add(text);
                }
            }
        }

        return new StarterKitBuildVerificationReport(
            (string?)root["kitVersion"] ?? string.Empty,
            (string?)root["revitYear"] ?? string.Empty,
            (string?)root["templateFileName"] ?? string.Empty,
            (long?)root["templateFileSizeBytes"] ?? 0L,
            (int?)root["maxRteFileSizeMb"] ?? 25,
            (string?)root["expectedManifestSha256"] ?? string.Empty,
            (string?)root["actualManifestSha256"] ?? string.Empty,
            new ManifestComparisonResult(diffs),
            families,
            (bool?)root["zeroIdentityVerified"] ?? false,
            (bool?)root["zeroWorksetsVerified"] ?? false,
            errors);
    }
}

/// <summary>Role of a single staged file inside a versioned Starter Kit <c>.zip</c> release archive.</summary>
public enum StarterKitPackageEntryKind
{
    TemplateRte,
    FamilyRfa,
    ManifestJson,
    ManifestDiffJson,
    ChecksumsTxt,
    ReadmeMarkdown,
    StartHereTxt,
}

/// <summary>One deterministic entry inside a versioned Kanizu Starter Kit <c>.zip</c> archive.</summary>
public sealed class StarterKitPackageEntry
{
    public StarterKitPackageEntry(
        string archiveRelativePath,
        StarterKitPackageEntryKind kind,
        bool required,
        bool includeInSha256Sums,
        string? familyId = null)
    {
        ArchiveRelativePath = (archiveRelativePath ?? string.Empty).Replace('\\', '/');
        Kind = kind;
        Required = required;
        IncludeInSha256Sums = includeInSha256Sums;
        FamilyId = familyId;
    }

    public string ArchiveRelativePath { get; }

    public StarterKitPackageEntryKind Kind { get; }

    public bool Required { get; }

    public bool IncludeInSha256Sums { get; }

    public string? FamilyId { get; }
}

/// <summary>Complete deterministic staging and <c>.zip</c> packaging plan for one Revit version.</summary>
public sealed class StarterKitPackagePlan
{
    public StarterKitPackagePlan(
        string kitVersion,
        string revitYear,
        string zipFileName,
        string templateFileName,
        int maxRteFileSizeMb,
        int maxRfaFileSizeKb,
        IReadOnlyList<StarterKitPackageEntry> entries)
    {
        KitVersion = kitVersion ?? string.Empty;
        RevitYear = revitYear ?? string.Empty;
        ZipFileName = zipFileName ?? string.Empty;
        TemplateFileName = templateFileName ?? string.Empty;
        MaxRteFileSizeMb = maxRteFileSizeMb;
        MaxRfaFileSizeKb = maxRfaFileSizeKb;
        Entries = Array.AsReadOnly(
            (entries ?? Array.Empty<StarterKitPackageEntry>())
                .OrderBy(e => e.ArchiveRelativePath, StringComparer.Ordinal)
                .ToArray());
    }

    public string KitVersion { get; }

    public string RevitYear { get; }

    public string ZipFileName { get; }

    public string TemplateFileName { get; }

    public int MaxRteFileSizeMb { get; }

    public int MaxRfaFileSizeKb { get; }

    public IReadOnlyList<StarterKitPackageEntry> Entries { get; }

    public IReadOnlyList<StarterKitPackageEntry> ChecksumEntries =>
        Array.AsReadOnly(Entries.Where(e => e.IncludeInSha256Sums).ToArray());
}

/// <summary>Validation result for a staged Starter Kit directory prior to creating the release <c>.zip</c>.</summary>
public sealed class StarterKitPackageValidationResult
{
    public StarterKitPackageValidationResult(IReadOnlyList<string> errors)
    {
        Errors = Array.AsReadOnly((errors ?? Array.Empty<string>()).ToArray());
    }

    public IReadOnlyList<string> Errors { get; }

    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Pure-C# planner and validator for versioned Kanizu Starter Kit <c>.zip</c> release packages
/// (<c>Kanizu-Revit-StarterKit-v&lt;version&gt;-Revit&lt;year&gt;.zip</c>).
/// </summary>
public static class StarterKitPackagePlanner
{
    public const string FamiliesFolderName = "Families";
    public const string ManifestFileName = "manifest.json";
    public const string ManifestDiffFileName = "manifest-diff.json";
    public const string Sha256SumsFileName = "SHA256SUMS.txt";
    public const string ReadmeFileName = "README.md";
    public const string StartHereFileName = "START_HERE.txt";

    private static readonly Regex SemVerRegex = new(
        @"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex RevitYearRegex = new(
        @"^20[2-9][0-9]$",
        RegexOptions.CultureInvariant);

    /// <summary>Formats the canonical versioned <c>.zip</c> archive filename.</summary>
    public static string GetZipFileName(string kitVersion, string revitYear)
    {
        if (string.IsNullOrWhiteSpace(kitVersion) || !SemVerRegex.IsMatch(kitVersion.Trim()))
        {
            throw new ArgumentException($"Invalid Starter Kit SemVer '{kitVersion}'.", nameof(kitVersion));
        }

        if (string.IsNullOrWhiteSpace(revitYear) || !RevitYearRegex.IsMatch(revitYear.Trim()))
        {
            throw new ArgumentException($"Invalid Revit year '{revitYear}'.", nameof(revitYear));
        }

        return $"Kanizu-Revit-StarterKit-v{kitVersion.Trim()}-Revit{revitYear.Trim()}.zip";
    }

    /// <summary>Returns the canonical <c>.rte</c> filename declared by <paramref name="spec"/>.</summary>
    public static string GetTemplateFileName(StarterKitSpec spec)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        string name = (spec.Rte.TemplateName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Starter Kit rte.json templateName cannot be empty.");
        }

        return name.EndsWith(".rte", StringComparison.OrdinalIgnoreCase) ? name : name + ".rte";
    }

    /// <summary>Builds a deterministic, sorted packaging plan for <paramref name="revitYear"/>.</summary>
    public static StarterKitPackagePlan PlanPackage(
        StarterKitSpec spec,
        string revitYear,
        bool includeOptionalFamilies = true)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        string trimmedYear = (revitYear ?? string.Empty).Trim();
        if (!spec.Header.SupportedRevitVersions.Contains(trimmedYear, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Revit year '{trimmedYear}' is not listed in kit.json supportedRevitVersions ({string.Join(", ", spec.Header.SupportedRevitVersions)}).",
                nameof(revitYear));
        }

        string zipName = GetZipFileName(spec.Header.KitVersion, trimmedYear);
        string templateFileName = GetTemplateFileName(spec);

        List<StarterKitPackageEntry> entries = new()
        {
            new StarterKitPackageEntry(templateFileName, StarterKitPackageEntryKind.TemplateRte, required: true, includeInSha256Sums: true),
            new StarterKitPackageEntry(ManifestFileName, StarterKitPackageEntryKind.ManifestJson, required: true, includeInSha256Sums: true),
            new StarterKitPackageEntry(ManifestDiffFileName, StarterKitPackageEntryKind.ManifestDiffJson, required: true, includeInSha256Sums: true),
            new StarterKitPackageEntry(Sha256SumsFileName, StarterKitPackageEntryKind.ChecksumsTxt, required: true, includeInSha256Sums: false),
            new StarterKitPackageEntry(ReadmeFileName, StarterKitPackageEntryKind.ReadmeMarkdown, required: true, includeInSha256Sums: true),
            new StarterKitPackageEntry(StartHereFileName, StarterKitPackageEntryKind.StartHereTxt, required: true, includeInSha256Sums: true),
        };

        IReadOnlyList<FamilyGeometrySpec> families = StarterKitCatalogSelector.SelectCatalogSpecs(
            spec,
            includeOptionalFamilies);
        foreach (FamilyGeometrySpec family in families)
        {
            entries.Add(new StarterKitPackageEntry(
                $"{FamiliesFolderName}/{family.FamilyName}.rfa",
                StarterKitPackageEntryKind.FamilyRfa,
                required: true,
                includeInSha256Sums: true,
                familyId: family.Id));
        }

        return new StarterKitPackagePlan(
            spec.Header.KitVersion,
            trimmedYear,
            zipName,
            templateFileName,
            spec.Header.Budgets.MaxRteFileSizeMb,
            spec.Header.Budgets.MaxRfaFileSizeKb,
            entries);
    }

    /// <summary>
    /// Validates a staged artifact map (relative POSIX path -&gt; file size in bytes) plus a
    /// <see cref="StarterKitBuildVerificationReport"/> before zip creation. Rejects missing required files,
    /// budget violations, non-zero manifest diffs, or non-zero <c>RfaLinter</c> errors.
    /// </summary>
    public static StarterKitPackageValidationResult ValidateStagedArtifacts(
        StarterKitPackagePlan plan,
        IReadOnlyDictionary<string, long> stagedFileSizesByRelativePath,
        StarterKitBuildVerificationReport? verificationReport)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        if (stagedFileSizesByRelativePath is null)
        {
            throw new ArgumentNullException(nameof(stagedFileSizesByRelativePath));
        }

        Dictionary<string, long> normalized = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, long> kv in stagedFileSizesByRelativePath)
        {
            string key = (kv.Key ?? string.Empty).Replace('\\', '/').TrimStart('/');
            if (key.Length > 0 && !normalized.ContainsKey(key))
            {
                normalized[key] = kv.Value;
            }
        }

        List<string> errors = new();
        long maxRteBytes = (long)plan.MaxRteFileSizeMb * 1024L * 1024L;
        long maxRfaBytes = (long)plan.MaxRfaFileSizeKb * 1024L;

        foreach (StarterKitPackageEntry entry in plan.Entries)
        {
            if (!entry.Required)
            {
                continue;
            }

            if (!normalized.TryGetValue(entry.ArchiveRelativePath, out long sizeBytes))
            {
                errors.Add($"Missing required package artifact '{entry.ArchiveRelativePath}'.");
                continue;
            }

            if (sizeBytes <= 0)
            {
                errors.Add($"Artifact '{entry.ArchiveRelativePath}' is empty (0 bytes).");
                continue;
            }

            if (entry.Kind == StarterKitPackageEntryKind.TemplateRte && sizeBytes > maxRteBytes)
            {
                errors.Add($"Template '{entry.ArchiveRelativePath}' ({sizeBytes} bytes) exceeds maxRteFileSizeMb ({plan.MaxRteFileSizeMb} MB).");
            }

            if (entry.Kind == StarterKitPackageEntryKind.FamilyRfa && sizeBytes > maxRfaBytes)
            {
                errors.Add($"Family '{entry.ArchiveRelativePath}' ({sizeBytes} bytes) exceeds maxRfaFileSizeKb ({plan.MaxRfaFileSizeKb} KB).");
            }
        }

        if (verificationReport is null)
        {
            errors.Add("Missing build verification report (manifest-diff.json).");
        }
        else
        {
            if (!verificationReport.IsMatch || verificationReport.DiffCount > 0)
            {
                errors.Add($"Manifest comparison failed with {verificationReport.DiffCount} diff(s).");
            }

            if (verificationReport.RfaLintErrorCount > 0)
            {
                errors.Add($"RfaLinter reported {verificationReport.RfaLintErrorCount} blocking error(s).");
            }

            if (!verificationReport.ZeroIdentityVerified)
            {
                errors.Add("Template failed zero-project-identity verification (KZ-TPL-001/002).");
            }

            if (!verificationReport.ZeroWorksetsVerified)
            {
                errors.Add("Template failed zero-workset verification.");
            }

            if (!verificationReport.Succeeded)
            {
                foreach (string err in verificationReport.Errors)
                {
                    errors.Add(err);
                }
            }
        }

        return new StarterKitPackageValidationResult(errors);
    }

    /// <summary>Generates deterministic bilingual EN/JA <c>README.md</c> content for the packaged release.</summary>
    public static string GenerateReadmeMarkdown(TemplateBuildPlan plan, string revitYear)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        StringBuilder sb = new();
        sb.Append("# Kanizu Revit Starter Kit v").Append(plan.KitVersion)
            .Append(" (Revit ").Append(revitYear).Append(")\n\n");
        sb.Append("Kanizu GIS Standard v").Append(plan.GisStandardVersion.ToString(CultureInfo.InvariantCulture))
            .Append(" — Metric Station Authoring Template & Lightweight Family Library\n\n");
        sb.Append("Kanizu GIS 標準 v").Append(plan.GisStandardVersion.ToString(CultureInfo.InvariantCulture))
            .Append(" — メートル単位駅モデル作成用テンプレート＆軽量ファミリライブラリ\n\n");

        sb.Append("## Contents / 収録内容\n\n");
        sb.Append("- `").Append(plan.TemplateName).Append(".rte` — Zero-identity starter project template (metric mm / m²)\n");
        sb.Append("- `Families/*.rfa` — ").Append(plan.LoadedFamilies.Count).Append(" pre-linted dual-LOD (Coarse `K_Footprint` + Medium/Fine low-poly 3D) families\n");
        sb.Append("- `manifest.json` — Canonical semantic build manifest\n");
        sb.Append("- `manifest-diff.json` — Verification report confirming zero diffs and zero `RfaLinter` errors\n");
        sb.Append("- `SHA256SUMS.txt` — SHA-256 checksums of all release artifacts\n");
        sb.Append("- `START_HERE.txt` — Bilingual EN/JA quick-start checklist matching the `00_Start_Here` Starting View\n\n");

        sb.Append("## View Templates / ビューテンプレート\n\n");
        foreach (TemplateViewTemplatePlan vt in plan.ViewTemplates)
        {
            sb.Append("- **").Append(vt.Name).Append("** (`").Append(vt.ViewType).Append("`, `")
                .Append(vt.DetailLevel).Append("`)");
            if (vt.IsCanonical2DExport)
            {
                sb.Append(" — Canonical 2D GIS export template");
            }

            sb.Append('\n');
        }

        sb.Append("\n## QA Schedules / QA 集計表\n\n");
        foreach (TemplateSchedulePlan sched in plan.Schedules)
        {
            sb.Append("- **").Append(sched.Name).Append("** (")
                .Append(string.IsNullOrEmpty(sched.Category) ? "Multi-Category" : sched.Category)
                .Append(sched.EmptyMeansPass ? ", empty = PASS" : ", review")
                .Append(")\n");
        }

        sb.Append("\n## Loaded Families / 収録ファミリ\n\n");
        foreach (TemplateLoadedFamilyPlan fam in plan.LoadedFamilies)
        {
            sb.Append("- `Families/").Append(fam.RfaFileName).Append("` (`").Append(fam.Category).Append("`)\n");
        }

        return sb.ToString();
    }

    /// <summary>Generates deterministic bilingual EN/JA <c>START_HERE.txt</c> matching <c>00_Start_Here</c>.</summary>
    public static string GenerateStartHereText(TemplateBuildPlan plan)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        return plan.StartingView.RenderBilingualText().Replace("\r\n", "\n") + "\n";
    }
}
