using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.ProjectMetadata;

namespace RevitGeoSuite.RevitInterop.Storage;

/// <summary>
/// Reads the model's stable <see cref="GeoProjectInfo.ModelId"/>, writing a new one into the
/// model the first time it is needed. Must run in a Revit API context with no open transaction.
/// </summary>
public sealed class ModelIdentityService
{
    private readonly GeoProjectInfoStorage storage;

    public ModelIdentityService(GeoProjectInfoStorage? storage = null)
    {
        this.storage = storage ?? new GeoProjectInfoStorage();
    }

    /// <summary>
    /// The model id, or null when the model has no georeference yet or cannot be written to
    /// (read-only, or the transaction failed). A warning says why in that case.
    /// </summary>
    public string? GetOrCreate(Document document, ICollection<string> warnings)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var handle = new RevitDocumentHandle(document);
        GeoProjectInfo? info = storage.Load(handle);
        if (info is null)
        {
            warnings?.Add("The model has no georeference yet, so it has no model id. Georeference it to give it one.");
            return null;
        }

        if (!info.EnsureModelId())
        {
            return info.ModelId;
        }

        if (document.IsReadOnly || document.IsModifiable)
        {
            warnings?.Add("The model id could not be saved because the model is read-only or busy; this package carries none.");
            return null;
        }

        using Transaction transaction = new Transaction(document, "Save Model Id");
        transaction.Start();
        storage.Save(handle, info);
        if (transaction.Commit() != TransactionStatus.Committed)
        {
            warnings?.Add("Revit did not commit the model id; this package carries none.");
            return null;
        }

        return info.ModelId;
    }
}
