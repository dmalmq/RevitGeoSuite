using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.RevitInterop;

namespace RevitGeoSuite.RevitInterop.Storage;

public sealed class ModuleStateStorage : IModuleStateStore
{
    private static readonly Guid SchemaGuid = new("F6B06D1B-4A6A-4A51-912C-9EA4F61D565A");
    internal const int CurrentSchemaVersion = 1;
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
            SchemaVersion = CurrentSchemaVersion,
            PayloadType = typeof(TState).FullName ?? typeof(TState).Name,
            Payload = JsonConvert.SerializeObject(state)
        };

        Entity entity = new Entity(schema);
        entity.Set(schema.GetField(SchemaVersionFieldName), CurrentSchemaVersion);
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
        if (!entries.TryGetValue(moduleId, out StorageEnvelope<string>? entry))
        {
            return null;
        }

        if (!ModuleStateStorageSerializer.TryDeserializeState(entry, out TState? state, out string? reason))
        {
            Trace.WriteLine($"Skipping stored module state for '{moduleId}' because {reason}.");
            return null;
        }

        return state;
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
            && ModuleStateStorageSerializer.HasUsablePayload(entry);
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
        return ModuleStateStorageSerializer.DeserializeEntries(payloadJson);
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

internal static class ModuleStateStorageSerializer
{
    public static Dictionary<string, StorageEnvelope<string>> DeserializeEntries(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new Dictionary<string, StorageEnvelope<string>>(StringComparer.Ordinal);
        }

        try
        {
            return JsonConvert.DeserializeObject<Dictionary<string, StorageEnvelope<string>>>(payloadJson)
                ?? new Dictionary<string, StorageEnvelope<string>>(StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            Trace.WriteLine($"Ignoring corrupt module state payload because it could not be deserialized: {ex}");
            return new Dictionary<string, StorageEnvelope<string>>(StringComparer.Ordinal);
        }
    }

    public static bool HasUsablePayload(StorageEnvelope<string>? entry)
    {
        return entry is not null
            && entry.SchemaVersion == ModuleStateStorage.CurrentSchemaVersion
            && !string.IsNullOrWhiteSpace(entry.PayloadType)
            && !string.IsNullOrWhiteSpace(entry.Payload);
    }

    public static bool TryDeserializeState<TState>(
        StorageEnvelope<string>? entry,
        out TState? state,
        out string? reason)
        where TState : class
    {
        state = null;

        if (entry is null)
        {
            reason = "no payload entry was found";
            return false;
        }

        if (entry.SchemaVersion != ModuleStateStorage.CurrentSchemaVersion)
        {
            reason = $"schema version {entry.SchemaVersion} is not supported";
            return false;
        }

        if (!IsExpectedPayloadType<TState>(entry.PayloadType))
        {
            reason = $"payload type '{entry.PayloadType}' does not match '{typeof(TState).FullName ?? typeof(TState).Name}'";
            return false;
        }

        if (string.IsNullOrWhiteSpace(entry.Payload))
        {
            reason = "payload JSON was blank";
            return false;
        }

        try
        {
            state = JsonConvert.DeserializeObject<TState>(entry.Payload!);
            if (state is null)
            {
                reason = "payload deserialized to null";
                return false;
            }

            reason = null;
            return true;
        }
        catch (JsonException ex)
        {
            reason = $"payload JSON could not be deserialized: {ex.Message}";
            return false;
        }
    }

    private static bool IsExpectedPayloadType<TState>(string payloadType)
    {
        if (string.IsNullOrWhiteSpace(payloadType))
        {
            return false;
        }

        string fullName = typeof(TState).FullName ?? typeof(TState).Name;
        string name = typeof(TState).Name;
        return string.Equals(payloadType, fullName, StringComparison.Ordinal)
            || string.Equals(payloadType, name, StringComparison.Ordinal);
    }
}
