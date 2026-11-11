namespace RevitGeoSuite.FloorPlanExport.Core.Models;

/// <summary>
/// The <c>category</c> value of a detail line. Step lines use the value the reference GDBs use;
/// a model line carries the name of its line style, so lines drawn in one style can be selected
/// together in ArcGIS.
/// </summary>
public static class DetailCategory
{
    public const string AttributeName = "category";

    /// <summary>A stair step line, as in the Shinjuku reference GDB.</summary>
    public const string Steps = "steps";

    /// <summary>A model line whose line style could not be read.</summary>
    public const string Line = "line";

    /// <summary>The category of a model line drawn in <paramref name="lineStyleName"/>.</summary>
    public static string ForLineStyle(string? lineStyleName)
    {
        string trimmed = (lineStyleName ?? string.Empty).Trim();
        return trimmed.Length == 0 ? Line : trimmed;
    }
}
