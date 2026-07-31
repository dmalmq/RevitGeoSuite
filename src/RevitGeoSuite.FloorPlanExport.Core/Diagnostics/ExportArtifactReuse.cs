using System;
using System.IO;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.Diagnostics;

/// <summary>
/// Rules for reusing a previously published export artifact instead of regenerating it.
/// </summary>
/// <remarks>
/// This is pure path and file logic with no Revit dependency. It lives in Core - rather
/// than on the Revit-coupled exporter - so it stays exercisable from a test project that
/// runs without Revit installed.
/// </remarks>
public static class ExportArtifactReuse
{
    private static readonly string[] ShapefileComponentExtensions =
        { ".shp", ".shx", ".dbf", ".prj", ".cpg" };

    /// <summary>
    /// Returns the published artifact a staged export can be copied from, or null when the
    /// baseline holds no match that still exists on disk.
    /// </summary>
    /// <param name="packagingMode">
    /// The packaging mode's name. This is a string rather than an enum because that is how
    /// <see cref="ExportBaselineArtifactSnapshot.PackagingMode"/> persists it.
    /// </param>
    public static string? FindReusableArtifactPath(
        ExportBaselineSnapshot? baselineSnapshot,
        string artifactKey,
        string packagingMode)
    {
        ExportBaselineArtifactSnapshot? baselineArtifact = baselineSnapshot?.Artifacts.FirstOrDefault(artifact =>
            string.Equals(artifact.ArtifactKey, artifactKey, StringComparison.Ordinal) &&
            string.Equals(artifact.PackagingMode, packagingMode, StringComparison.Ordinal));
        return baselineArtifact != null && File.Exists(baselineArtifact.OutputFilePath)
            ? baselineArtifact.OutputFilePath
            : null;
    }

    /// <summary>
    /// Copies a reusable artifact into place, bringing the sidecar components along when the
    /// artifact is a shapefile (a .shp alone is not a usable dataset).
    /// </summary>
    public static void CopyReusableArtifact(string sourcePath, string destinationPath)
    {
        string source = Path.GetFullPath(sourcePath);
        string destination = Path.GetFullPath(destinationPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!source.EndsWith(".shp", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source, destination, overwrite: true);
            return;
        }

        foreach (string extension in ShapefileComponentExtensions)
        {
            string componentSource = Path.ChangeExtension(source, extension);
            if (File.Exists(componentSource))
            {
                File.Copy(componentSource, Path.ChangeExtension(destination, extension), overwrite: true);
            }
        }
    }

    /// <summary>
    /// Fingerprint contribution for the output format, so a format switch invalidates reuse
    /// of artifacts written in the previous format.
    /// </summary>
    public static string BuildOutputFormatFingerprintInput(ExportFormat outputFormat)
    {
        return $"outputFormat:{outputFormat}";
    }
}
