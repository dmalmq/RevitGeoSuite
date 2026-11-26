using System;
using System.IO;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;

namespace RevitGeoSuite.FloorPlanExport.StarterKit;

/// <summary>
/// RAII guard around Revit's global <see cref="Application.SharedParametersFilename"/>.
/// Guarantees that the original user shared-parameter file path is restored in <see cref="Dispose"/>
/// even if family building throws an exception mid-operation.
/// </summary>
public sealed class ScopedSharedParametersFile : IDisposable
{
    private readonly ScopedRestoreGuard _guard;

    public ScopedSharedParametersFile(Application application, string temporarySharedParametersPath)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (string.IsNullOrWhiteSpace(temporarySharedParametersPath))
        {
            throw new ArgumentException("Shared parameter file path is required.", nameof(temporarySharedParametersPath));
        }

        _guard = new ScopedRestoreGuard(
            () => application.SharedParametersFilename ?? string.Empty,
            value => application.SharedParametersFilename = value,
            temporarySharedParametersPath);
    }

    public string OriginalPath => _guard.OriginalValue;

    public string ActivePath => _guard.TemporaryValue;

    public bool IsRestored => _guard.IsRestored;

    /// <summary>
    /// Ensures the shipped <c>KanizuGIS.SharedParameters.txt</c> exists on disk (writing it from the embedded
    /// standard resource when <paramref name="explicitPath"/> is omitted) and returns its full path.
    /// </summary>
    public static string EnsureShippedFilePath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            string full = Path.GetFullPath(explicitPath!);
            if (!File.Exists(full))
            {
                throw new FileNotFoundException($"Shared parameters file not found at '{full}'.", full);
            }

            return full;
        }

        string content = GisStandardFiles.ReadSharedParameterFile();
        string filePath = Path.Combine(FloorPlanExportAppDataPaths.CurrentBaseDirectory, GisSharedParameters.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        if (!File.Exists(filePath) || !string.Equals(File.ReadAllText(filePath), content, StringComparison.Ordinal))
        {
            File.WriteAllText(filePath, content, new UTF8Encoding(false));
        }

        return filePath;
    }

    public void Dispose()
    {
        _guard.Dispose();
    }
}
