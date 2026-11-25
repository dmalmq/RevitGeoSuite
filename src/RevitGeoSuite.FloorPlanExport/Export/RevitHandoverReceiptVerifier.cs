using System;
using System.IO;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Export;

/// <summary>Verifies a handover receipt using only metadata from a saved local Revit project.</summary>
public static class RevitHandoverReceiptVerifier
{
    public static HandoverVerificationResult Verify(string receiptJson, string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath)
            || !string.Equals(Path.GetExtension(projectPath), ".rvt", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A saved .rvt project is required.", nameof(projectPath));
        }

        if (!File.Exists(projectPath))
        {
            return new HandoverVerificationResult(
                HandoverVerificationStatus.UnreadableDocument,
                "The target .rvt project file could not be found.");
        }

        BasicFileInfo? file;
        try
        {
            file = BasicFileInfo.Extract(projectPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Autodesk.Revit.Exceptions.ApplicationException)
        {
            return new HandoverVerificationResult(
                HandoverVerificationStatus.UnreadableDocument,
                "Could not read BasicFileInfo from the target .rvt project.");
        }

        if (file == null || !file.IsValidObject)
        {
            file?.Dispose();
            return new HandoverVerificationResult(
                HandoverVerificationStatus.UnreadableDocument,
                "Could not read BasicFileInfo from the target .rvt project.");
        }

        using (file)
        {
            DocumentVersion? version;
            try
            {
                version = file.GetDocumentVersion();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return new HandoverVerificationResult(
                    HandoverVerificationStatus.InvalidDocumentVersion,
                    "The target .rvt project does not expose a readable document version.");
            }

            if (version == null || !version.IsValidObject)
            {
                version?.Dispose();
                return new HandoverVerificationResult(
                    HandoverVerificationStatus.InvalidDocumentVersion,
                    "The target .rvt project does not expose a valid document version.");
            }

            using (version)
            {
                if (version.VersionGUID == Guid.Empty || version.NumberOfSaves < 0)
                {
                    return new HandoverVerificationResult(
                        HandoverVerificationStatus.InvalidDocumentVersion,
                        "The target .rvt project has an uninitialized document version or save count.");
                }

                return HandoverReceiptVerifier.Verify(receiptJson, version.VersionGUID, version.NumberOfSaves);
            }
        }
    }
}
