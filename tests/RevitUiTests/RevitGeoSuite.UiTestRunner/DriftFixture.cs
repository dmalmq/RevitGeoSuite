using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// A one-row Kanizu drift file (schema kanizu.gis-drift v1, as in tests\Fixtures\Conformance\gis-drift):
/// the GDB changed one floor's name. Used when the model has no real drift file from Kanizu yet.
/// </summary>
internal static class DriftFixture
{
    public static string Write(string model, string uniqueId, string revitName, string gdbName)
    {
        var file = new JsonObject
        {
            ["schema"] = "kanizu.gis-drift",
            ["version"] = 1,
            ["model"] = model,
            ["appliedVersion"] = "revit/uitest/20261113T000000Z-00000000",
            ["rows"] = new JsonArray(new JsonObject
            {
                ["revitKey"] = $"{model}|{uniqueId}|0",
                ["uniqueId"] = uniqueId,
                ["layer"] = "unit",
                ["field"] = "name",
                ["parameter"] = "",
                ["guid"] = "",
                ["binding"] = "",
                ["label"] = revitName,
                ["revit"] = revitName,
                ["gdb"] = gdbName,
                ["base"] = revitName,
                ["conflict"] = false,
                ["gdbCategory"] = "",
                ["gdbRestriction"] = "",
            }),
            ["notPlaced"] = new JsonArray(),
            ["epsg"] = 6677,
            ["shapes"] = new JsonArray(),
        };
        return file.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
