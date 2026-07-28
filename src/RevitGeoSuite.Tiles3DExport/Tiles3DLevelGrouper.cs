using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DLevelGrouper
{
    public IReadOnlyList<Tiles3DLevelGroup> Group(IReadOnlyList<Tiles3DMeshPrimitive> meshes)
    {
        if (meshes is null || meshes.Count == 0)
        {
            return Array.Empty<Tiles3DLevelGroup>();
        }

        Dictionary<string, List<Tiles3DMeshPrimitive>> byLevel = new Dictionary<string, List<Tiles3DMeshPrimitive>>(StringComparer.Ordinal);
        Dictionary<string, string> nameByLevel = new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, double> elevationByLevel = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (Tiles3DMeshPrimitive mesh in meshes)
        {
            string key = string.IsNullOrWhiteSpace(mesh.Metadata.LevelKey)
                ? Tiles3DLevelMetadata.BuildLevelKey(mesh.Metadata.LevelName)
                : mesh.Metadata.LevelKey;
            if (!byLevel.TryGetValue(key, out List<Tiles3DMeshPrimitive>? list))
            {
                list = new List<Tiles3DMeshPrimitive>();
                byLevel[key] = list;
                nameByLevel[key] = string.IsNullOrWhiteSpace(mesh.Metadata.LevelName)
                    ? Tiles3DLevelMetadata.UnassignedLevelName
                    : mesh.Metadata.LevelName;
                elevationByLevel[key] = mesh.Metadata.LevelElevationMeters;
            }

            list.Add(mesh);
        }

        List<Tiles3DLevelGroup> result = new List<Tiles3DLevelGroup>();

        List<string> namedKeys = byLevel.Keys
            .Where(k => !string.Equals(k, Tiles3DLevelMetadata.UnassignedLevelKey, StringComparison.Ordinal))
            .OrderBy(k => elevationByLevel[k])
            .ThenBy(k => nameByLevel[k], StringComparer.Ordinal)
            .ToList();

        foreach (string key in namedKeys)
        {
            result.Add(BuildGroup(key, nameByLevel[key], elevationByLevel[key], byLevel[key]));
        }

        if (byLevel.TryGetValue(Tiles3DLevelMetadata.UnassignedLevelKey, out List<Tiles3DMeshPrimitive>? unassigned))
        {
            result.Add(BuildGroup(
                Tiles3DLevelMetadata.UnassignedLevelKey,
                Tiles3DLevelMetadata.UnassignedLevelName,
                0d,
                unassigned));
        }

        return result;
    }

    private static Tiles3DLevelGroup BuildGroup(
        string levelKey,
        string levelName,
        double levelElevationMeters,
        List<Tiles3DMeshPrimitive> meshes)
    {
        double minZ = 0d;
        double maxZ = 0d;
        if (meshes.Count > 0)
        {
            minZ = meshes.Min(mesh => mesh.Metadata.MinZMeters);
            maxZ = meshes.Max(mesh => mesh.Metadata.MaxZMeters);
        }

        return new Tiles3DLevelGroup
        {
            LevelKey = levelKey,
            LevelName = levelName,
            LevelElevationMeters = levelElevationMeters,
            MinZMeters = minZ,
            MaxZMeters = maxZ,
            Meshes = meshes
        };
    }
}
