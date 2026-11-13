using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

/// <summary>
/// Geometry Z is in Revit's internal coordinates, so a height test against it must use
/// Level.ProjectElevation. Level.Elevation is measured from the project base point or survey
/// point: LUMINE EST's levels read 37.71 m there while their geometry sits near Z 0, and every
/// comparison that mixed the two silently dropped openings, model lines and walls on that model.
/// Level.Elevation is still right for sorting levels and for reporting a level's real height.
/// </summary>
public sealed class LevelElevationUsageTests
{
    private static readonly Regex LevelElevation = new(@"[Ll]evels?(\[\w+\])?[!?]?\.Elevation\b", RegexOptions.Compiled);

    // Uses that never meet geometry Z, by file and a piece of the line.
    private static readonly (string File, string Line, string Reason)[] Allowed =
    {
        ("RevitGeoSuite.Shell/Handlers/UnitFloorsHandlers.cs", "ElevationFeet = level.Elevation", "level picker label"),
        ("RevitGeoSuite.Shell/Handlers/GisImportHandler.cs", "ElevationFeet = level.Elevation", "level picker label"),
        ("RevitGeoSuite.Shell/Handlers/GisImportHandler.cs", "new GisExportLevel(level.Id.Value, level.Name ?? string.Empty, level.Elevation)", "sorts groups by level"),
        ("RevitGeoSuite.FloorPlanExport/Extractors/LevelBoundaryBuilder.cs", "[\"elevation_m\"] = level.Elevation * FeetToMeters,", "the level's real height"),
        ("RevitGeoSuite.FloorPlanExport/Export/FloorExportDataPreparer.cs", "new LevelOrdinalInput(level.Id.Value, level.Name ?? string.Empty, level.Elevation,", "level height order for ordinals"),
        ("RevitGeoSuite.FloorPlanExport/Export/Temp3DViewScope.cs", "level.Elevation,", "diagnostic beside ProjectElevation"),
    };

    [Fact]
    public void GeometryIsComparedWithProjectElevation()
    {
        string sourceRoot = TestPaths.GetRepoPath("src");
        List<string> problems = new();

        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            string relative = file.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.Split('/').Any(part => part is "obj" or "bin"))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal) || !LevelElevation.IsMatch(line) || line.Contains(".OrderBy(") || IsAllowed(relative, line))
                {
                    continue;
                }

                problems.Add($"{relative}:{index + 1}: {line}");
            }
        }

        Assert.True(
            problems.Count == 0,
            "Compare geometry Z with Level.ProjectElevation, not Level.Elevation. If a line only labels, sorts or " +
            "reports a level's real height, add it to LevelElevationUsageTests.Allowed with the reason:" +
            Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    private static bool IsAllowed(string file, string line) =>
        Allowed.Any(entry => entry.File == file && line.Contains(entry.Line));
}
