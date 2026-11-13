using System;
using System.Globalization;
using System.IO;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.FloorPlanExport.GisTools;

/// <summary>Where Kanizu files this model's GDB edits, and the model values Review GDB edits compares them with.</summary>
public static class GisDriftSource
{
    /// <summary>
    /// The model key and station folder from the model's GeoProjectInfo, and the drift file Kanizu
    /// writes for it there. Empty strings when the model has no id or station folder yet.
    /// </summary>
    public static (string ModelKey, string StationFolder, string ExpectedPath) Locate(Document document)
    {
        GeoProjectInfo? info;
        try
        {
            info = new GeoProjectInfoStorage().Load(new RevitDocumentHandle(document));
        }
        catch (Exception)
        {
            info = null;
        }

        string modelKey = GisDrift.ModelKey(info?.ModelId);
        string station = GisToolStandard.Normalize(info?.GisStationFolder);
        if (modelKey.Length == 0 || station.Length == 0)
        {
            return (modelKey, station, string.Empty);
        }

        try
        {
            return (modelKey, station, Path.Combine(station, GisDrift.Folder, GisDrift.FileName(modelKey)));
        }
        catch (ArgumentException)
        {
            return (modelKey, station, string.Empty);
        }
    }

    /// <summary>The parameter a drift row writes: on the element, or on its type for a type binding.</summary>
    public static Parameter? Target(Document document, string uniqueId, string name, string binding)
    {
        Element? element = document.GetElement(uniqueId);
        if (element != null && binding == GisDrift.Type)
        {
            ElementId typeId = element.GetTypeId();
            element = typeId == ElementId.InvalidElementId ? null : document.GetElement(typeId);
        }

        return element?.LookupParameter(name);
    }

    /// <summary>A parameter's value as text for <see cref="GisDrift.Plan"/>, or null when the element or parameter is missing.</summary>
    public static string? CurrentValue(Document document, string uniqueId, string name, string binding)
    {
        Parameter? parameter = Target(document, uniqueId, name, binding);
        return parameter?.StorageType switch
        {
            StorageType.Integer => parameter.HasValue ? parameter.AsInteger().ToString(CultureInfo.InvariantCulture) : string.Empty,
            StorageType.String => GisToolStandard.Normalize(parameter.AsString()),
            _ => null,
        };
    }
}
