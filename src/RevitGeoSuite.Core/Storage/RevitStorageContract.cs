namespace RevitGeoSuite.Core.Storage;

/// <summary>
/// Identifiers saved inside .rvt files. Existing models depend on these exact values, so they must
/// not change when the product or its namespaces are renamed.
/// </summary>
public static class RevitStorageContract
{
    public const string GeoProjectInfoSchemaGuid = "1AA3F739-27A4-4426-B9A6-B0A6D7088F8D";
    public const string ModuleStateSchemaGuid = "F6B06D1B-4A6A-4A51-912C-9EA4F61D565A";
    public const string PlacementAuditSchemaGuid = "18A444A0-CE9E-4C12-9945-873B5F521A41";
    public const string GeoreferenceUndoSchemaGuid = "A7E3B2C1-5D94-4F8A-B1E6-3C8A9D2F7E04";

    public const string GeoProjectInfoSchemaName = "RevitGeoSuiteGeoProjectInfo";
    public const string ModuleStateSchemaName = "RevitGeoSuiteModuleState";
    public const string PlacementAuditSchemaName = "RevitGeoSuiteGeoreferenceAudit";
    public const string GeoreferenceUndoSchemaName = "RevitGeoSuiteGeoreferenceUndo";

    public const string SchemaVersionFieldName = "SchemaVersion";
    public const string PayloadTypeFieldName = "PayloadType";
    public const string PayloadJsonFieldName = "PayloadJson";

    public const string GeoProjectInfoPayloadType = "GeoProjectInfo";
    public const string PlacementAuditPayloadType = "PlacementAuditRecord";
}
