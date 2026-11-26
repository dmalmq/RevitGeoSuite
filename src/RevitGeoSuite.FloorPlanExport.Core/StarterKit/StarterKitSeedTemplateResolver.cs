using System;
using System.Collections.Generic;
using System.IO;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>
/// Pure-C# seed <c>.rft</c> template resolver for Kanizu Starter Kit families supporting both English and
/// Japanese Revit Family Templates directories (with Generic Model fallbacks for procedural categories,
/// since <c>FamilyBuilder</c> assigns the target <c>FamilyCategory</c> in-document).
/// </summary>
public static class StarterKitSeedTemplateResolver
{
    private static readonly string[] DoorTemplateCandidates =
    {
        "Metric Door.rft",
        "Door.rft",
        "ドア(メートル単位).rft",
    };

    private static readonly string[] SpecialtyEquipmentTemplateCandidates =
    {
        "Metric Specialty Equipment.rft",
        "Specialty Equipment.rft",
        "特殊設備(メートル単位).rft",
        "Metric Generic Model.rft",
        "Generic Model.rft",
        "一般モデル(メートル単位).rft",
    };

    private static readonly string[] MechanicalEquipmentTemplateCandidates =
    {
        "Metric Mechanical Equipment.rft",
        "Mechanical Equipment.rft",
        "機械設備(メートル単位).rft",
        "Metric Generic Model.rft",
        "Generic Model.rft",
        "一般モデル(メートル単位).rft",
    };

    private static readonly string[] ColumnTemplateCandidates =
    {
        "Metric Column.rft",
        "Column.rft",
        "柱(メートル単位).rft",
        "Metric Generic Model.rft",
        "Generic Model.rft",
        "一般モデル(メートル単位).rft",
    };

    private static readonly string[] GenericModelTemplateCandidates =
    {
        "Metric Generic Model.rft",
        "Generic Model.rft",
        "一般モデル(メートル単位).rft",
    };

    /// <summary>Returns ordered candidate <c>.rft</c> filenames for a Starter Kit family spec.</summary>
    public static IReadOnlyList<string> GetCandidateTemplateFileNames(FamilyGeometrySpec familySpec)
    {
        if (familySpec is null)
        {
            throw new ArgumentNullException(nameof(familySpec));
        }

        return GetCandidateTemplateFileNames(familySpec.Category, familySpec.SeedKind);
    }

    /// <summary>Returns ordered candidate <c>.rft</c> filenames for a Revit BuiltInCategory name and seed kind.</summary>
    public static IReadOnlyList<string> GetCandidateTemplateFileNames(string category, string seedKind)
    {
        if (string.Equals(seedKind, FamilyGeometrySpec.SeedWallHostedDoor, StringComparison.Ordinal) ||
            string.Equals(category, "OST_Doors", StringComparison.Ordinal))
        {
            return Array.AsReadOnly(DoorTemplateCandidates);
        }

        if (string.Equals(category, "OST_SpecialityEquipment", StringComparison.Ordinal))
        {
            return Array.AsReadOnly(SpecialtyEquipmentTemplateCandidates);
        }

        if (string.Equals(category, "OST_MechanicalEquipment", StringComparison.Ordinal))
        {
            return Array.AsReadOnly(MechanicalEquipmentTemplateCandidates);
        }

        if (string.Equals(category, "OST_Columns", StringComparison.Ordinal))
        {
            return Array.AsReadOnly(ColumnTemplateCandidates);
        }

        return Array.AsReadOnly(GenericModelTemplateCandidates);
    }

    /// <summary>
    /// Resolves the on-disk <c>.rft</c> seed template path for <paramref name="familySpec"/> by checking
    /// optional per-ID / per-category overrides first, then scanning <paramref name="seedTemplatesDirectory"/>
    /// across English and Japanese candidate template filenames.
    /// </summary>
    public static string ResolveSeedTemplatePath(
        FamilyGeometrySpec familySpec,
        string seedTemplatesDirectory,
        IReadOnlyDictionary<string, string>? customSeedPaths = null)
    {
        if (familySpec is null)
        {
            throw new ArgumentNullException(nameof(familySpec));
        }

        if (string.IsNullOrWhiteSpace(seedTemplatesDirectory))
        {
            throw new ArgumentException("Seed templates directory is required.", nameof(seedTemplatesDirectory));
        }

        if (customSeedPaths != null)
        {
            if (customSeedPaths.TryGetValue(familySpec.Id, out string? byId) && !string.IsNullOrWhiteSpace(byId))
            {
                return VerifySeedExists(byId!, familySpec.Id);
            }

            if (customSeedPaths.TryGetValue(familySpec.Category, out string? byCategory) && !string.IsNullOrWhiteSpace(byCategory))
            {
                return VerifySeedExists(byCategory!, familySpec.Id);
            }
        }

        IReadOnlyList<string> candidateNames = GetCandidateTemplateFileNames(familySpec);
        foreach (string fileName in candidateNames)
        {
            string candidate = Path.Combine(seedTemplatesDirectory, fileName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(
            $"Could not find a seed .rft template for '{familySpec.Id}' ({familySpec.Category}) in '{seedTemplatesDirectory}'. Tried: {string.Join(", ", candidateNames)}.");
    }

    private static string VerifySeedExists(string path, string familyId)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"Configured seed .rft for '{familyId}' was not found at '{full}'.", full);
        }

        return full;
    }
}
