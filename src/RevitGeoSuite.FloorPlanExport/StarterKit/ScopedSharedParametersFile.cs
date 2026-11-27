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
        WriteUtf16LeBomIfChanged(filePath, content);

        return filePath;
    }

    /// <summary>
    /// Writes a Revit shared-parameter <c>.txt</c> file using UTF-16 LE with BOM (<see cref="Encoding.Unicode"/>),
    /// matching Revit's canonical shared-parameter parser across English and Japanese Windows locales.
    /// </summary>
    public static void WriteUtf16LeBomIfChanged(string filePath, string content)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Shared parameter file path is required.", nameof(filePath));
        }

        Encoding utf16LeBom = Encoding.Unicode;
        byte[] preamble = utf16LeBom.GetPreamble();
        byte[] payload = utf16LeBom.GetBytes(content ?? string.Empty);
        byte[] expected = new byte[preamble.Length + payload.Length];
        Buffer.BlockCopy(preamble, 0, expected, 0, preamble.Length);
        Buffer.BlockCopy(payload, 0, expected, preamble.Length, payload.Length);

        if (File.Exists(filePath))
        {
            byte[] existing = File.ReadAllBytes(filePath);
            if (existing.Length == expected.Length)
            {
                bool same = true;
                for (int i = 0; i < existing.Length; i++)
                {
                    if (existing[i] != expected[i])
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    return;
                }
            }
        }

        File.WriteAllBytes(filePath, expected);
    }

    public void Dispose()
    {
        _guard.Dispose();
    }
}
