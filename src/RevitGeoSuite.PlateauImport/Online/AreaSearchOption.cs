using RevitGeoSuite.Core.Plateau.Catalog;

namespace RevitGeoSuite.PlateauImport.Online;

/// <summary>
/// One row in the unified prefecture+municipality search list. Carries the
/// underlying <see cref="PlateauAreaOption"/> plus presentation strings and a
/// pre-lowercased token bag used by the filter.
/// </summary>
public sealed class AreaSearchOption
{
    public AreaSearchOption(
        string prefectureJapaneseName,
        PlateauAreaOption area,
        string displayLabel,
        string codeLabel,
        string searchTokens)
    {
        PrefectureJapaneseName = prefectureJapaneseName;
        Area = area;
        DisplayLabel = displayLabel;
        CodeLabel = codeLabel;
        SearchTokens = searchTokens;
    }

    public string PrefectureJapaneseName { get; }

    public PlateauAreaOption Area { get; }

    public string DisplayLabel { get; }

    public string CodeLabel { get; }

    public string SearchTokens { get; }

    public override string ToString() => DisplayLabel;
}
