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
        await Step("MF.2", "Every group type: instances, and what one instance holds by the categories the export writes to", step =>
        {
            groups = host.Call("groups");
            GroupRow[] rows = groups["groups"].Deserialize<GroupRow[]>(Web) ?? Array.Empty<GroupRow>();
            foreach (GroupRow group in rows)
            {
                step.Note($"{group.Name} (id {group.Id}, {group.Kind}) | instances {group.Instances} | {group.Members}");
            }

            GroupRow[] warn = rows.Where(group => group.Instances == 1 && group.Exported > 0).ToArray();
            GroupRow[] error = rows.Where(group => group.Instances >= 2 && group.Exported > 0).ToArray();
            step.Note($"{rows.Length} group type(s). With one instance holding exported categories (an export write warns 'A group has been changed outside group edit mode'): {List(warn)}.");
            step.Note($"With two or more instances holding exported categories (an export write is an error, or the value goes to every copy, so ids repeat): {List(error)}.");
            return Task.CompletedTask;

            static string List(GroupRow[] found) => found.Length == 0 ? "none" : string.Join(", ", found.Select(group => $"'{group.Name}' (id {group.Id}) ×{group.Instances}: {group.Members}"));
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

        text.Append("\n## Group types\n\nOne instance holding exported categories: an export write warns. Two or more: an error, or the value goes to every copy. Member ids are in model-facts.json.\n\n| Name | Id | Kind | Instances | Floors | Rooms | Doors | Stairs | Columns | Openings | Family instances | Other |\n| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |\n");
        foreach (GroupRow group in groups?["groups"].Deserialize<GroupRow[]>(Web) ?? Array.Empty<GroupRow>())
        {
            text.Append(CultureInfo.InvariantCulture, $"| {group.Name} | {group.Id} | {group.Kind} | {group.Instances} | {group.Floors} | {group.Rooms} | {group.Doors} | {group.Stairs} | {group.Columns} | {group.Openings} | {group.FamilyInstances} | {group.Other} |\n");
        }

        File.WriteAllText(Path.Combine(options.OutputFolder, "model-facts.md"), text.ToString());
    }
}

internal sealed record GroupRow(long Id, string Name, string? Kind, int Instances, int Floors, int Rooms, int Doors, int Stairs, int Columns, int Openings, int FamilyInstances, int Other)
{
    /// <summary>Members in the categories the export writes to (every one but other).</summary>
    public int Exported => Floors + Rooms + Doors + Stairs + Columns + Openings + FamilyInstances;

    public string Members => $"floors {Floors}, rooms {Rooms}, doors {Doors}, stairs {Stairs}, columns {Columns}, openings {Openings}, family instances {FamilyInstances}, other {Other}";
}
