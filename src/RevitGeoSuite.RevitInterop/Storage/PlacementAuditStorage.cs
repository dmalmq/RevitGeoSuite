using System;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop;

namespace RevitGeoSuite.RevitInterop.Storage;

public sealed class PlacementAuditStorage
{
    private static readonly Guid SchemaGuid = new("18A444A0-CE9E-4C12-9945-873B5F521A41");
    private const string SchemaVersionFieldName = "SchemaVersion";
    private const string PayloadTypeFieldName = "PayloadType";
    private const string PayloadJsonFieldName = "PayloadJson";

    public void Save(IDocumentHandle document, PlacementAuditRecord record)
    {
        if (record is null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        RevitDocumentHandle handle = document as RevitDocumentHandle
            ?? throw new InvalidOperationException("Audit storage requires a RevitDocumentHandle.");
        Schema schema = GetOrCreateSchema();
        string payload = JsonConvert.SerializeObject(record);

        Entity entity = new Entity(schema);
        entity.Set(schema.GetField(SchemaVersionFieldName), 1);
        entity.Set(schema.GetField(PayloadTypeFieldName), nameof(PlacementAuditRecord));
        entity.Set(schema.GetField(PayloadJsonFieldName), payload);
        handle.Document.ProjectInformation.SetEntity(entity);
    }

    private static Schema GetOrCreateSchema()
    {
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is not null)
        {
            return schema;
        }

        SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName(RevitStorageSchemaNames.PlacementAudit);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(SchemaVersionFieldName, typeof(int));
        builder.AddSimpleField(PayloadTypeFieldName, typeof(string));
        builder.AddSimpleField(PayloadJsonFieldName, typeof(string));
        return builder.Finish();
    }
}
