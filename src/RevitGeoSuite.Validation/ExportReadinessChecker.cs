using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Validation;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.Validation;

public sealed class ExportReadinessChecker
{
    public ExportReadinessSummary Build(CurrentProjectStateSummary currentState, GeoProjectInfo? info, IReadOnlyCollection<ValidationResult> findings)
    {
        if (currentState is null)
        {
            throw new ArgumentNullException(nameof(currentState));
        }

        IReadOnlyCollection<ValidationResult> effectiveFindings = findings ?? Array.Empty<ValidationResult>();
        bool hasBlockingIssues = !currentState.IsSupportedDocument
            || info?.ProjectCrs is null
            || info?.Origin is null
            || effectiveFindings.Any(result => result.Severity == ValidationSeverity.Error)
            || effectiveFindings.Any(result => string.Equals(result.Code, "invalid-crs", StringComparison.Ordinal));

        bool hasAttentionIssues = effectiveFindings.Any(result => result.Severity == ValidationSeverity.Warning)
            || effectiveFindings.Any(result => string.Equals(result.Code, "missing-primary-mesh", StringComparison.Ordinal))
            || effectiveFindings.Any(result => string.Equals(result.Code, "read-only-document", StringComparison.Ordinal));

        List<ExportReadinessItem> items = new List<ExportReadinessItem>
        {
            new ExportReadinessItem
            {
                Title = "Supported Revit project document",
                IsSatisfied = currentState.IsSupportedDocument,
                Detail = currentState.IsSupportedDocument
                    ? "The current document can participate in suite workflows."
                    : string.IsNullOrWhiteSpace(currentState.StatusMessage)
                        ? "Open a supported Revit project document before running downstream modules."
                        : currentState.StatusMessage
            },
            new ExportReadinessItem
            {
                Title = "Canonical CRS reference",
                IsSatisfied = info?.ProjectCrs is not null && !effectiveFindings.Any(result => string.Equals(result.Code, "invalid-crs", StringComparison.Ordinal)),
                Detail = info?.ProjectCrs is null
                    ? "Run Georeference Setup and save a CRS reference first."
                    : $"EPSG:{info.ProjectCrs.EpsgCode} is available for downstream modules."
            },
            new ExportReadinessItem
            {
                Title = "Canonical origin",
                IsSatisfied = info?.Origin is not null,
                Detail = info?.Origin is null
                    ? "Run Georeference Setup and save the project origin first."
                    : $"{info.Origin.Latitude:F6}, {info.Origin.Longitude:F6} is available as the shared geographic origin."
            },
            new ExportReadinessItem
            {
                Title = "Confidence recorded",
                IsSatisfied = info is not null && info.Confidence != GeoConfidenceLevel.Unknown,
                Detail = info is null || info.Confidence == GeoConfidenceLevel.Unknown
                    ? "Record whether the current setup is approximate or verified before export-heavy workflows."
                    : $"Confidence is stored as {info.Confidence}."
            },
            new ExportReadinessItem
            {
                Title = "Primary mesh key stored",
                IsSatisfied = info?.PrimaryMeshCode is not null && !effectiveFindings.Any(result => string.Equals(result.Code, "stale-mesh", StringComparison.Ordinal)),
                Detail = info?.PrimaryMeshCode is null
                    ? "Mesh Inspector can compute and save the primary mesh code for PLATEAU-oriented workflows."
                    : effectiveFindings.Any(result => string.Equals(result.Code, "stale-mesh", StringComparison.Ordinal))
                        ? "The stored primary mesh code is stale. Recompute it in Mesh Inspector before relying on mesh-based workflows."
                        : $"Stored primary mesh code {info.PrimaryMeshCode.Value} is current."
            }
        };

        ExportReadinessStatus status = hasBlockingIssues
            ? ExportReadinessStatus.Blocked
            : hasAttentionIssues
                ? ExportReadinessStatus.NeedsAttention
                : ExportReadinessStatus.Ready;

        return new ExportReadinessSummary
        {
            Status = status,
            StatusTitle = status switch
            {
                ExportReadinessStatus.Blocked => "Blocked",
                ExportReadinessStatus.NeedsAttention => "Needs Attention",
                _ => "Ready"
            },
            StatusMessage = status switch
            {
                ExportReadinessStatus.Blocked => "Downstream modules are blocked until the missing or invalid canonical geo data is fixed.",
                ExportReadinessStatus.NeedsAttention => "Downstream modules can proceed, but some recommended mesh or confidence items still need attention.",
                _ => "The project has the canonical geo information later modules expect."
            },
            Items = items
        };
    }
}
