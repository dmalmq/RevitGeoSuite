using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Export;

/// <summary>
/// Decides where a model's category rules and shared profiles live: the station's
/// <c>gis-rules.json</c> when the model has a station folder and model id, the same test
/// <see cref="GisModelSettings"/> makes for <c>gis-model.json</c>, else this PC's own stores.
/// </summary>
public static class GisRulesSourceFactory
{
    public static GisRulesSource For(Document document)
    {
        (string stationFolder, _) = GisModelSettings.ReadStation(document);
        return GisRulesSource.Resolve(stationFolder, DocumentProjectKeyBuilder.Create(document));
    }
}
