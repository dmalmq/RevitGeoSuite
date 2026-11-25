using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Export;

namespace RevitGeoSuite.FloorPlanExport;

internal static class OpeningFamilyClassifier
{
    public static bool IsAcceptedOpening(FamilyInstance instance)
    {
        return IsAcceptedOpening(instance, null, null);
    }

    /// <summary>Pre-v3 acceptance: doors, windows, the elevator-door family name and explicitly accepted families.</summary>
    public static bool IsAcceptedOpening(FamilyInstance instance, IReadOnlyList<string>? additionalAcceptedFamilies)
    {
        return IsAcceptedOpening(instance, additionalAcceptedFamilies, null);
    }

    /// <summary>
    /// Accepts doors, windows, elevator doors and explicitly accepted families. On standard v3+ a type whose
    /// <c>gis_door</c> normalises to <c>elevator</c> also counts as an elevator door; on v1/v2 only the family name does.
    /// </summary>
    public static bool IsAcceptedOpening(
        FamilyInstance instance,
        IReadOnlyList<string>? additionalAcceptedFamilies,
        int? standardVersion)
    {
        if (instance == null)
        {
            return false;
        }

        return IsDoorOrWindow(instance) ||
               IsAcceptedElevatorDoorFamily(instance, standardVersion) ||
               IsExplicitlyAcceptedFamily(instance, additionalAcceptedFamilies);
    }

    public static bool IsPotentialOpening(FamilyInstance instance)
    {
        return IsPotentialOpening(instance, null);
    }

    public static bool IsPotentialOpening(FamilyInstance instance, int? standardVersion)
    {
        if (instance == null)
        {
            return false;
        }

        return IsDoorOrWindow(instance) || IsAcceptedElevatorDoorFamily(instance, standardVersion);
    }

    /// <summary>Pre-v3 elevator-door detection: the accepted elevator-door family name only.</summary>
    public static bool IsAcceptedElevatorDoorFamily(FamilyInstance instance)
    {
        return IsAcceptedElevatorDoorFamily(instance, null);
    }

    /// <summary>
    /// The accepted elevator-door family name on every standard version; on v3+ also a type <c>gis_door</c>
    /// (read by its frozen GUID) that normalises to <c>elevator</c>.
    /// </summary>
    public static bool IsAcceptedElevatorDoorFamily(FamilyInstance instance, int? standardVersion)
    {
        if (instance == null)
        {
            return false;
        }

        string familyName = GetFamilyName(instance);
        if (OpeningFamilyRules.IsAcceptedElevatorDoorFamilyName(familyName))
        {
            return true;
        }

        if (!OpeningFamilyRules.IsStandardV3OrNewer(standardVersion))
        {
            return false;
        }

        string? gisDoor = GisStandardStateReader.ReadSharedTypeParameterRaw(instance, GisSharedParameters.GisDoor);
        return OpeningFamilyRules.IsElevatorDoor(familyName, gisDoor, standardVersion);
    }

    public static string GetFamilyName(FamilyInstance instance)
    {
        if (instance == null)
        {
            return string.Empty;
        }

        return instance.Symbol?.FamilyName?.Trim() ??
               instance.Name?.Trim() ??
               string.Empty;
    }

    private static bool IsExplicitlyAcceptedFamily(
        FamilyInstance instance,
        IReadOnlyList<string>? additionalAcceptedFamilies)
    {
        if (additionalAcceptedFamilies == null || additionalAcceptedFamilies.Count == 0)
        {
            return false;
        }

        string familyName = GetFamilyName(instance);
        return additionalAcceptedFamilies.Any(candidate =>
            string.Equals(candidate?.Trim(), familyName, StringComparison.Ordinal));
    }

    private static bool IsDoorOrWindow(FamilyInstance instance)
    {
        Category? category = instance.Category;
        if (category == null)
        {
            return false;
        }

        BuiltInCategory categoryId = (BuiltInCategory)(int)category.Id.Value;
        return categoryId == BuiltInCategory.OST_Doors || categoryId == BuiltInCategory.OST_Windows;
    }
}
