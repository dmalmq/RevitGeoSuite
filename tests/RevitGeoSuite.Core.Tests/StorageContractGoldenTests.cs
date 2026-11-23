using RevitGeoSuite.Core.Storage;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

/// <summary>
/// These values are saved in customer .rvt files. A failure here means a change would orphan
/// stored data. Fix the change, and do not update the expected values.
/// </summary>
public sealed class StorageContractGoldenTests
{
    [Fact]
    public void Schema_guids_are_frozen()
    {
        Assert.Equal("1AA3F739-27A4-4426-B9A6-B0A6D7088F8D", RevitStorageContract.GeoProjectInfoSchemaGuid);
        Assert.Equal("F6B06D1B-4A6A-4A51-912C-9EA4F61D565A", RevitStorageContract.ModuleStateSchemaGuid);
        Assert.Equal("18A444A0-CE9E-4C12-9945-873B5F521A41", RevitStorageContract.PlacementAuditSchemaGuid);
        Assert.Equal("A7E3B2C1-5D94-4F8A-B1E6-3C8A9D2F7E04", RevitStorageContract.GeoreferenceUndoSchemaGuid);
    }

    [Fact]
    public void Schema_names_are_frozen()
    {
        Assert.Equal("RevitGeoSuiteGeoProjectInfo", RevitStorageContract.GeoProjectInfoSchemaName);
        Assert.Equal("RevitGeoSuiteModuleState", RevitStorageContract.ModuleStateSchemaName);
        Assert.Equal("RevitGeoSuiteGeoreferenceAudit", RevitStorageContract.PlacementAuditSchemaName);
        Assert.Equal("RevitGeoSuiteGeoreferenceUndo", RevitStorageContract.GeoreferenceUndoSchemaName);
    }

    [Fact]
    public void Field_names_and_envelope_payload_types_are_frozen()
    {
        Assert.Equal("SchemaVersion", RevitStorageContract.SchemaVersionFieldName);
        Assert.Equal("PayloadType", RevitStorageContract.PayloadTypeFieldName);
        Assert.Equal("PayloadJson", RevitStorageContract.PayloadJsonFieldName);
        Assert.Equal("GeoProjectInfo", RevitStorageContract.GeoProjectInfoPayloadType);
        Assert.Equal("PlacementAuditRecord", RevitStorageContract.PlacementAuditPayloadType);
    }

    [Fact]
    public void Module_state_ids_are_frozen()
    {
        Assert.Equal("RevitGeoSuite.Georeference", ModuleStateIds.Georeference);
        Assert.Equal("RevitGeoSuite.PlateauImport", ModuleStateIds.PlateauImport);
        Assert.Equal("RevitGeoSuite.Tiles3DExport", ModuleStateIds.Tiles3DExport);
        Assert.Equal("RevitGeoSuite.CityGmlExport", ModuleStateIds.CityGmlExport);
    }

    [Theory]
    [InlineData("RevitGeoSuite.Georeference", "RevitGeoSuite.Core.Workflow.GeoreferenceModuleState")]
    [InlineData("RevitGeoSuite.PlateauImport", "RevitGeoSuite.PlateauImport.PlateauImportState")]
    [InlineData("RevitGeoSuite.Tiles3DExport", "RevitGeoSuite.Tiles3DExport.Tiles3DExportState")]
    [InlineData("RevitGeoSuite.CityGmlExport", "RevitGeoSuite.CityGmlExport.CityGmlExportState")]
    public void Module_payload_types_are_frozen(string moduleId, string payloadType)
    {
        Assert.Equal(payloadType, ModuleStatePayloadTypes.ForModule(moduleId));
        Assert.True(ModuleStatePayloadTypes.Matches(moduleId, payloadType));
        Assert.True(ModuleStatePayloadTypes.Matches(moduleId, payloadType.Substring(payloadType.LastIndexOf('.') + 1)));
    }

    [Fact]
    public void Payload_types_do_not_match_another_module_or_an_unknown_module()
    {
        Assert.False(ModuleStatePayloadTypes.Matches(ModuleStateIds.PlateauImport, ModuleStatePayloadTypes.Georeference));
        Assert.False(ModuleStatePayloadTypes.Matches("Unknown.Module", ModuleStatePayloadTypes.Georeference));
        Assert.False(ModuleStatePayloadTypes.Matches(ModuleStateIds.Georeference, null));
        Assert.Throws<System.ArgumentException>(() => ModuleStatePayloadTypes.ForModule("Unknown.Module"));
    }
}
