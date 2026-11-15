using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// Reads facts from a detached copy of a model and changes nothing: every level (name, level_id,
/// ProjectElevation and Elevation in metres) and every group type (instances, and the floors, rooms
/// and doors one instance holds). Writes model-facts.md and model-facts.json into the run folder.
/// Step ids MF.*.
/// </summary>
internal sealed partial class GisStandardRun
{
    private const double FeetToMetres = 0.3048;

    private async Task ModelFactsSteps()
    {
        JsonObject? levels = null, groups = null;
        await Step("MF.1", "Every level: name, level_id, ProjectElevation and Elevation in metres", step =>
        {
            levels = host.Call("levels");
            LevelRow[] rows = levels["levels"].Deserialize<LevelRow[]>(Web) ?? Array.Empty<LevelRow>();
            step.Check(rows.Length > 0, $"The model has levels ({rows.Length}).");
            foreach (LevelRow level in rows.OrderBy(level => level.ElevationFt))
            {
                step.Note(FormattableString.Invariant($"{level.Name} | level_id '{level.LevelId}' | ProjectElevation {level.ElevationFt * FeetToMetres:0.000} m | Elevation {level.LevelElevationFt * FeetToMetres:0.000} m | floors {(level.HasFloors ? "yes" : "no")}"));
            }

            return Task.CompletedTask;
        });
        await Step("MF.2", "Every group type: instances, and the floors, rooms and doors one instance holds", step =>
        {
            groups = host.Call("groups");
            GroupRow[] rows = groups["groups"].Deserialize<GroupRow[]>(Web) ?? Array.Empty<GroupRow>();
            foreach (GroupRow group in rows)
            {
                step.Note($"{group.Name} ({group.Kind}) | instances {group.Instances} | floors {group.Floors}, rooms {group.Rooms}, doors {group.Doors}, other {group.Other}");
            }

            GroupRow[] risky = rows.Where(group => group.Instances >= 2 && group.Floors + group.Rooms + group.Doors > 0).ToArray();
            step.Note(risky.Length == 0
                ? $"{rows.Length} group type(s); none with 2+ instances holds floors, rooms or doors."
                : $"{rows.Length} group type(s); {risky.Length} with 2+ instances hold floors, rooms or doors: {string.Join(", ", risky.Select(group => $"'{group.Name}' ×{group.Instances}"))}.");
            return Task.CompletedTask;
        });

        WriteModelFacts(levels, groups);
    }

    private void WriteModelFacts(JsonObject? levels, JsonObject? groups)
    {
        var facts = new JsonObject
        {
            ["model"] = options.Model,
            ["levels"] = levels?["levels"]?.DeepClone(),
            ["groups"] = groups?["groups"]?.DeepClone(),
        };
        File.WriteAllText(Path.Combine(options.OutputFolder, "model-facts.json"), facts.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var text = new StringBuilder($"# Model facts: {Path.GetFileName(options.Model)}\n\n## Levels\n\n| Name | level_id | ProjectElevation (m) | Elevation (m) | Floors |\n| --- | --- | --- | --- | --- |\n");
        foreach (LevelRow level in (levels?["levels"].Deserialize<LevelRow[]>(Web) ?? Array.Empty<LevelRow>()).OrderBy(level => level.ElevationFt))
        {
            text.Append(CultureInfo.InvariantCulture, $"| {level.Name} | {level.LevelId} | {level.ElevationFt * FeetToMetres:0.000} | {level.LevelElevationFt * FeetToMetres:0.000} | {(level.HasFloors ? "yes" : "no")} |\n");
        }

        text.Append("\n## Group types\n\n| Name | Kind | Instances | Floors | Rooms | Doors | Other |\n| --- | --- | --- | --- | --- | --- | --- |\n");
        foreach (GroupRow group in groups?["groups"].Deserialize<GroupRow[]>(Web) ?? Array.Empty<GroupRow>())
        {
            text.Append(CultureInfo.InvariantCulture, $"| {group.Name} | {group.Kind} | {group.Instances} | {group.Floors} | {group.Rooms} | {group.Doors} | {group.Other} |\n");
        }

        File.WriteAllText(Path.Combine(options.OutputFolder, "model-facts.md"), text.ToString());
    }
}

internal sealed record GroupRow(long Id, string Name, string? Kind, int Instances, int Floors, int Rooms, int Doors, int Other);
