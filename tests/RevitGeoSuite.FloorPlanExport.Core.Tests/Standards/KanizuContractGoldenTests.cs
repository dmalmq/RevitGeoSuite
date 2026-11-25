using Newtonsoft.Json.Linq;
using RevitGeoSuite.CesiumHandoff;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.UnitFloors;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

/// <summary>
/// Golden contract guards for Kanizu interoperability. These schema IDs, generator names,
/// shared parameter group names, and JSON field names are consumed by Kanizu conformance
/// suites and existing station folders. Never modify them during branding updates.
/// </summary>
public sealed class KanizuContractGoldenTests
{
    [Fact]
    public void All_five_revitgeosuite_json_schemas_are_frozen()
    {
        Assert.Equal("revitgeosuite.cesium-package", CesiumPackageManifest.SchemaId);
        Assert.Equal("revitgeosuite.gis-model", GisModelFile.SchemaName);
        Assert.Equal("revitgeosuite.gis-rules", GisRulesFile.SchemaName);
        Assert.Equal("revitgeosuite.gis-fields", GisFieldList.SchemaName);
        Assert.Equal("revitgeosuite.unit-floors", UnitFloorLinkFile.Format);
    }

    [Fact]
    public void All_three_kanizu_gis_schemas_are_frozen()
    {
        Assert.Equal("kanizu.gis-drift", GisDrift.Schema);
        Assert.Equal("kanizu.gis-keep", GisKeep.Schema);
        Assert.Equal("kanizu.gis-network", GisNetwork.Schema);
    }

    [Fact]
    public void Json_generator_name_and_version_keys_are_frozen()
    {
        CesiumPackageGenerator generator = new();
        Assert.Equal("RevitGeoSuite", generator.Name);

        JObject modelVersions = JObject.FromObject(new GisModelVersions { LastExportRevitGeoSuite = "1.3.0.0" });
        Assert.Equal("1.3.0.0", modelVersions.Value<string>("lastExportRevitGeoSuite"));

        JObject rulesChanged = JObject.FromObject(new GisRulesChanged { RevitGeoSuite = "1.3.0.0" });
        Assert.Equal("1.3.0.0", rulesChanged.Value<string>("revitGeoSuite"));
    }

    [Fact]
    public void Shared_parameter_group_name_is_frozen()
    {
        Assert.Equal("RevitGeoSuite FloorPlanExport", GisSharedParameters.GroupName);
    }
}
