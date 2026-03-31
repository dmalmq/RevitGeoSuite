using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Validation;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.Validation;

public sealed class ProjectHealthChecker
{
    private readonly CoordinateValidator coordinateValidator;

    public ProjectHealthChecker(CoordinateValidator coordinateValidator)
    {
        this.coordinateValidator = coordinateValidator ?? throw new ArgumentNullException(nameof(coordinateValidator));
    }

    public ProjectHealthSummary BuildSummary(CurrentProjectStateSummary currentState, GeoProjectInfo? info)
    {
        if (currentState is null)
        {
            throw new ArgumentNullException(nameof(currentState));
        }

        List<ValidationResult> findings = BuildFindings(currentState, info);
        ExportReadinessSummary exportReadiness = new ExportReadinessChecker().Build(currentState, info, findings);

        return new ProjectHealthSummary
        {
            DocumentTitle = string.IsNullOrWhiteSpace(currentState.DocumentTitle) ? "Active Revit Project" : currentState.DocumentTitle,
            StatusTitle = BuildStatusTitle(findings),
            StatusMessage = BuildStatusMessage(currentState, findings, exportReadiness),
            DetailRows = BuildDetailRows(currentState, info),
            Findings = findings,
            ExportReadiness = exportReadiness
        };
    }

    private List<ValidationResult> BuildFindings(CurrentProjectStateSummary currentState, GeoProjectInfo? info)
    {
        List<ValidationResult> findings = new List<ValidationResult>();

        if (!currentState.IsSupportedDocument)
        {
            AddFinding(findings, new ValidationResult(
                "unsupported-document",
                ValidationSeverity.Error,
                string.IsNullOrWhiteSpace(currentState.StatusMessage)
                    ? "This Revit document is not supported by the validation workflow."
                    : currentState.StatusMessage));
            return findings;
        }

        if (currentState.IsReadOnly)
        {
            AddFinding(findings, new ValidationResult(
                "read-only-document",
                ValidationSeverity.Info,
                "This Revit project is read-only. Project checks remain available, but write workflows require an editable model."));
        }

        if (info is null)
        {
            AddFinding(findings, new ValidationResult(
                "missing-info",
                ValidationSeverity.Error,
                "Shared geo metadata is missing. Run Georeference Setup before relying on downstream mesh, import, or export workflows."));
            return findings;
        }

        if (info.ProjectCrs is null)
        {
            AddFinding(findings, new ValidationResult("missing-crs", ValidationSeverity.Error, "CRS reference is missing from GeoProjectInfo."));
        }

        if (info.Origin is null)
        {
            AddFinding(findings, new ValidationResult("missing-origin", ValidationSeverity.Error, "Canonical origin is missing from GeoProjectInfo."));
        }

        if (info.Confidence == GeoConfidenceLevel.Unknown)
        {
            AddFinding(findings, new ValidationResult(
                "unknown-confidence",
                ValidationSeverity.Warning,
                "Confidence is still Unknown. Record whether the georeference came from approximate map selection or verified coordinates."));
        }

        if (info.PrimaryMeshCode is null)
        {
            AddFinding(findings, new ValidationResult(
                "missing-primary-mesh",
                ValidationSeverity.Info,
                "Primary mesh code is not stored yet. Mesh Inspector can compute and save it as a shared convenience key."));
        }

        foreach (ValidationResult result in coordinateValidator.Validate(info))
        {
            ValidationSeverity severity = result.Code switch
            {
                "missing-info" or "missing-crs" or "missing-origin" => ValidationSeverity.Error,
                _ => result.Severity
            };

            AddFinding(findings, new ValidationResult(result.Code, severity, result.Message));
        }

        return findings
            .OrderByDescending(result => result.Severity)
            .ThenBy(result => result.Code, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyCollection<DetailRow> BuildDetailRows(CurrentProjectStateSummary currentState, GeoProjectInfo? info)
    {
        List<DetailRow> rows = new List<DetailRow>
        {
            new DetailRow("Document", string.IsNullOrWhiteSpace(currentState.DocumentTitle) ? "Active Revit Project" : currentState.DocumentTitle),
            new DetailRow("Supported Document", currentState.IsSupportedDocument ? "Yes" : "No"),
            new DetailRow("Read-Only", currentState.IsReadOnly ? "Yes" : "No"),
            new DetailRow("Stored Geo Metadata", currentState.HasStoredGeoInfo ? "Yes" : "No")
        };

        if (info?.ProjectCrs is not null)
        {
            rows.Add(new DetailRow("Stored CRS", $"EPSG:{info.ProjectCrs.EpsgCode}  {info.ProjectCrs.NameSnapshot}"));
        }
        else
        {
            rows.Add(new DetailRow("Stored CRS", "Not stored"));
        }

        if (info?.Origin is not null)
        {
            rows.Add(new DetailRow("Stored Origin", $"{info.Origin.Latitude:F6}, {info.Origin.Longitude:F6}, elev {info.Origin.ElevationMeters:F3} m"));
        }
        else
        {
            rows.Add(new DetailRow("Stored Origin", "Not stored"));
        }

        rows.Add(new DetailRow("Stored Confidence", info is null ? "Not stored" : info.Confidence.ToString()));
        rows.Add(new DetailRow("Stored Primary Mesh", info?.PrimaryMeshCode?.Value ?? "Not stored"));
        rows.Add(new DetailRow("Setup Source", string.IsNullOrWhiteSpace(info?.SetupSource) ? "Not recorded" : info!.SetupSource));
        return rows;
    }

    private static string BuildStatusTitle(IReadOnlyCollection<ValidationResult> findings)
    {
        if (findings.Any(result => result.Severity == ValidationSeverity.Error))
        {
            return "Blocking Issues Found";
        }

        if (findings.Any(result => result.Severity == ValidationSeverity.Warning))
        {
            return "Project Needs Attention";
        }

        return "Project Health Looks Good";
    }

    private static string BuildStatusMessage(CurrentProjectStateSummary currentState, IReadOnlyCollection<ValidationResult> findings, ExportReadinessSummary exportReadiness)
    {
        if (!currentState.IsSupportedDocument)
        {
            return string.IsNullOrWhiteSpace(currentState.StatusMessage)
                ? "This document cannot participate in the current suite workflows."
                : currentState.StatusMessage;
        }

        if (findings.Any(result => result.Severity == ValidationSeverity.Error))
        {
            return "Canonical georeference data is incomplete or invalid. Fix the blocking items below before relying on later mesh, import, or export modules.";
        }

        if (findings.Any(result => result.Severity == ValidationSeverity.Warning))
        {
            return "Core georeference data exists, but some confidence or derived-state checks still need attention before downstream exports.";
        }

        return exportReadiness.Status == ExportReadinessStatus.Ready
            ? "Shared georeference metadata is in a good state for downstream suite workflows."
            : "The project can be checked safely, but some downstream workflows still need attention.";
    }

    private static void AddFinding(ICollection<ValidationResult> findings, ValidationResult candidate)
    {
        ValidationResult? existing = findings.FirstOrDefault(item => string.Equals(item.Code, candidate.Code, StringComparison.Ordinal));
        if (existing is null)
        {
            findings.Add(candidate);
            return;
        }

        if (candidate.Severity > existing.Severity)
        {
            findings.Remove(existing);
            findings.Add(candidate);
        }
    }
}
