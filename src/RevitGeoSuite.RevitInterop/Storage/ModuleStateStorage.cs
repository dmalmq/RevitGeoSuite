using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Versioning;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop;

namespace RevitGeoSuite.RevitInterop.Storage;

public sealed class ModuleStateStorage : IModuleStateStore
{
    private static readonly Guid SchemaGuid = new(RevitStorageContract.ModuleStateSchemaGuid);
    internal const int CurrentSchemaVersion = 1;
    private const string SchemaVersionFieldName = RevitStorageContract.SchemaVersionFieldName;
    private const string PayloadJsonFieldName = RevitStorageContract.PayloadJsonFieldName;

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
        Dictionary<string, StorageEnvelope<string>> entries = LoadPayloadForWrite(handle.Document, schema);
        entries[moduleId] = new StorageEnvelope<string>
        {
            SchemaVersion = CurrentSchemaVersion,
            PayloadType = ModuleStatePayloadTypes.ForModule(moduleId),
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
        if (handle.Document.IsFamilyDocument || handle.Document.ProjectInformation is null)
        {
            return null;
        }

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

        if (!ModuleStateStorageSerializer.TryDeserializeState(entry, moduleId, out TState? state, out string? reason))
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
        if (handle.Document.IsFamilyDocument || handle.Document.ProjectInformation is null)
        {
            return false;
        }

        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
        {
            return false;
        }

        Dictionary<string, StorageEnvelope<string>> entries = LoadPayload(handle.Document, schema);
        return entries.TryGetValue(moduleId, out StorageEnvelope<string>? entry)
            && ModuleStateStorageSerializer.HasUsablePayload(entry);
    }

    public void Remove(IDocumentHandle document, string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            throw new ArgumentException("Module id cannot be empty.", nameof(moduleId));
        }

        RevitDocumentHandle handle = RequireRevitDocumentHandle(document);
        if (handle.Document.IsFamilyDocument || handle.Document.ProjectInformation is null)
        {
            return;
        }

        if (!handle.Document.IsModifiable)
        {
            throw new InvalidOperationException("Removing module state requires an active Revit transaction.");
        }

        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
        {
            return;
        }

        Entity existingEntity = handle.Document.ProjectInformation.GetEntity(schema);
        if (!existingEntity.IsValid())
        {
            return;
        }

        Dictionary<string, StorageEnvelope<string>> entries = LoadPayloadForWrite(handle.Document, schema);
        if (!ModuleStateStorageSerializer.RemoveEntry(entries, moduleId))
        {
            return;
        }

        Entity entity = new Entity(schema);
        entity.Set(schema.GetField(SchemaVersionFieldName), CurrentSchemaVersion);
        entity.Set(schema.GetField(PayloadJsonFieldName), JsonConvert.SerializeObject(entries));
        handle.Document.ProjectInformation.SetEntity(entity);
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

    private static Dictionary<string, StorageEnvelope<string>> LoadPayloadForWrite(Autodesk.Revit.DB.Document document, Schema schema)
    {
        Entity entity = document.ProjectInformation.GetEntity(schema);
        if (!entity.IsValid())
        {
            return new Dictionary<string, StorageEnvelope<string>>(StringComparer.Ordinal);
        }

        string payloadJson = entity.Get<string>(schema.GetField(PayloadJsonFieldName));
        if (!ModuleStateStorageSerializer.TryDeserializeEntries(payloadJson, out Dictionary<string, StorageEnvelope<string>> entries))
        {
            throw new InvalidOperationException(
                "Stored module state is corrupt. It was left unchanged because saving would erase the state of every module.");
        }

        return entries;
    }

    private static Schema GetOrCreateSchema()
    {
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is not null)
        {
            return schema;
        }

        SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName(RevitStorageSchemaNames.ModuleState);
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
        TryDeserializeEntries(payloadJson, out Dictionary<string, StorageEnvelope<string>> entries);
        return entries;
    }

    public static bool TryDeserializeEntries(
        string? payloadJson,
        out Dictionary<string, StorageEnvelope<string>> entries)
    {
        entries = new Dictionary<string, StorageEnvelope<string>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return true;
        }

        try
        {
            Dictionary<string, StorageEnvelope<string>>? stored =
                JsonConvert.DeserializeObject<Dictionary<string, StorageEnvelope<string>>>(payloadJson!);
            if (stored is not null)
            {
                entries = stored;
            }

            return true;
        }
        catch (JsonException ex)
        {
            Trace.WriteLine($"Ignoring corrupt module state payload because it could not be deserialized: {ex}");
            return false;
        }
    }

    public static bool RemoveEntry(Dictionary<string, StorageEnvelope<string>> entries, string moduleId)
    {
        if (entries is null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        if (string.IsNullOrWhiteSpace(moduleId))
        {
            throw new ArgumentException("Module id cannot be empty.", nameof(moduleId));
        }

        return entries.Remove(moduleId);
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
        string moduleId,
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

        if (!ModuleStatePayloadTypes.Matches(moduleId, entry.PayloadType))
        {
            reason = $"payload type '{entry.PayloadType}' does not match module '{moduleId}'";
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

            if (state is GeoreferenceModuleState georeferenceState)
            {
                MigrationRunner.Migrate(georeferenceState);
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
}
