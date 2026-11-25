using System;
using System.Collections.Generic;
using System.Globalization;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.Models;

public static class OpeningFamilyRules
{
    public const int V3StandardVersion = 3;
    public const string DefaultOpeningCategory = "pedestrian";
    public const string EmergencyExitOpeningCategory = "emergencyexit";
    public const string ElevatorDoorType = "elevator";
    public const string EmergencyAccessControl = "emergency";

    private static readonly HashSet<string> AcceptedElevatorDoorFamilyNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "\u0045\u0056\u6249",
        };

    public static bool IsStandardV3OrNewer(int? standardVersion)
    {
        return standardVersion.HasValue && standardVersion.Value >= V3StandardVersion;
    }

    /// <summary>
    /// Parses <c>gis_standard_version</c> exactly as <see cref="GisStandardCheck"/> does (trimmed, invariant
    /// integer), so the extractors gate on the same version the standard check reports. Null when unreadable.
    /// </summary>
    public static int? ParseStandardVersion(string? standardVersionText)
    {
        string trimmed = (standardVersionText ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;
    }

    public static bool IsAcceptedElevatorDoorFamilyName(string? familyName)
    {
        string normalized = (familyName ?? string.Empty).Trim();
        return normalized.Length > 0 && AcceptedElevatorDoorFamilyNames.Contains(normalized);
    }

    /// <summary>
    /// Recognizes elevator doors by the legacy <c>EV扉</c> family name on all standard versions,
    /// and additionally by <c>gis_door = "elevator"</c> on standard v3+ models.
    /// </summary>
    public static bool IsElevatorDoor(string? familyName, string? gisDoor, int? standardVersion)
    {
        if (IsAcceptedElevatorDoorFamilyName(familyName))
        {
            return true;
        }

        if (!IsStandardV3OrNewer(standardVersion))
        {
            return false;
        }

        return GisStandardDomains.TryNormalizeDoorType(gisDoor, out string normalizedDoor) &&
               string.Equals(normalizedDoor, ElevatorDoorType, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the exported opening <c>category</c>.
    /// On standard v3+: <c>gis_opening_category</c> (valid closed domain) &gt;
    /// <c>gis_access_con == "emergency"</c> -&gt; <c>"emergencyexit"</c> &gt; legacy family-name heuristic.
    /// On v1/v2 or uninitialized models: preserves exact legacy family-name heuristic.
    /// </summary>
    public static string ResolveOpeningCategory(
        string? familyName,
        string? gisOpeningCategory,
        string? gisAccessCon,
        int? standardVersion)
    {
        if (IsStandardV3OrNewer(standardVersion))
        {
            if (GisStandardDomains.TryNormalizeOpeningCategory(gisOpeningCategory, out string normalizedCategory))
            {
                return normalizedCategory;
            }

            if (GisStandardDomains.TryNormalizeAccessControl(gisAccessCon, out string normalizedAccessCon) &&
                string.Equals(normalizedAccessCon, EmergencyAccessControl, StringComparison.Ordinal))
            {
                return EmergencyExitOpeningCategory;
            }
        }

        return ResolveLegacyOpeningCategory(familyName);
    }

    private static string ResolveLegacyOpeningCategory(string? familyName)
    {
        return (familyName ?? string.Empty).IndexOf("emergency", StringComparison.OrdinalIgnoreCase) >= 0
            ? EmergencyExitOpeningCategory
            : DefaultOpeningCategory;
    }
}
