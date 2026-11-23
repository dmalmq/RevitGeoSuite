using System;
using System.Collections.Generic;

namespace RevitGeoSuite.Core.Storage;

/// <summary>
/// The payload type saved with each module's state. These are the original CLR names, frozen as
/// text: new builds keep writing them so older add-ins can still read the same model.
/// </summary>
public static class ModuleStatePayloadTypes
{
    public const string Georeference = "RevitGeoSuite.Core.Workflow.GeoreferenceModuleState";
    public const string PlateauImport = "RevitGeoSuite.PlateauImport.PlateauImportState";
    public const string Tiles3DExport = "RevitGeoSuite.Tiles3DExport.Tiles3DExportState";
    public const string CityGmlExport = "RevitGeoSuite.CityGmlExport.CityGmlExportState";

    private static readonly Dictionary<string, string> ByModuleId = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ModuleStateIds.Georeference] = Georeference,
        [ModuleStateIds.PlateauImport] = PlateauImport,
        [ModuleStateIds.Tiles3DExport] = Tiles3DExport,
        [ModuleStateIds.CityGmlExport] = CityGmlExport
    };

    public static string ForModule(string moduleId)
    {
        return ByModuleId.TryGetValue(moduleId, out string? payloadType)
            ? payloadType!
            : throw new ArgumentException($"Module '{moduleId}' has no frozen payload type.", nameof(moduleId));
    }

    /// <summary>Accepts the frozen full name and the short name that earlier builds also accepted.</summary>
    public static bool Matches(string moduleId, string? storedPayloadType)
    {
        if (storedPayloadType is null || !ByModuleId.TryGetValue(moduleId, out string? frozen))
        {
            return false;
        }

        string shortName = frozen!.Substring(frozen.LastIndexOf('.') + 1);
        return string.Equals(storedPayloadType, frozen, StringComparison.Ordinal)
            || string.Equals(storedPayloadType, shortName, StringComparison.Ordinal);
    }
}
