using System;
using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Request to scan a GSI/Kiban (terrain) folder for the CityGML mesh set being exported. Kept as a
/// top-level public type so the shared <see cref="PlateauContextExportPipeline"/> and web shell
/// handlers can drive Kiban scans without UI-specific state.
/// </summary>
public sealed class KibanScanRequest
{
    public KibanScanRequest(string folderPath, IReadOnlyCollection<string> plateauSecondaryMeshCodes)
        : this(folderPath, plateauSecondaryMeshCodes, additionalGreenLandUseTokens: null)
    {
    }

    public KibanScanRequest(
        string folderPath,
        IReadOnlyCollection<string> plateauSecondaryMeshCodes,
        IReadOnlyCollection<string>? additionalGreenLandUseTokens)
    {
        FolderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
        PlateauSecondaryMeshCodes = plateauSecondaryMeshCodes ?? throw new ArgumentNullException(nameof(plateauSecondaryMeshCodes));
        AdditionalGreenLandUseTokens = additionalGreenLandUseTokens;
    }

    public string FolderPath { get; }

    public IReadOnlyCollection<string> PlateauSecondaryMeshCodes { get; }

    public IReadOnlyCollection<string>? AdditionalGreenLandUseTokens { get; }
}
