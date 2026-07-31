using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.RevitInterop;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlExportCoordinator
{
    private readonly CityGmlGeometryExtractor geometryExtractor;
    private readonly CityGmlWriter writer;
    private readonly ExportValidator validator;
    private readonly CityGmlExportStateService stateService;

    public CityGmlExportCoordinator(
        CityGmlGeometryExtractor? geometryExtractor = null,
        CityGmlWriter? writer = null,
        ExportValidator? validator = null,
        CityGmlExportStateService? stateService = null)
    {
        this.geometryExtractor = geometryExtractor ?? new CityGmlGeometryExtractor();
        this.writer = writer ?? new CityGmlWriter();
        this.validator = validator ?? new ExportValidator();
        this.stateService = stateService ?? new CityGmlExportStateService();
    }

    public CityGmlExportPreparationResult Prepare(
        IDocumentHandle document,
        CityGmlExportReferenceContext referenceContext,
        CityGmlExportScopeSelection scope,
        string targetSchemaVersion,
        IReadOnlyDictionary<string, string>? categoryOverrides,
        IReadOnlyDictionary<string, string>? codelistOverrides)
    {
        RevitDocumentHandle handle = document as RevitDocumentHandle
            ?? throw new InvalidOperationException("CityGML export requires a RevitDocumentHandle.");
        Document revitDocument = handle.Document;

        if (revitDocument.IsFamilyDocument)
        {
            throw new InvalidOperationException("CityGML export is not supported in family documents.");
        }

        if (!scope.HasSelectedView)
        {
            throw new InvalidOperationException("Select a 3D view before preparing CityGML export.");
        }

        CityGmlExtractionResult extraction = geometryExtractor.Extract(revitDocument, referenceContext, scope, categoryOverrides, codelistOverrides);
        IReadOnlyList<CityGmlFeature> features = extraction.Features.ToList();

        if (features.Count == 0)
        {
            string warningText = extraction.Warnings.Count == 0 ? string.Empty : $" Last warning: {extraction.Warnings.First()}";
            throw new InvalidOperationException($"No exportable model geometry was found for the selected CityGML reference context and 3D view.{warningText}");
        }

        CityGmlExportPackage package = new CityGmlExportPackage
        {
            ReferenceContext = referenceContext,
            TargetSchemaVersion = targetSchemaVersion,
            Features = features,
            SemanticCounts = features
                .GroupBy(feature => feature.SemanticType)
                .ToDictionary(group => group.Key, group => group.Count()),
            XmlPreview = string.Empty
        };

        string xmlPreview = writer.BuildXml(package);
        CityGmlValidationReport report = validator.Validate(xmlPreview, package);
        package = new CityGmlExportPackage
        {
            ReferenceContext = package.ReferenceContext,
            TargetSchemaVersion = package.TargetSchemaVersion,
            Features = package.Features,
            SemanticCounts = package.SemanticCounts,
            XmlPreview = xmlPreview,
            ValidationReport = report,
            OutputFileName = package.OutputFileName
        };

        List<string> validationMessages = new List<string>(extraction.Warnings);
        validationMessages.AddRange(report.AllMessages);

        return new CityGmlExportPreparationResult
        {
            Package = package,
            PreparedRows = BuildPreparedRows(package, scope),
            FeatureNames = BuildFeatureNames(package),
            ValidationMessages = validationMessages,
            StatusMessage = BuildStatusMessage(package, scope, extraction.Warnings.Count)
        };
    }

    public CityGmlExportResult Export(
        IDocumentHandle document,
        CityGmlExportPackage package,
        string outputDirectory,
        CityGmlExportReferenceSource referenceSource,
        CityGmlExportScopeSelection scope,
        CityGmlExportState? existingState,
        IReadOnlyDictionary<string, string>? categoryOverrides,
        IReadOnlyDictionary<string, string>? codelistOverrides)
    {
        RevitDocumentHandle handle = document as RevitDocumentHandle
            ?? throw new InvalidOperationException("CityGML export requires a RevitDocumentHandle.");
        Document revitDocument = handle.Document;

        if (revitDocument.IsFamilyDocument)
        {
            throw new InvalidOperationException("CityGML export is not supported in family documents.");
        }

        if (package.ValidationReport.HasErrors)
        {
            throw new InvalidOperationException("The prepared CityGML export contains validation errors and cannot be written.");
        }

        if (!scope.HasSelectedView)
        {
            throw new InvalidOperationException("Select a 3D view before exporting CityGML.");
        }

        string exportPath = writer.Write(outputDirectory, package);
        CityGmlExportState updatedState = BuildUpdatedState(existingState, outputDirectory, package, referenceSource, scope, categoryOverrides, codelistOverrides);
        bool statePersisted = false;

        if (!revitDocument.IsReadOnly)
        {
            using Transaction transaction = new Transaction(revitDocument, "Save CityGML Export State");
            transaction.Start();
            stateService.Save(handle, updatedState);
            TransactionStatus status = transaction.Commit();
            if (status != TransactionStatus.Committed)
            {
                throw new InvalidOperationException("CityGML export completed, but Revit did not commit the export state transaction.");
            }

            statePersisted = true;
        }

        return new CityGmlExportResult
        {
            UpdatedState = updatedState,
            ExportPath = exportPath,
            StatePersisted = statePersisted,
            SummaryMessage = BuildSummaryMessage(updatedState, package, exportPath, statePersisted)
        };
    }

    internal static CityGmlExportState BuildUpdatedState(
        CityGmlExportState? existingState,
        string outputDirectory,
        CityGmlExportPackage package,
        CityGmlExportReferenceSource referenceSource,
        CityGmlExportScopeSelection scope,
        IReadOnlyDictionary<string, string>? categoryOverrides,
        IReadOnlyDictionary<string, string>? codelistOverrides)
    {
        return new CityGmlExportState
        {
            LastExportPath = outputDirectory,
            LastExportDateUtc = DateTime.UtcNow,
            LastReferenceSource = referenceSource,
            LastViewUniqueId = scope.SelectedView?.UniqueId ?? string.Empty,
            LastViewName = scope.SelectedView?.Title ?? string.Empty,
            LastSelectedLinkUniqueIds = scope.SelectedLinkedModels.Select(option => option.UniqueId).ToList(),
            LastSelectedLinkNames = scope.SelectedLinkedModels.Select(option => option.Title).ToList(),
            LastExportedFeatureCount = package.Features.Count,
            TargetSchemaVersion = package.TargetSchemaVersion,
            CategoryMappingOverrides = categoryOverrides is null
                ? existingState?.CategoryMappingOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : categoryOverrides.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
            CodelistOverrides = codelistOverrides is null
                ? existingState?.CodelistOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : codelistOverrides.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string BuildStatusMessage(CityGmlExportPackage package, CityGmlExportScopeSelection scope, int extractionWarningCount)
    {
        string viewText = scope.SelectedView?.Title ?? "selected view";
        string linkText = scope.SelectedLinkedModels.Count == 0
            ? "host model only"
            : $"{scope.SelectedLinkedModels.Count} linked model(s) included";
        string extractionText = extractionWarningCount > 0
            ? $" {extractionWarningCount} extraction warning(s) were recorded."
            : string.Empty;
        if (package.ValidationReport.HasErrors)
        {
            return $"Prepared {package.Features.Count} CityGML features from '{viewText}', but validation found {package.ValidationReport.Errors.Count} blocking issue(s).{extractionText}";
        }

        if (package.ValidationReport.HasWarnings)
        {
            return $"Prepared {package.Features.Count} CityGML features from '{viewText}' with {linkText}. Validation completed with {package.ValidationReport.Warnings.Count} warning(s).{extractionText}";
        }

        return $"Prepared {package.Features.Count} CityGML features from '{viewText}' with {linkText} using {package.ReferenceContext.Title}.{extractionText}";
    }

    private static string BuildSummaryMessage(CityGmlExportState state, CityGmlExportPackage package, string exportPath, bool statePersisted)
    {
        string persistenceText = statePersisted
            ? "The CityGML export state was saved in module storage separately from GeoProjectInfo."
            : "The CityGML export state was not saved because the Revit document is read-only.";
        string linkText = state.LastSelectedLinkNames.Count == 0
            ? "host model only"
            : $"{state.LastSelectedLinkNames.Count} linked model(s)";
        return $"Exported {package.Features.Count} CityGML features to '{exportPath}' using {FormatReferenceSource(state.LastReferenceSource)} from view '{state.LastViewName}' with {linkText}. {persistenceText}";
    }

    private static string FormatReferenceSource(CityGmlExportReferenceSource referenceSource)
    {
        return referenceSource == CityGmlExportReferenceSource.WorkingProjectBasePoint
            ? "Working Project Base Point"
            : "Canonical Origin";
    }

    private static IReadOnlyCollection<DetailRow> BuildPreparedRows(CityGmlExportPackage package, CityGmlExportScopeSelection scope)
    {
        List<DetailRow> rows = new List<DetailRow>
        {
            new DetailRow("Export Reference", package.ReferenceContext.Title),
            new DetailRow("Reference CRS", $"EPSG:{package.ReferenceContext.ProjectCrs.EpsgCode}  {package.ReferenceContext.ProjectCrs.NameSnapshot}"),
            new DetailRow("Anchor Location", $"{package.ReferenceContext.AnchorLatitude:F6}, {package.ReferenceContext.AnchorLongitude:F6}, elev {package.ReferenceContext.AnchorElevationMeters:F3} m"),
            new DetailRow("Source View", scope.SelectedView?.Title ?? "Not selected"),
            new DetailRow("Linked Models", scope.SelectedLinkedModels.Count == 0 ? "Host model only" : string.Join(", ", scope.SelectedLinkedModels.Select(option => option.Title))),
            new DetailRow("Target Profile", package.TargetSchemaVersion),
            new DetailRow("City Objects", package.Features.Count.ToString()),
            new DetailRow("Output File", package.OutputFileName),
            new DetailRow("Validation Errors", package.ValidationReport.Errors.Count.ToString()),
            new DetailRow("Validation Warnings", package.ValidationReport.Warnings.Count.ToString())
        };

        foreach (KeyValuePair<CityGmlSemanticType, int> semanticCount in package.SemanticCounts.OrderBy(entry => entry.Key.ToString(), StringComparer.Ordinal))
        {
            rows.Add(new DetailRow($"{semanticCount.Key} Features", semanticCount.Value.ToString()));
        }

        return rows;
    }

    private static IReadOnlyCollection<string> BuildFeatureNames(CityGmlExportPackage package)
    {
        List<string> names = package.Features
            .Select(feature => $"{feature.SemanticType}: {feature.Name}")
            .Take(24)
            .ToList();

        if (package.Features.Count > names.Count)
        {
            names.Add($"... and {package.Features.Count - names.Count} more");
        }

        return names;
    }
}
