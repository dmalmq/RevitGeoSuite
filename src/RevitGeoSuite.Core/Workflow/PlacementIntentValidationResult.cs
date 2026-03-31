using System.Collections.Generic;

namespace RevitGeoSuite.Core.Workflow;

public sealed class PlacementIntentValidationResult
{
    public IList<string> Errors { get; } = new List<string>();

    public bool IsValid => Errors.Count == 0;
}
