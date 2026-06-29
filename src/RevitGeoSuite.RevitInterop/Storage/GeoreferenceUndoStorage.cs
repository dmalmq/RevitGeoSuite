using System;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;
using RevitGeoSuite.Core.ProjectMetadata;

namespace RevitGeoSuite.RevitInterop.Storage;

public sealed class GeoreferenceUndoStorage
{
    private static readonly Guid SchemaGuid = new("A7E3B2C1-5D94-4F8A-B1E6-3C8A9D2F7E04");
    private const string PayloadJsonFieldName = "PayloadJson";

    public void Save(RevitDocumentHandle handle, GeoreferenceUndoSnapshot snapshot)
    {
        if (handle is null) throw new ArgumentNullException(nameof(handle));
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

        Schema schema = GetOrCreateSchema();
        string json = JsonConvert.SerializeObject(snapshot);

        Entity entity = new Entity(schema);
        entity.Set(schema.GetField(PayloadJsonFieldName), json);
        handle.Document.ProjectInformation.SetEntity(entity);
    }

    public GeoreferenceUndoSnapshot? Load(RevitDocumentHandle handle)
    {
        if (handle is null) throw new ArgumentNullException(nameof(handle));

        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null) return null;

        Entity entity = handle.Document.ProjectInformation.GetEntity(schema);
        if (!entity.IsValid()) return null;

        string payload = entity.Get<string>(schema.GetField(PayloadJsonFieldName));
        if (string.IsNullOrWhiteSpace(payload)) return null;

        return JsonConvert.DeserializeObject<GeoreferenceUndoSnapshot>(payload);
    }

    public bool HasSnapshot(RevitDocumentHandle handle)
    {
        if (handle is null) return false;

        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null) return false;

        Entity entity = handle.Document.ProjectInformation.GetEntity(schema);
        if (!entity.IsValid()) return false;

        string payload = entity.Get<string>(schema.GetField(PayloadJsonFieldName));
        return !string.IsNullOrWhiteSpace(payload);
    }

    public void Clear(RevitDocumentHandle handle)
    {
        if (handle is null) throw new ArgumentNullException(nameof(handle));

        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null) return;

        Entity entity = handle.Document.ProjectInformation.GetEntity(schema);
        if (!entity.IsValid()) return;

        Entity empty = new Entity(schema);
        empty.Set(schema.GetField(PayloadJsonFieldName), string.Empty);
        handle.Document.ProjectInformation.SetEntity(empty);
    }

    private static Schema GetOrCreateSchema()
    {
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is not null) return schema;

        SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName(RevitStorageSchemaNames.GeoreferenceUndo);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(PayloadJsonFieldName, typeof(string));
        return builder.Finish();
    }
}
