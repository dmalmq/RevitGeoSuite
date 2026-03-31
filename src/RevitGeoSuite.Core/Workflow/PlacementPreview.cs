using System.Collections.Generic;

namespace RevitGeoSuite.Core.Workflow;

public sealed class PlacementPreview
{
    public IList<PlacementPreviewField> Fields { get; } = new List<PlacementPreviewField>();

    public IList<string> Warnings { get; } = new List<string>();

    public IList<string> WhatWillChange { get; } = new List<string>();

    public IList<string> WhatWillNotChange { get; } = new List<string>();

    public string PersistenceSummary { get; set; } = string.Empty;

    public string ChangeImpactSummary { get; set; } = string.Empty;

    public string ConfidenceSummary { get; set; } = string.Empty;

    public bool IsReadyToApply { get; set; }
}
