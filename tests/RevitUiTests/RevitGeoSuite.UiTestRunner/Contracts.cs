namespace RevitGeoSuite.UiTestRunner;

// The parts of the bridge replies the checks read. Names follow the bridge's camelCase JSON.

internal sealed record InspectReply(
    string ModelVersion,
    int StandardVersion,
    FindingItem[] Findings,
    string[] SetupLines,
    ProposalItem[] Proposals,
    UnmatchedItem[] Unmatched,
    string FixBlocker)
{
    public int Count(string severity) => Findings.Count(finding => finding.Severity == severity);

    public int FloorTypeFindings() => Findings.Count(finding => finding.Topic.Contains("floor type", StringComparison.OrdinalIgnoreCase));
}

internal sealed record FindingItem(string Severity, string Topic, string Message, long[] ElementIds);

// NewName and Reason "oldName" come with floor type standard version 2 (RevitGeoSuite #52); older builds leave them empty.
internal sealed record ProposalItem(long FloorTypeId, string FloorTypeName, int FloorCount, string? StandardTypeName = null, string? GisCategory = null, string? NewName = null, string? Reason = null);

internal sealed record UnmatchedItem(long FloorTypeId, string Name, long[] FloorIds);
