namespace RevitGeoSuite.Georeference;

public sealed class SummaryRow
{
    public SummaryRow(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public string Value { get; }
}
