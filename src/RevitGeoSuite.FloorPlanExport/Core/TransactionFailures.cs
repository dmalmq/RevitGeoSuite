using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.FloorPlanExport.Core;

/// <summary>
/// Keeps Revit's failure dialogs out of a RevitGeoSuite write. Each warning's text is recorded and
/// the warning deleted; an error rolls the whole transaction back, and its text is recorded. Revit
/// then shows nothing, so no modal dialog can stall the write, and the caller reports both lists.
/// </summary>
public sealed class TransactionFailures : IFailuresPreprocessor
{
    public List<string> Warnings { get; } = new();

    public List<string> Errors { get; } = new();

    /// <summary>Sets a new instance as <paramref name="transaction"/>'s failures preprocessor. Call it before Start.</summary>
    public static TransactionFailures Attach(Transaction transaction)
    {
        TransactionFailures failures = new();
        transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(failures));
        return failures;
    }

    /// <summary>Each distinct warning once, as a line for a log or a warnings list.</summary>
    public IEnumerable<string> WarningLines() => Warnings.Distinct().Select(text => "Revit warned: " + text);

    /// <summary>Commits; when Revit rolled back instead, throws with the errors it gave.</summary>
    public void Commit(Transaction transaction)
    {
        if (transaction.Commit() != TransactionStatus.Committed)
        {
            string why = Errors.Count > 0 ? ": " + string.Join(" ", Errors.Distinct()) : ".";
            throw new InvalidOperationException($"Revit did not commit '{transaction.GetName()}'{why}");
        }
    }

    public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
    {
        bool error = false;
        foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
        {
            string text = failure.GetDescriptionText();
            if (failure.GetSeverity() == FailureSeverity.Warning)
            {
                Warnings.Add(text);
                failuresAccessor.DeleteWarning(failure);
            }
            else
            {
                Errors.Add(text);
                error = true;
            }
        }

        if (error)
        {
            // Without this Revit still shows the rolled-back errors in its own dialog, which waits for a click.
            FailureHandlingOptions options = failuresAccessor.GetFailureHandlingOptions();
            options.SetClearAfterRollback(true);
            failuresAccessor.SetFailureHandlingOptions(options);
        }

        return error ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
    }
}
