using System;
using System.Collections.Generic;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.RevitInterop;

namespace RevitGeoSuite.RevitInterop.Storage;

public sealed class ModuleStateStorage : IModuleStateStore
{
    private static readonly Guid SchemaGuid = new("F6B06D1B-4A6A-4A51-912C-9EA4F61D565A");
    private const string SchemaVersionFieldName = "SchemaVersion";
    private const string PayloadJsonFieldName = "PayloadJson";

    public void Save<TState>(IDocumentHandle document, string moduleId, TState state)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            throw new ArgumentException("Module id cannot be empty.", nameof(moduleId));
        }

        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        RevitDocumentHandle handle = RequireRevitDocumentHandle(document);
        Schema schema = GetOrCreateSchema();
        Dictionary<string, StorageEnvelope<string>> entries = LoadPayload(handle.Document, schema);
        entries[moduleId] = new StorageEnvelope<string>
        {
            SchemaVersion = 1,
            PayloadType = typeof(TState).FullName ?? typeof(TState).Name,
            Payload = JsonConvert.SerializeObject(state)
        };

        Entity entity = new Entity(schema);
        entity.Set(schema.GetField(SchemaVersionFieldName), 1);
        entity.Set(schema.GetField(PayloadJsonFieldName), JsonConvert.SerializeObject(entries));
        handle.Document.ProjectInformation.SetEntity(entity);
    }

    public TState? Load<TState>(IDocumentHandle document, string moduleId) where TState : class
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            throw new ArgumentException("Module id cannot be empty.", nameof(moduleId));
        }

        RevitDocumentHandle handle = RequireRevitDocumentHandle(document);
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
        {
            return null;
        }

        Dictionary<string, StorageEnvelope<string>> entries = LoadPayload(handle.Document, schema);
        if (!entries.TryGetValue(moduleId, out StorageEnvelope<string>? entry) || string.IsNullOrWhiteSpace(entry.Payload))
        {
            return null;
        }

        string payload = entry.Payload!;
        return JsonConvert.DeserializeObject<TState>(payload);
    }

    public bool HasState(IDocumentHandle document, string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            throw new ArgumentException("Module id cannot be empty.", nameof(moduleId));
        }

        RevitDocumentHandle handle = RequireRevitDocumentHandle(document);
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
        {
            return false;
        }

        Dictionary<string, StorageEnvelope<string>> entries = LoadPayload(handle.Document, schema);
        return entries.TryGetValue(moduleId, out StorageEnvelope<string>? entry)
            && !string.IsNullOrWhiteSpace(entry.Payload);
    }

    private static RevitDocumentHandle RequireRevitDocumentHandle(IDocumentHandle document)
    {
        return document as RevitDocumentHandle
            ?? throw new InvalidOperationException("Module state storage requires a RevitDocumentHandle.");
    }

    private static Dictionary<string, StorageEnvelope<string>> LoadPayload(Autodesk.Revit.DB.Document document, Schema schema)
    {
        Entity entity = document.ProjectInformation.GetEntity(schema);
        if (!entity.IsValid())
        {
            return new Dictionary<string, StorageEnvelope<string>>(StringComparer.Ordinal);
        }

        string payloadJson = entity.Get<string>(schema.GetField(PayloadJsonFieldName));
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new Dictionary<string, StorageEnvelope<string>>(StringComparer.Ordinal);
        }

        return JsonConvert.DeserializeObject<Dictionary<string, StorageEnvelope<string>>>(payloadJson)
            ?? new Dictionary<string, StorageEnvelope<string>>(StringComparer.Ordinal);
    }

    private static Schema GetOrCreateSchema()
    {
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is not null)
        {
            return schema;
        }

        SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName("RevitGeoSuiteModuleState");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(SchemaVersionFieldName, typeof(int));
        builder.AddSimpleField(PayloadJsonFieldName, typeof(string));
        return builder.Finish();
    }
}



