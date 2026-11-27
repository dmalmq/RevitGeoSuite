using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;

namespace RevitGeoSuite.FloorPlanExport.StarterKit;

/// <summary>Full outcome of executing <see cref="StarterKitBuildEntryPoint.Run"/>.</summary>
public sealed class StarterKitBuildRunResult
{
    public StarterKitBuildRunResult(
        StarterKitCatalogBuildResult catalogResult,
        TemplateBuildResult templateResult,
        ExpectedManifest actualManifest,
        StarterKitBuildVerificationReport verificationReport,
        string outputDirectory,
        string manifestPath,
        string manifestDiffPath,
        string buildReportPath)
    {
        CatalogResult = catalogResult ?? throw new ArgumentNullException(nameof(catalogResult));
        TemplateResult = templateResult ?? throw new ArgumentNullException(nameof(templateResult));
        ActualManifest = actualManifest ?? throw new ArgumentNullException(nameof(actualManifest));
        VerificationReport = verificationReport ?? throw new ArgumentNullException(nameof(verificationReport));
        OutputDirectory = outputDirectory ?? string.Empty;
        ManifestPath = manifestPath ?? string.Empty;
        ManifestDiffPath = manifestDiffPath ?? string.Empty;
        BuildReportPath = buildReportPath ?? string.Empty;
    }

    public StarterKitCatalogBuildResult CatalogResult { get; }

    public TemplateBuildResult TemplateResult { get; }

    public ExpectedManifest ActualManifest { get; }

    public StarterKitBuildVerificationReport VerificationReport { get; }

    public string OutputDirectory { get; }

    public string ManifestPath { get; }

    public string ManifestDiffPath { get; }

    public string BuildReportPath { get; }

    public bool Succeeded => VerificationReport.Succeeded;
}

/// <summary>
/// Batch/headless orchestration entry point callable from <c>scripts/Build-StarterKit.ps1</c> or
/// <see cref="StarterKitBuildCommand"/>. Executes the full Starter Kit pipeline:
/// <c>StarterKitSpecLoader</c> -&gt; <c>StarterKitFamilyCatalogBuilder</c> -&gt; <c>TemplateBuilder</c>
/// -&gt; reopened <c>ManifestDumper.DumpFile</c> -&gt; <c>StarterKitManifestComparer</c> -&gt;
/// staged <c>manifest.json</c>, <c>manifest-diff.json</c>, <c>build-report.json</c>,
/// <c>README.md</c>, and <c>START_HERE.txt</c>.
/// </summary>
public static class StarterKitBuildEntryPoint
{
    public const string BuildReportFileName = "build-report.json";

    /// <summary>Loads a JSON <see cref="StarterKitBuildConfig"/> from disk and executes the full build.</summary>
    public static StarterKitBuildRunResult RunFromConfigFile(Application application, string configFilePath)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (string.IsNullOrWhiteSpace(configFilePath) || !File.Exists(configFilePath))
        {
            throw new FileNotFoundException($"Starter Kit build config file not found at '{configFilePath}'.", configFilePath);
        }

        string json = File.ReadAllText(configFilePath, Encoding.UTF8);
        StarterKitBuildConfig config = StarterKitBuildConfig.FromJson(json);
        return Run(application, config);
    }

    /// <summary>Executes the full Starter Kit build, reopen verification, and artifact emission.</summary>
    public static StarterKitBuildRunResult Run(Application application, StarterKitBuildConfig config)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (string.IsNullOrWhiteSpace(config.OutputDirectory))
        {
            throw new ArgumentException("StarterKitBuildConfig.OutputDirectory is required.", nameof(config));
        }

        if (string.IsNullOrWhiteSpace(config.FamilySeedTemplatesDirectory))
        {
            throw new ArgumentException("StarterKitBuildConfig.FamilySeedTemplatesDirectory is required.", nameof(config));
        }

        string outputDir = Path.GetFullPath(config.OutputDirectory);
        string familiesDir = Path.Combine(outputDir, StarterKitPackagePlanner.FamiliesFolderName);
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(familiesDir);

        // 1. Load & validate Starter Kit declarative specs.
        StarterKitLoadResult loadResult = string.IsNullOrWhiteSpace(config.StarterKitSpecDirectory)
            ? StarterKitSpecLoader.LoadEmbedded()
            : StarterKitSpecLoader.LoadFromDirectory(config.StarterKitSpecDirectory, config.StandardsDirectory);

        if (!loadResult.IsValid || loadResult.Spec is null)
        {
            string summary = string.Join("; ", loadResult.Validation.Errors.Select(e => e.ToString()));
            throw new InvalidOperationException($"Starter Kit specification validation failed: {summary}");
        }

        StarterKitSpec spec = loadResult.Spec;
        StarterKitReferenceStandards refStandards = !string.IsNullOrWhiteSpace(config.StandardsDirectory)
            ? StarterKitReferenceStandards.LoadFromDirectory(config.StandardsDirectory!)
            : StarterKitReferenceStandards.LoadEmbedded();

        // 2. Build & lint all Starter Kit .rfa families.
        FamilyBuildOptions familyOptions = new(
            config.FamilySeedTemplatesDirectory,
            familiesDir,
            config.SharedParametersFilePath,
            config.OverwriteExisting,
            lintBeforeReturn: true);

        StarterKitCatalogBuildOptions catalogOptions = new()
        {
            IncludeOptionalFamilies = config.IncludeOptionalFamilies,
        };

        StarterKitCatalogBuildResult catalogResult = StarterKitFamilyCatalogBuilder.BuildCatalog(
            application,
            spec,
            familyOptions,
            catalogOptions,
            refStandards);

        // 3. Build & save the Starter Kit .rte template with all families loaded.
        string rteFileName = StarterKitPackagePlanner.GetTemplateFileName(spec);
        string rteOutputPath = Path.Combine(outputDir, rteFileName);
        TemplateBuildOptions templateOptions = new(
            rteOutputPath,
            config.ProjectSeedTemplatePath,
            config.ProjectSeedTemplatesDirectory,
            familiesDir,
            config.SharedParametersFilePath,
            config.OverwriteExisting,
            config.IncludeOptionalFamilies,
            config.PreferJapaneseSeedTemplates);

        TemplateBuildResult templateResult = TemplateBuilder.BuildTemplate(
            application,
            spec,
            templateOptions,
            refStandards,
            catalogResult.Results);

        // 4. Reopen the saved .rte from disk, dump manifest, and compare against ExpectedManifest.
        ExpectedManifest actualManifest = ManifestDumper.DumpFile(
            application,
            rteOutputPath,
            spec,
            catalogResult.ManifestEntries,
            refStandards);

        List<string> combinedLog = new();
        combinedLog.AddRange(templateResult.Log);
        combinedLog.AddRange(templateResult.Warnings.Select(w => "WARNING: " + w));

        ManifestCompareOptions compareOptions = new()
        {
            TriangleMode = config.ResolveTriangleMode(),
        };

        StarterKitBuildVerificationReport report = ManifestDumper.Verify(
            actualManifest,
            spec,
            config.RevitYear,
            rteOutputPath,
            config.IncludeOptionalFamilies,
            compareOptions,
            catalogResult,
            refStandards,
            extraErrors: null,
            log: combinedLog);

        // 5. Write deterministic JSON & bilingual documentation artifacts.
        UTF8Encoding utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
        string manifestPath = Path.Combine(outputDir, StarterKitPackagePlanner.ManifestFileName);
        string manifestDiffPath = Path.Combine(outputDir, StarterKitPackagePlanner.ManifestDiffFileName);
        string buildReportPath = Path.Combine(outputDir, BuildReportFileName);
        string readmePath = Path.Combine(outputDir, StarterKitPackagePlanner.ReadmeFileName);
        string startHerePath = Path.Combine(outputDir, StarterKitPackagePlanner.StartHereFileName);

        File.WriteAllText(manifestPath, actualManifest.ToCanonicalJson(indented: true), utf8NoBom);
        string reportJson = report.ToCanonicalJson(indented: true);
        File.WriteAllText(manifestDiffPath, reportJson, utf8NoBom);
        File.WriteAllText(buildReportPath, reportJson, utf8NoBom);
        File.WriteAllText(readmePath, StarterKitPackagePlanner.GenerateReadmeMarkdown(templateResult.Plan, config.RevitYear), utf8NoBom);
        File.WriteAllText(startHerePath, StarterKitPackagePlanner.GenerateStartHereText(templateResult.Plan), utf8NoBom);

        return new StarterKitBuildRunResult(
            catalogResult,
            templateResult,
            actualManifest,
            report,
            outputDir,
            manifestPath,
            manifestDiffPath,
            buildReportPath);
    }
}

/// <summary>
/// Revit <see cref="IExternalCommand"/> hook invoked by <c>scripts/Build-StarterKit.ps1</c> (via
/// <see cref="StarterKitBuildConfig.DefaultConfigEnvironmentVariable"/>) to execute the full T8 build
/// and verification pipeline inside Revit 2024 or Revit 2027.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class StarterKitBuildCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (commandData?.Application?.Application is null)
        {
            message = "Revit Application context is unavailable.";
            return Result.Failed;
        }

        string? configPath = Environment.GetEnvironmentVariable(StarterKitBuildConfig.DefaultConfigEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configPath))
        {
            message = $"Environment variable '{StarterKitBuildConfig.DefaultConfigEnvironmentVariable}' must point to a StarterKitBuildConfig JSON file.";
            return Result.Failed;
        }

        try
        {
            StarterKitBuildRunResult result = StarterKitBuildEntryPoint.RunFromConfigFile(
                commandData.Application.Application,
                configPath!);

            if (!result.Succeeded)
            {
                message = $"Starter Kit build verification failed: {result.VerificationReport.DiffCount} manifest diff(s), " +
                          $"{result.VerificationReport.RfaLintErrorCount} RfaLinter error(s). See '{result.BuildReportPath}'.";
                return Result.Failed;
            }

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = $"Starter Kit build failed: {ex.Message}";
            return Result.Failed;
        }
    }
}
