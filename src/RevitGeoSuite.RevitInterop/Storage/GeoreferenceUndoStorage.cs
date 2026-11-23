using System;
using System.Diagnostics;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Versioning;

namespace RevitGeoSuite.RevitInterop.Storage;

public sealed class GeoreferenceUndoStorage
{
    private static readonly Guid SchemaGuid = new(RevitStorageContract.GeoreferenceUndoSchemaGuid);
    private const string PayloadJsonFieldName = RevitStorageContract.PayloadJsonFieldName;

    public void Save(RevitDocumentHandle handle, GeoreferenceUndoSnapshot snapshot)
    {
        if (handle is null) throw new ArgumentNullException(nameof(handle));
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (handle.Document.IsFamilyDocument || handle.Document.ProjectInformation is null)
        {
            throw new InvalidOperationException("Georeference undo snapshots are not supported in family documents.");
        }

        if (!handle.Document.IsModifiable)
        {
            throw new InvalidOperationException("Saving a georeference undo snapshot requires an active Revit transaction.");
        }

        ValidateForRestore(snapshot);

        Schema schema = GetOrCreateSchema();
        string json = GeoreferenceUndoSnapshotSerializer.Serialize(snapshot);

        Entity entity = new Entity(schema);
        entity.Set(schema.GetField(PayloadJsonFieldName), json);
        handle.Document.ProjectInformation.SetEntity(entity);
    }

    public GeoreferenceUndoSnapshot? Load(RevitDocumentHandle handle)
    {
        if (handle is null) throw new ArgumentNullException(nameof(handle));
        if (handle.Document.IsFamilyDocument || handle.Document.ProjectInformation is null)
        {
            return null;
        }

        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null) return null;

        Entity entity = handle.Document.ProjectInformation.GetEntity(schema);
        if (!entity.IsValid()) return null;

        string payload = entity.Get<string>(schema.GetField(PayloadJsonFieldName));
        return GeoreferenceUndoSnapshotSerializer.TryDeserialize(payload, out GeoreferenceUndoSnapshot? snapshot)
            ? snapshot
            : null;
    }

    public bool HasSnapshot(RevitDocumentHandle handle)
    {
        if (handle is null) return false;
        return Load(handle) is not null;
    }

    public void Clear(RevitDocumentHandle handle)
    {
        if (handle is null) throw new ArgumentNullException(nameof(handle));
        if (handle.Document.IsFamilyDocument || handle.Document.ProjectInformation is null)
        {
            return;
        }

        if (!handle.Document.IsModifiable)
        {
            throw new InvalidOperationException("Clearing a georeference undo snapshot requires an active Revit transaction.");
        }

        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null) return;

        Entity entity = handle.Document.ProjectInformation.GetEntity(schema);
        if (!entity.IsValid()) return;

        handle.Document.ProjectInformation.DeleteEntity(schema);
    }

    public static void ValidateForRestore(GeoreferenceUndoSnapshot snapshot)
    {
        GeoreferenceUndoSnapshotSerializer.ValidateForRestore(snapshot);
    }

    public static double NormalizeAngleRadians(double angleRadians)
    {
        return GeoreferenceUndoSnapshotSerializer.NormalizeAngleRadians(angleRadians);
    }

    public static GeoreferenceModuleStateRevertAction ResolveModuleStateRevertAction(GeoreferenceUndoSnapshot snapshot)
    {
        return GeoreferenceUndoSnapshotSerializer.ResolveModuleStateRevertAction(snapshot);
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

public enum GeoreferenceModuleStateRevertAction
{
    LeaveUnchanged = 0,
    Restore = 1,
    Remove = 2
}

internal static class GeoreferenceUndoSnapshotSerializer
{
    private const double RadiansTolerance = 1e-9d;

    public static string Serialize(GeoreferenceUndoSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        return JsonConvert.SerializeObject(snapshot);
    }

    public static bool TryDeserialize(string? payloadJson, out GeoreferenceUndoSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            GeoreferenceUndoSnapshot? parsed = JsonConvert.DeserializeObject<GeoreferenceUndoSnapshot>(payloadJson!);
            if (parsed is null)
            {
                return false;
            }

            snapshot = MigrationRunner.Migrate(parsed);
            return true;
        }
        catch (JsonException ex)
        {
            Trace.WriteLine($"Ignoring corrupt georeference undo snapshot payload: {ex}");
            return false;
        }
    }

    public static void ValidateForRestore(GeoreferenceUndoSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        RequireFinite(snapshot.SiteLatitudeRadians, nameof(snapshot.SiteLatitudeRadians));
        RequireFinite(snapshot.SiteLongitudeRadians, nameof(snapshot.SiteLongitudeRadians));
        RequireFinite(snapshot.EastWestFeet, nameof(snapshot.EastWestFeet));
        RequireFinite(snapshot.NorthSouthFeet, nameof(snapshot.NorthSouthFeet));
        RequireFinite(snapshot.ElevationFeet, nameof(snapshot.ElevationFeet));
        RequireFinite(snapshot.AngleRadians, nameof(snapshot.AngleRadians));

        if (snapshot.SiteLatitudeRadians < -(Math.PI / 2.0d) - RadiansTolerance
            || snapshot.SiteLatitudeRadians > (Math.PI / 2.0d) + RadiansTolerance)
        {
            throw new InvalidOperationException("Undo snapshot contains an out-of-range site latitude.");
        }

        if (snapshot.SiteLongitudeRadians < -Math.PI - RadiansTolerance
            || snapshot.SiteLongitudeRadians > Math.PI + RadiansTolerance)
        {
            throw new InvalidOperationException("Undo snapshot contains an out-of-range site longitude.");
        }

        bool hasAnySurveyCoord = snapshot.SurveyPointXFeet.HasValue
            || snapshot.SurveyPointYFeet.HasValue
            || snapshot.SurveyPointZFeet.HasValue;
        if (hasAnySurveyCoord)
        {
            if (!snapshot.HasSurveyPointPosition())
            {
                throw new InvalidOperationException("Undo snapshot contains an incomplete Survey Point position.");
            }

            RequireFinite(snapshot.SurveyPointXFeet!.Value, nameof(snapshot.SurveyPointXFeet));
            RequireFinite(snapshot.SurveyPointYFeet!.Value, nameof(snapshot.SurveyPointYFeet));
            RequireFinite(snapshot.SurveyPointZFeet!.Value, nameof(snapshot.SurveyPointZFeet));
        }
    }

    public static double NormalizeAngleRadians(double angleRadians)
    {
        RequireFinite(angleRadians, nameof(angleRadians));
        if (angleRadians >= -Math.PI && angleRadians <= Math.PI)
        {
            return angleRadians;
        }

        return Math.Atan2(Math.Sin(angleRadians), Math.Cos(angleRadians));
    }

    public static GeoreferenceModuleStateRevertAction ResolveModuleStateRevertAction(GeoreferenceUndoSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (snapshot.PreviousModuleState is not null)
        {
            return GeoreferenceModuleStateRevertAction.Restore;
        }

        return snapshot.HasPreviousModuleStateSnapshot
            ? GeoreferenceModuleStateRevertAction.Remove
            : GeoreferenceModuleStateRevertAction.LeaveUnchanged;
    }

    private static void RequireFinite(double value, string fieldName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException($"Undo snapshot contains a non-finite value for '{fieldName}'.");
        }
    }
}
