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

internal sealed record ProposalItem(long FloorTypeId, string FloorTypeName, int FloorCount);

internal sealed record UnmatchedItem(long FloorTypeId, string Name, long[] FloorIds);
