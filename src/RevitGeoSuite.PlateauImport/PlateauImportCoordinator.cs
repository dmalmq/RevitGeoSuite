using System;
using System.Collections.Generic;
using System.Globalization;
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
            PlateauContextImportExecutionResult execution = contextImporter.Import(revitDocument, plan);
            PlateauImportState updatedState = BuildUpdatedState(existingState, plan, execution, referenceSource);
            stateService.Save(handle, updatedState);

            TransactionStatus status = transaction.Commit();
            if (status != TransactionStatus.Committed)
            {
                throw new InvalidOperationException("Revit did not commit the PLATEAU context import transaction.");
            }

            return new PlateauImportResult
            {
                ImportedElementCount = execution.ImportedElementCount,
                CreatedGroupCount = execution.CreatedGroupCount,
                UpdatedState = updatedState,
                WarningMessages = execution.WarningMessages,
                SummaryMessage = BuildSummaryMessage(plan, execution, referenceSource)
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
        PlateauContextImportExecutionResult execution,
        PlateauImportReferenceSource referenceSource)
    {
        PlateauImportState updatedState = new PlateauImportState
        {
            ImportedTileIds = new List<string>((IEnumerable<string>?)existingState?.ImportedTileIds ?? Array.Empty<string>()),
            LastImportDateUtc = DateTime.UtcNow,
            LastImportedFilePath = plan.SourceModels.Count == 1 ? plan.SourceModels.First().SourcePath : string.Empty,
            LastImportedFolderPath = plan.SourceFolderPath,
            LastReferenceSource = referenceSource,
            LastImportedFeatureCount = execution.ImportedElementCount,
            LastImportedGroupCount = execution.CreatedGroupCount,
            LastSelectedTileIds = plan.SelectedTileIds.OrderBy(tileId => tileId, StringComparer.Ordinal).ToList(),
            LastSelectedFeatureTypes = plan.SelectedFeatureTypes.Select(type => type.ToString()).OrderBy(name => name, StringComparer.Ordinal).ToList(),
            LastImportSummary = string.Format(CultureInfo.InvariantCulture, "Imported {0} elements in {1} groups.", execution.ImportedElementCount, execution.CreatedGroupCount)
        };

        foreach (string tileId in plan.SelectedTileIds)
        {
            if (!updatedState.ImportedTileIds.Contains(tileId, StringComparer.Ordinal))
            {
                updatedState.ImportedTileIds.Add(tileId);
            }
        }

        return updatedState;
    }

    private static string BuildSummaryMessage(
        ContextImportPlan plan,
        PlateauContextImportExecutionResult execution,
        PlateauImportReferenceSource referenceSource)
    {
        string folderName = string.IsNullOrWhiteSpace(plan.SourceFolderPath)
            ? "selected folder"
            : Path.GetFileName(plan.SourceFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string sourceText = referenceSource == PlateauImportReferenceSource.WorkingProjectBasePoint
            ? "Project Base Point"
            : "Canonical Origin";
        string tileText = plan.SelectedTileIds.Count == 0
            ? "No tile filter was recorded."
            : "Tiles: " + string.Join(", ", plan.SelectedTileIds);
        string categoryText = plan.SelectedFeatureTypes.Count == 0
            ? "No category filter was recorded."
            : "Categories: " + string.Join(", ", plan.SelectedFeatureTypes.Select(type => type.GetPluralDisplayName()));
        string warningText = execution.WarningMessages.Count == 0
            ? string.Empty
            : string.Format(CultureInfo.InvariantCulture, " {0} warning(s) were recorded.", execution.WarningMessages.Count);

        return string.Format(
            CultureInfo.InvariantCulture,
            "Imported {0} PLATEAU context elements from '{1}' using {2} and created {3} Revit group(s). {4} {5}{6}",
            execution.ImportedElementCount,
            folderName,
            sourceText,
            execution.CreatedGroupCount,
            categoryText,
            tileText,
            warningText);
    }
}


