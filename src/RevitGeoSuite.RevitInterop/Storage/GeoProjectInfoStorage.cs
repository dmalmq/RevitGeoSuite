using System;
using System.Diagnostics;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Versioning;
using RevitGeoSuite.RevitInterop;

namespace RevitGeoSuite.RevitInterop.Storage;

public sealed class GeoProjectInfoStorage : IGeoProjectInfoStore
{
    private static readonly Guid SchemaGuid = new(RevitStorageContract.GeoProjectInfoSchemaGuid);
    private const string SchemaVersionFieldName = RevitStorageContract.SchemaVersionFieldName;
    private const string PayloadTypeFieldName = RevitStorageContract.PayloadTypeFieldName;
    private const string PayloadJsonFieldName = RevitStorageContract.PayloadJsonFieldName;

    public void Save(IDocumentHandle document, GeoProjectInfo info)
    {
        if (info is null)
        {
            throw new ArgumentNullException(nameof(info));
        }

        RevitDocumentHandle handle = RequireRevitDocumentHandle(document);
        if (string.IsNullOrWhiteSpace(info.ModelId) || string.IsNullOrWhiteSpace(info.GisStationFolder))
        {
            // Re-georeferencing builds a fresh GeoProjectInfo; the model's id and its station
            // folder must survive it.
            GeoProjectInfo? previous = Load(document);
            if (string.IsNullOrWhiteSpace(info.ModelId))
            {
                info.ModelId = previous?.ModelId;
            }

            if (string.IsNullOrWhiteSpace(info.GisStationFolder))
            {
                info.GisStationFolder = previous?.GisStationFolder;
            }
        }

        Schema schema = GetOrCreateSchema();
        info.SchemaVersion = SchemaVersion.Current;

        StorageEnvelope<string> envelope = new StorageEnvelope<string>
        {
            SchemaVersion = SchemaVersion.Current,
            PayloadType = RevitStorageContract.GeoProjectInfoPayloadType,
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
        if (handle.Document.IsFamilyDocument || handle.Document.ProjectInformation is null)
        {
            return null;
        }

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

        try
        {
            GeoProjectInfo? info = JsonConvert.DeserializeObject<GeoProjectInfo>(payload);
            return info is null ? null : MigrationRunner.Migrate(info);
        }
        catch (JsonException ex)
        {
            Trace.WriteLine($"Ignoring corrupt geo project info payload because it could not be deserialized: {ex}");
            return null;
        }
    }

    public bool HasData(IDocumentHandle document)
    {
        RevitDocumentHandle handle = RequireRevitDocumentHandle(document);
        if (handle.Document.IsFamilyDocument || handle.Document.ProjectInformation is null)
        {
            return false;
        }

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

    public void Clear(IDocumentHandle document)
    {
        RevitDocumentHandle handle = RequireRevitDocumentHandle(document);
        if (handle.Document.IsFamilyDocument || handle.Document.ProjectInformation is null)
        {
            return;
        }

        if (!handle.Document.IsModifiable)
        {
            throw new InvalidOperationException("Clearing stored geo project info requires an active Revit transaction.");
        }

        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
        {
            return;
        }

        Entity entity = handle.Document.ProjectInformation.GetEntity(schema);
        if (!entity.IsValid())
        {
            return;
        }

        handle.Document.ProjectInformation.DeleteEntity(schema);
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
