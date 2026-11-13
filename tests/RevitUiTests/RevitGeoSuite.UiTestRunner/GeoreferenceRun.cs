using System.Globalization;
using System.Text.Json.Nodes;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// RevitGeoSuite's own georeference record (GeoProjectInfo) for the run's copy. The GeoPackage export
/// does not read it: it writes Revit's shared coordinates with the EPSG it is given. The Cesium export
/// does ("3D Tiles export needs a saved CRS and origin"), so the scenarios that run it record it first,
/// the way a person clicks "Confirm existing setup" on the Georeference page: RGS stores the CRS and the
/// Survey Point and Project Base Point the model already has, and leaves Revit's coordinates as they are.
/// </summary>
internal sealed partial class GisStandardRun
{
    private const int LumineEpsg = 6677;

    // LUMINE EST sits on JR Shinjuku station; a Project Base Point the model places further away than
    // this means its Site Location is not the station, and the record would be wrong.
    private const double ShinjukuLatitude = 35.6896;
    private const double ShinjukuLongitude = 139.7006;
    private const double ShinjukuToleranceKm = 5;

    private async Task EnsureGeoreference(StepResult step)
    {
        JsonNode before = await Shell.RpcAsync("georeference.getCurrentState") ?? throw new InvalidOperationException("georeference.getCurrentState returned nothing.");
        step.Note($"Before: {Describe(before)}");
        int? stored = (int?)before["storedCrsEpsgCode"];
        if ((bool?)before["hasStoredGeoInfo"] == true)
        {
            step.Check(stored == LumineEpsg, $"The copy already has a georeference record, with EPSG:{stored?.ToString(CultureInfo.InvariantCulture) ?? "none"} (expected {LumineEpsg}).");
            return;
        }

        JsonNode? basePoint = before["projectBasePoint"];
        double? latitude = (double?)basePoint?["estimatedLatitudeDegrees"];
        double? longitude = (double?)basePoint?["estimatedLongitudeDegrees"];
        if (!step.Check(latitude.HasValue && longitude.HasValue && (double?)before["surveyPoint"]?["estimatedLatitudeDegrees"] != null,
                "The model's Site Location gives the Survey Point and Project Base Point a latitude and longitude."))
        {
            return;
        }

        double distance = DistanceKm(latitude!.Value, longitude!.Value, ShinjukuLatitude, ShinjukuLongitude);
        if (!step.Check(distance <= ShinjukuToleranceKm,
                $"The Project Base Point is {distance.ToString("0.0", CultureInfo.InvariantCulture)} km from Shinjuku station (at most {ShinjukuToleranceKm}); otherwise the Site Location is not LUMINE's and the record needs real values."))
        {
            return;
        }

        int beforeTransactions = host.LastEvent();
        JsonNode? applied = await Shell.RpcAsync("georeference.apply", new { crsCode = $"EPSG:{LumineEpsg}", selectedMeshCodes = Array.Empty<string>(), confirmExistingSetup = true });
        step.Note($"georeference.apply (confirm existing setup, EPSG:{LumineEpsg}): {applied?["message"]}");
        step.Note($"Transactions: {string.Join(", ", host.EventsAfter(beforeTransactions).Where(item => item.Kind == "commit"))}");

        JsonNode after = await Shell.RpcAsync("georeference.getCurrentState") ?? throw new InvalidOperationException("georeference.getCurrentState returned nothing.");
        step.Note($"After: {Describe(after)}");
        step.Check((bool?)after["hasStoredGeoInfo"] == true && (int?)after["storedCrsEpsgCode"] == LumineEpsg, $"The copy now has a georeference record with EPSG:{LumineEpsg}.");
        step.Check(JsonNode.DeepEquals(before["projectPosition"], after["projectPosition"]), "Revit's shared coordinates (project position) are unchanged.");
        step.Check(JsonNode.DeepEquals(before["projectBasePoint"], after["projectBasePoint"]) && JsonNode.DeepEquals(before["surveyPoint"], after["surveyPoint"]),
            "The Survey Point and Project Base Point are unchanged.");
    }

    /// <summary>The georef-read scenario: the values a georeferenced copy holds, to compare with C8.G's.</summary>
    private async Task ReadGeoreference(StepResult step)
    {
        JsonNode state = await Shell.RpcAsync("georeference.getCurrentState") ?? throw new InvalidOperationException("georeference.getCurrentState returned nothing.");
        step.Note(Describe(state));
        step.Note($"Raw: {state.ToJsonString()}");
        step.Check((bool?)state["hasStoredGeoInfo"] == true, "The model has a georeference record.");
    }

    private static string Describe(JsonNode state)
    {
        static string Point(JsonNode? point) => point == null ? "none" : string.Format(
            CultureInfo.InvariantCulture,
            "shared E {0} N {1} ft, lat {2} lon {3}",
            (double?)point["sharedEastWestFeet"], (double?)point["sharedNorthSouthFeet"], (double?)point["estimatedLatitudeDegrees"], (double?)point["estimatedLongitudeDegrees"]);

        JsonNode? position = state["projectPosition"];
        return string.Format(
            CultureInfo.InvariantCulture,
            "record {0} (EPSG {1}, origin {2}, {3}); site lat {4} lon {5}; project position E {6} N {7} elev {8} ft, angle {9}°; Survey Point {10}; Project Base Point {11}.",
            (bool?)state["hasStoredGeoInfo"] == true ? "stored" : "missing",
            (int?)state["storedCrsEpsgCode"], (double?)state["storedOriginLatitude"], (double?)state["storedOriginLongitude"],
            (double?)state["siteLatitudeDegrees"], (double?)state["siteLongitudeDegrees"],
            (double?)position?["eastWestFeet"], (double?)position?["northSouthFeet"], (double?)position?["elevationFeet"], (double?)position?["angleDegrees"],
            Point(state["surveyPoint"]), Point(state["projectBasePoint"]));
    }

    private static double DistanceKm(double latitudeA, double longitudeA, double latitudeB, double longitudeB)
    {
        const double EarthRadiusKm = 6371;
        double toRadians = Math.PI / 180;
        double dLatitude = (latitudeB - latitudeA) * toRadians;
        double dLongitude = (longitudeB - longitudeA) * toRadians;
        double a = Math.Pow(Math.Sin(dLatitude / 2), 2) + Math.Cos(latitudeA * toRadians) * Math.Cos(latitudeB * toRadians) * Math.Pow(Math.Sin(dLongitude / 2), 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Sqrt(a));
    }
}
