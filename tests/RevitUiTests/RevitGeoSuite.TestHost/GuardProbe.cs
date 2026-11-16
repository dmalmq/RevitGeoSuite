using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitGeoSuite.TestHost;

/// <summary>
/// G4 probe: what Revit does with a guard warning that no failures preprocessor deletes. Copies a
/// floor in place and keeps the guard's warnings (others are taken off as in every fixture), then
/// reads the model's stored warnings after the commit, in a saved and reopened copy, and after the
/// floor type gets a unit category back.
/// </summary>
internal static class GuardProbe
{
    // The guard's failure definition ids all start with this (StandardGuardUpdater).
    private const string GuardIdPrefix = "0f6c2b71-3a8e-4c55-a1d4-6e2f9b7c3d1";

    public static Dictionary<string, object?> Run(UIApplication app, string root, long floorId, long typeId, string gisCategory, string savePath)
    {
        Document document = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");
        TestRoot.Require(root, document.PathName);
        List<string> log = new();
        var keeper = new KeepGuardWarnings();
        long copyId;
        using (var transaction = new Transaction(document, "UI test: guard probe"))
        {
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(keeper).SetClearAfterRollback(true));
            transaction.Start();
            copyId = ElementTransformUtils.CopyElement(document, new ElementId(floorId), XYZ.Zero).First().Value;
            if (transaction.Commit() != TransactionStatus.Committed)
            {
                throw new InvalidOperationException($"Revit did not commit the copy. {string.Join(" ", keeper.Errors)}");
            }
        }

        log.Add($"copied {floorId} to {copyId}; kept {keeper.Kept.Count} guard warning(s), took off {keeper.Removed.Count} other(s)");
        List<string> afterCommit = GuardWarnings(document);

        List<string> afterReopen;
        string reopenPath = Path.Combine(Path.GetDirectoryName(savePath)!, Path.GetFileNameWithoutExtension(savePath) + "-reopen.rvt");
        var save = new SaveAsOptions { OverwriteExistingFile = true };
        if (document.IsWorkshared)
        {
            save.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });
        }

        document.SaveAs(TestRoot.Require(root, savePath), save);
        File.Copy(savePath, TestRoot.Require(root, reopenPath), true);
        var open = new OpenOptions();
        if (document.IsWorkshared)
        {
            open.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
        }

        Document reopened = app.Application.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(reopenPath), open);
        try
        {
            afterReopen = GuardWarnings(reopened);
        }
        finally
        {
            reopened.Close(false);
        }

        var fixer = new FixtureWarnings();
        using (var transaction = new Transaction(document, Commands.FixtureTransaction))
        {
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(fixer).SetClearAfterRollback(true));
            transaction.Start();
            Parameter parameter = document.GetElement(new ElementId(typeId)).LookupParameter("gis_category") ?? throw new InvalidOperationException($"Type {typeId} has no gis_category.");
            parameter.Set(gisCategory);
            if (transaction.Commit() != TransactionStatus.Committed)
            {
                throw new InvalidOperationException($"Revit did not commit gis_category on type {typeId}. {string.Join(" ", fixer.Errors)}");
            }
        }

        log.Add($"set gis_category '{gisCategory}' on type {typeId}");
        return new Dictionary<string, object?>
        {
            ["copyId"] = copyId,
            ["kept"] = keeper.Kept,
            ["afterCommit"] = afterCommit,
            ["afterReopen"] = afterReopen,
            ["afterFix"] = GuardWarnings(document),
            ["log"] = log,
        };
    }

    /// <summary>The model's stored warnings that the guard posted, as "text [element ids]".</summary>
    private static List<string> GuardWarnings(Document document) =>
        document.GetWarnings()
            .Where(warning => warning.GetFailureDefinitionId().Guid.ToString().StartsWith(GuardIdPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(warning => $"{warning.GetDescriptionText()} [{string.Join(", ", warning.GetFailingElements().Select(id => id.Value))}]")
            .ToList();

    private sealed class KeepGuardWarnings : IFailuresPreprocessor
    {
        public List<string> Kept { get; } = new();

        public List<string> Removed { get; } = new();

        public List<string> Errors { get; } = new();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failures)
        {
            foreach (FailureMessageAccessor failure in failures.GetFailureMessages())
            {
                if (failure.GetSeverity() != FailureSeverity.Warning)
                {
                    Errors.Add(failure.GetDescriptionText());
                }
                else if (failure.GetFailureDefinitionId().Guid.ToString().StartsWith(GuardIdPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    Kept.Add(failure.GetDescriptionText());
                }
                else
                {
                    Removed.Add(failure.GetDescriptionText());
                    failures.DeleteWarning(failure);
                }
            }

            return Errors.Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
