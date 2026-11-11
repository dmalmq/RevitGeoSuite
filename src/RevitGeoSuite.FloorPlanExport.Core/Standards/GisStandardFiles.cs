using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>The version of the Kanizu GIS standard this build of RevitGeoSuite knows.</summary>
public static class GisStandard
{
    /// <summary>
    /// The current standard. Raise it when the shipped files change in a way a model has to be set up
    /// again for, and add the migration from the version before.
    /// </summary>
    public const int Version = 1;
}

/// <summary>
/// The files in <c>standards/</c>, embedded in this assembly so the add-in never depends on a file
/// beside it. The pyRevit tools read the same files from the folder.
/// </summary>
public static class GisStandardFiles
{
    public const string ColorThemeFileName = "color_theme.json";

    private const string ResourcePrefix = "RevitGeoSuite.Standards.";

    public static string ReadColorTheme() => Read(ColorThemeFileName);

    public static string ReadFloorTypes() => Read(FloorTypeStandard.FileName);

    public static string ReadSharedParameterFile() => Read(GisSharedParameters.FileName);

    public static string ReadGisModelTemplate() => Read(GisModelFile.TemplateFileName);

    private static string Read(string fileName)
    {
        Assembly assembly = typeof(GisStandardFiles).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourcePrefix + fileName);
        if (stream == null)
        {
            throw new InvalidOperationException($"The standards file '{fileName}' is not embedded in {assembly.GetName().Name}.");
        }

        using StreamReader reader = new(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }
}
