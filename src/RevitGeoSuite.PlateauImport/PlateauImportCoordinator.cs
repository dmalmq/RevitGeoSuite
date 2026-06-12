using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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
        PlateauImportState? existingState,
        Action<int, int, string>? onProgress = null,
        CancellationToken ct = default)
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

        if (plan.Shapes.Count == 0)
        {
            throw new InvalidOperationException("The selected PLATEAU folder and filters did not produce any importable context geometry.");
        }

        // Import one tile per transaction inside a TransactionGroup. Committing each tile lets Revit
        // release transient regeneration memory between tiles; building every tile in a single
        // transaction exhausts Revit's native heap on large selections and crashes the process. The
        // group makes the whole import a single undo step (same pattern the DXF basemap path uses).
        using TransactionGroup transactionGroup = new TransactionGroup(revitDocument, "Import PLATEAU Context");
        transactionGroup.Start();
        try
        {
            List<string> warnings = new List<string>(plan.WarningMessages ?? Array.Empty<string>());

            // 1) One delete pass for all overlapping prior imports before creating anything.
            using (Transaction deleteTransaction = new Transaction(revitDocument, "Replace PLATEAU Imports"))
            {
                deleteTransaction.Start();
                contextImporter.DeleteExistingImports(revitDocument, plan.Shapes);
                deleteTransaction.Commit();
            }

            // Shared across per-tile transactions so newly created group names stay unique.
            ISet<string> existingGroupNames = contextImporter.GetImportGroupNames(revitDocument);

            List<IGrouping<string, ContextShapePlan>> tileBatches = plan.Shapes
                .GroupBy(shape => shape.TileId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();

            int importedCount = 0;
            int createdGroupCount = 0;
            for (int index = 0; index < tileBatches.Count; index++)
            {
                ct.ThrowIfCancellationRequested();

                IGrouping<string, ContextShapePlan> tileBatch = tileBatches[index];
                ContextShapePlan[] tileShapes = tileBatch.ToArray();

                using (Transaction tileTransaction = new Transaction(revitDocument, $"Import PLATEAU tile {tileBatch.Key}"))
                {
                    tileTransaction.Start();
                    (int tileImported, int tileGroups) = contextImporter.ImportShapeBatch(revitDocument, tileShapes, warnings, existingGroupNames);
                    tileTransaction.Commit();
                    importedCount += tileImported;
                    createdGroupCount += tileGroups;
                }

                onProgress?.Invoke(index + 1, tileBatches.Count, $"Importing tile {tileBatch.Key} ({index + 1}/{tileBatches.Count})…");
            }

            if (importedCount == 0)
            {
                throw new InvalidOperationException("None of the filtered PLATEAU features could be converted into valid Revit context geometry. Review the warnings list and adjust the folder or filters before importing again.");
            }

            PlateauContextImportExecutionResult execution = new PlateauContextImportExecutionResult
            {
                ImportedElementCount = importedCount,
                CreatedGroupCount = createdGroupCount,
                WarningMessages = warnings
            };

            PlateauImportState updatedState = BuildUpdatedState(existingState, plan, execution, referenceSource);

            // 2) Persist import state, then assimilate so the whole import collapses to one undo step.
            using (Transaction stateTransaction = new Transaction(revitDocument, "Record PLATEAU Import State"))
            {
                stateTransaction.Start();
                stateService.Save(handle, updatedState);
                stateTransaction.Commit();
            }

            transactionGroup.Assimilate();

            return new PlateauImportResult
            {
                ImportedElementCount = execution.ImportedElementCount,
                CreatedGroupCount = execution.CreatedGroupCount,
                UpdatedState = updatedState,
                WarningMessages = execution.WarningMessages,
                SummaryMessage = BuildSummaryMessage(plan, execution, referenceSource)
            };
        }
        catch (OperationCanceledException)
        {
            // Surface cancellation unchanged so the job layer reports "Cancelled", not a failure.
            if (transactionGroup.GetStatus() == TransactionStatus.Started)
            {
                transactionGroup.RollBack();
            }

            throw;
        }
        catch (Exception ex)
        {
            if (transactionGroup.GetStatus() == TransactionStatus.Started)
            {
                transactionGroup.RollBack();
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
            LastGeometryImportMode = plan.GeometryImportMode,
            LastImportedFeatureCount = execution.ImportedElementCount,
            LastImportedGroupCount = execution.CreatedGroupCount,
            LastSelectedTileIds = plan.SelectedTileIds.OrderBy(tileId => tileId, StringComparer.Ordinal).ToList(),
            LastSelectedFeatureTypes = plan.SelectedFeatureTypes.Select(type => type.ToString()).OrderBy(name => name, StringComparer.Ordinal).ToList(),
            LastImportSummary = string.Format(CultureInfo.InvariantCulture, "Imported {0} elements in {1} groups using {2}.", execution.ImportedElementCount, execution.CreatedGroupCount, plan.GeometryImportMode.GetDisplayName().ToLowerInvariant())
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
        string modeText = plan.GeometryImportMode.GetDisplayName();
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
            "Imported {0} PLATEAU context elements from '{1}' using {2} in {3} mode and created {4} Revit group(s). {5} {6}{7}",
            execution.ImportedElementCount,
            folderName,
            sourceText,
            modeText,
            execution.CreatedGroupCount,
            categoryText,
            tileText,
            warningText);
    }
}


