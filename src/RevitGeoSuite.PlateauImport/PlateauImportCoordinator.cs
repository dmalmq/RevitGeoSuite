using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.RevitInterop;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportCoordinator
{
    private readonly PlateauContextImporter contextImporter;
    private readonly PlateauImportStateService stateService;

    public PlateauImportCoordinator(
        PlateauContextImporter? contextImporter = null,
        PlateauImportStateService? stateService = null)
    {
        this.contextImporter = contextImporter ?? new PlateauContextImporter();
        this.stateService = stateService ?? new PlateauImportStateService();
    }

    public PlateauImportResult Import(
        IDocumentHandle document,
        ContextImportPlan plan,
        PlateauImportReferenceSource referenceSource,
        PlateauImportState? existingState)
    {
        RevitDocumentHandle handle = document as RevitDocumentHandle
            ?? throw new InvalidOperationException("PLATEAU import requires a RevitDocumentHandle.");
        Document revitDocument = handle.Document;

        if (revitDocument.IsFamilyDocument)
        {
            throw new InvalidOperationException("PLATEAU context import is not supported in family documents.");
        }

        if (revitDocument.IsReadOnly)
        {
            throw new InvalidOperationException("This Revit document is read-only. PLATEAU import requires an editable project.");
        }

        if (revitDocument.IsModifiable)
        {
            throw new InvalidOperationException("Another Revit transaction is already active. Finish it before importing PLATEAU context.");
        }

        using Transaction transaction = new Transaction(revitDocument, "Import PLATEAU Context");
        transaction.Start();
        try
        {
            int importedElementCount = contextImporter.Import(revitDocument, plan);
            PlateauImportState updatedState = BuildUpdatedState(existingState, plan, importedElementCount, referenceSource);
            stateService.Save(handle, updatedState);

            TransactionStatus status = transaction.Commit();
            if (status != TransactionStatus.Committed)
            {
                throw new InvalidOperationException("Revit did not commit the PLATEAU context import transaction.");
            }

            return new PlateauImportResult
            {
                ImportedElementCount = importedElementCount,
                UpdatedState = updatedState,
                SummaryMessage = BuildSummaryMessage(plan, importedElementCount, referenceSource, updatedState)
            };
        }
        catch (Exception ex)
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw new InvalidOperationException("PLATEAU context import failed. Revit rolled back the transaction. " + ex.Message, ex);
        }
    }

    internal static PlateauImportState BuildUpdatedState(
        PlateauImportState? existingState,
        ContextImportPlan plan,
        int importedElementCount,
        PlateauImportReferenceSource referenceSource)
    {
        PlateauImportState updatedState = new PlateauImportState
        {
            ImportedTileIds = new List<string>((IEnumerable<string>?)existingState?.ImportedTileIds ?? Array.Empty<string>()),
            LastImportDateUtc = DateTime.UtcNow,
            LastImportedFilePath = plan.CityModel.SourcePath,
            LastReferenceSource = referenceSource,
            LastImportedFeatureCount = importedElementCount
        };

        if (!string.IsNullOrWhiteSpace(plan.CityModel.FileTileId)
            && !updatedState.ImportedTileIds.Contains(plan.CityModel.FileTileId!, StringComparer.Ordinal))
        {
            updatedState.ImportedTileIds.Add(plan.CityModel.FileTileId!);
        }

        return updatedState;
    }

    private static string BuildSummaryMessage(
        ContextImportPlan plan,
        int importedElementCount,
        PlateauImportReferenceSource referenceSource,
        PlateauImportState updatedState)
    {
        string fileName = Path.GetFileName(plan.CityModel.SourcePath);
        string sourceText = referenceSource == PlateauImportReferenceSource.WorkingProjectBasePoint
            ? "Working Project Base Point"
            : "Canonical Origin";
        string tileText = updatedState.ImportedTileIds.Count == 0
            ? "No tile id was detected from the file name."
            : "Tracked tile ids: " + string.Join(", ", updatedState.ImportedTileIds);

        return $"Imported {importedElementCount} PLATEAU context elements from '{fileName}' using {sourceText}. {tileText}";
    }
}
