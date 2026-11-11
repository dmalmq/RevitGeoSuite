using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.FloorPlanExport.Export;

/// <summary>
/// The model part of each feature's <see cref="RevitKey"/>: the model's stable id from
/// RevitGeoSuite, read from the model, or the document key when the model has none yet.
/// </summary>
public static class RevitModelKeyResolver
{
    // Only stable ids are cached, so a model that gets its id later is read again.
    private static readonly ConditionalWeakTable<Document, string> StableKeys = new();

    /// <summary>The model key of <paramref name="document"/>. Reads the model and never writes it.</summary>
    public static string Resolve(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (StableKeys.TryGetValue(document, out string? cached))
        {
            return cached;
        }

        string? modelId = null;
        try
        {
            modelId = new GeoProjectInfoStorage().Load(new RevitDocumentHandle(document))?.ModelId;
        }
        catch (Exception)
        {
            // A model whose georeference cannot be read keys its features by document instead.
        }

        string key = RevitKey.CreateModelKey(modelId, DocumentProjectKeyBuilder.Create(document));
        if (!RevitKey.IsDocumentModelKey(key))
        {
            StableKeys.Add(document, key);
        }

        return key;
    }

    /// <summary>
    /// Gives the host model a stable id when it has none, the same id the Cesium export writes,
    /// so that keys do not change between PCs. When the id cannot be saved the export goes on
    /// with document keys, and a warning says why.
    /// </summary>
    public static void EnsureHostModelId(Document hostDocument, ICollection<string> warnings)
    {
        if (hostDocument is null)
        {
            throw new ArgumentNullException(nameof(hostDocument));
        }

        if (!RevitKey.IsDocumentModelKey(Resolve(hostDocument)))
        {
            return;
        }

        List<string> reasons = new();
        string? modelId = null;
        try
        {
            modelId = new ModelIdentityService().GetOrCreate(hostDocument, reasons);
        }
        catch (Exception ex)
        {
            reasons.Add(ex.Message);
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            warnings?.Add(
                "revit_key uses this model's file path, which differs between PCs, because the model has no model id. " +
                string.Join(" ", reasons));
        }
    }
}
