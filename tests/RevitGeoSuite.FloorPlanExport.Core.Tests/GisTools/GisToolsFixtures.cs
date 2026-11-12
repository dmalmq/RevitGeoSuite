using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

/// <summary>Models built by hand around the shipped standard, as the pyRevit tests built them.</summary>
internal static class GisToolsFixtures
{
    public static GisToolStandard Standard { get; } = GisToolStandard.LoadEmbedded();

    public static List<BoundParameterInfo> BoundAsStandard(params string[] skip) =>
        Standard.Parameters
            .Where(parameter => !skip.Contains(parameter.Name))
            .Select(parameter => new BoundParameterInfo(parameter.Name, parameter.Guid, parameter.Binding, parameter.Categories))
            .ToList();

    public static List<MaterialInfo> MaterialsAsStandard() =>
        Standard.Materials.Select((material, index) => new MaterialInfo(100 + index, material.Name, material.FillHex)).ToList();

    public static List<FloorTypeInfo> FloorTypesAsStandard() =>
        Standard.FloorTypes
            .Select((floorType, index) => new FloorTypeInfo(
                200 + index,
                floorType.Name,
                floorType.GisCategory,
                floorType.GisRestriction ?? string.Empty,
                floorType.Material,
                new long[] { 300 + index }))
            .ToList();

    public static ModelSnapshot ModelOnStandard(
        IEnumerable<BoundParameterInfo>? boundParameters = null,
        IEnumerable<FloorTypeInfo>? floorTypes = null,
        IEnumerable<FloorInfo>? floors = null,
        IEnumerable<MaterialInfo>? materials = null,
        IEnumerable<LevelInfo>? levels = null,
        IEnumerable<FamilyTypeInfo>? familyTypes = null,
        string? standardVersion = null) =>
        new(
            boundParameters ?? BoundAsStandard(),
            floorTypes ?? FloorTypesAsStandard(),
            floors,
            materials ?? MaterialsAsStandard(),
            levels ?? new[] { new LevelInfo(1, "1FL", 1, "1F") },
            familyTypes,
            standardVersion ?? Standard.Version.ToString());
}
