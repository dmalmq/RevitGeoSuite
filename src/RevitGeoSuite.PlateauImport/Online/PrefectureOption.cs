namespace RevitGeoSuite.PlateauImport.Online;

public sealed class PrefectureOption
{
    public PrefectureOption(string japaneseName, string displayLabel)
    {
        JapaneseName = japaneseName;
        DisplayLabel = displayLabel;
    }

    public string JapaneseName { get; }

    public string DisplayLabel { get; }

    public override string ToString() => DisplayLabel;
}
