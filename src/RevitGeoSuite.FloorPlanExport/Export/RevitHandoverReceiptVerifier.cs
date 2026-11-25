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

        using BasicFileInfo file = BasicFileInfo.Extract(projectPath);
        using DocumentVersion version = file.GetDocumentVersion();
        return HandoverReceiptVerifier.Verify(receiptJson, version.VersionGUID, version.NumberOfSaves);
    }
}
