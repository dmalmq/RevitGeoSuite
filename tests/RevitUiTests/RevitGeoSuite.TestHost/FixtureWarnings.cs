using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.TestHost;

/// <summary>Takes a fixture's warnings off at commit, so no warning dialog opens, and keeps their text.</summary>
internal sealed class FixtureWarnings : IFailuresPreprocessor
{
    public List<string> Removed { get; } = new();

    public List<string> Errors { get; } = new();

    public FailureProcessingResult PreprocessFailures(FailuresAccessor failures)
    {
        foreach (FailureMessageAccessor failure in failures.GetFailureMessages())
        {
            if (failure.GetSeverity() == FailureSeverity.Warning)
            {
                Removed.Add(failure.GetDescriptionText());
                failures.DeleteWarning(failure);
            }
            else
            {
                Errors.Add(failure.GetDescriptionText());
            }
        }

        // An error left in place would open Revit's error dialog; roll back and let the caller report it.
        return Errors.Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
    }
}
