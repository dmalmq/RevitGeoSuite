namespace RevitGeoSuite.PlateauImport;

public sealed class DetailRow
{
    public DetailRow(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public string Value { get; }
}
