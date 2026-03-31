using System;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Versioning;
using RevitGeoSuite.RevitInterop;

namespace RevitGeoSuite.RevitInterop.Storage;

public sealed class GeoProjectInfoStorage : IGeoProjectInfoStore
{
    private static readonly Guid SchemaGuid = new("1AA3F739-27A4-4426-B9A6-B0A6D7088F8D");
    private const string SchemaVersionFieldName = "SchemaVersion";
    private const string PayloadTypeFieldName = "PayloadType";
    private const string PayloadJsonFieldName = "PayloadJson";

    public void Save(IDocumentHandle document, GeoProjectInfo info)
    {
        if (info is null)
        {
            throw new ArgumentNullException(nameof(info));
        }

        RevitDocumentHandle handle = RequireRevitDocumentHandle(document);
        Schema schema = GetOrCreateSchema();
        info.SchemaVersion = SchemaVersion.Current;

        StorageEnvelope<string> envelope = new StorageEnvelope<string>
        {
            SchemaVersion = SchemaVersion.Current,
            PayloadType = nameof(GeoProjectInfo),
            Payload = JsonConvert.SerializeObject(info)
        };

        Entity entity = new Entity(schema);
        entity.Set(schema.GetField(SchemaVersionFieldName), envelope.SchemaVersion);
        entity.Set(schema.GetField(PayloadTypeFieldName), envelope.PayloadType);
        entity.Set(schema.GetField(PayloadJsonFieldName), envelope.Payload ?? string.Empty);
        handle.Document.ProjectInformation.SetEntity(entity);
    }

    public GeoProjectInfo? Load(IDocumentHandle document)
    {
        RevitDocumentHandle handle = RequireRevitDocumentHandle(document);
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
        {
            return null;
        }

        Entity entity = handle.Document.ProjectInformation.GetEntity(schema);
        if (!entity.IsValid())
        {
            return null;
        }

        string payload = entity.Get<string>(schema.GetField(PayloadJsonFieldName));
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        GeoProjectInfo? info = JsonConvert.DeserializeObject<GeoProjectInfo>(payload);
        return info is null ? null : MigrationRunner.Migrate(info);
    }

    public bool HasData(IDocumentHandle document)
    {
        RevitDocumentHandle handle = RequireRevitDocumentHandle(document);
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
        {
            return false;
        }

        Entity entity = handle.Document.ProjectInformation.GetEntity(schema);
        if (!entity.IsValid())
        {
            return false;
        }

        string payload = entity.Get<string>(schema.GetField(PayloadJsonFieldName));
        return !string.IsNullOrWhiteSpace(payload);
    }

    private static RevitDocumentHandle RequireRevitDocumentHandle(IDocumentHandle document)
    {
        return document as RevitDocumentHandle
            ?? throw new InvalidOperationException("Revit document storage requires a RevitDocumentHandle.");
    }

    private static Schema GetOrCreateSchema()
    {
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is not null)
        {
            return schema;
        }

        SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName(RevitStorageSchemaNames.GeoProjectInfo);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(SchemaVersionFieldName, typeof(int));
        builder.AddSimpleField(PayloadTypeFieldName, typeof(string));
        builder.AddSimpleField(PayloadJsonFieldName, typeof(string));
        return builder.Finish();
    }
}
