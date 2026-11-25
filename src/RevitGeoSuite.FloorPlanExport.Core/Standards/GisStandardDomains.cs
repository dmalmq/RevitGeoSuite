using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>
/// Closed pick-list domains for Kanizu GIS Standard v3 opening, fixture, and model-role parameters.
/// Kept in exact sync with <c>standards/opening-types.json</c> and <c>standards/fields.json</c>
/// by <c>GisStandardTests</c> and <c>GisStandardDomainsTests</c>.
/// </summary>
public static class GisStandardDomains
{
    public const string OpeningTypesFileName = "opening-types.json";

    public static IReadOnlyList<string> DoorTypes { get; } = Array.AsReadOnly(new[]
    {
        "swinging",
        "sliding",
        "automatic",
        "elevator",
        "shutter",
        "revolving",
        "open",
    });

    public static IReadOnlyList<string> AccessControls { get; } = Array.AsReadOnly(new[]
    {
        "none",
        "employeesonly",
        "emergency",
        "scheduled",
        "badgereader",
        "restricted",
    });

    public static IReadOnlyList<string> OpeningCategories { get; } = Array.AsReadOnly(new[]
    {
        "pedestrian",
        "emergencyexit",
        "service",
        "automobile",
        "bicycle",
        "pedestrian.principal",
        "pedestrian.transit",
    });

    public static IReadOnlyList<string> FixtureTypes { get; } = Array.AsReadOnly(new[]
    {
        "turnstile",
        "wall",
        "kiosk",
        "equipment",
        "furniture",
        "obstruction",
    });

    public static IReadOnlyList<string> ModelRoles { get; } = Array.AsReadOnly(new[]
    {
        "authoring",
        "showcase",
    });

    private static readonly Dictionary<string, string> DoorTypeLookup = BuildLookup(DoorTypes);
    private static readonly Dictionary<string, string> AccessControlLookup = BuildLookup(AccessControls);
    private static readonly Dictionary<string, string> OpeningCategoryLookup = BuildLookup(OpeningCategories);
    private static readonly Dictionary<string, string> FixtureTypeLookup = BuildLookup(FixtureTypes);
    private static readonly Dictionary<string, string> ModelRoleLookup = BuildLookup(ModelRoles);

    private static readonly Dictionary<string, IReadOnlyList<string>> DomainsByParameter =
        new(StringComparer.Ordinal)
        {
            [GisSharedParameters.GisDoor] = DoorTypes,
            [GisSharedParameters.GisAccessCon] = AccessControls,
            [GisSharedParameters.GisOpeningCategory] = OpeningCategories,
            [GisSharedParameters.GisFixtureType] = FixtureTypes,
            [GisSharedParameters.GisModelRole] = ModelRoles,
        };

    private static readonly Dictionary<string, Dictionary<string, string>> LookupsByParameter =
        new(StringComparer.Ordinal)
        {
            [GisSharedParameters.GisDoor] = DoorTypeLookup,
            [GisSharedParameters.GisAccessCon] = AccessControlLookup,
            [GisSharedParameters.GisOpeningCategory] = OpeningCategoryLookup,
            [GisSharedParameters.GisFixtureType] = FixtureTypeLookup,
            [GisSharedParameters.GisModelRole] = ModelRoleLookup,
        };

    /// <summary>True when <paramref name="value"/> contains a control character or Unicode line/paragraph separator.</summary>
    public static bool ContainsDisallowedCharacters(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (char character in value!)
        {
            if (char.IsControl(character) || character == '\u2028' || character == '\u2029')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="value"/> is non-blank or carries control/line-break characters that must not be silently ignored.
    /// </summary>
    public static bool HasValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) || ContainsDisallowedCharacters(value);

    public static bool TryNormalizeDoorType(string? value, out string canonical) =>
        TryNormalize(value, DoorTypeLookup, out canonical);

    public static bool TryNormalizeAccessControl(string? value, out string canonical) =>
        TryNormalize(value, AccessControlLookup, out canonical);

    public static bool TryNormalizeOpeningCategory(string? value, out string canonical) =>
        TryNormalize(value, OpeningCategoryLookup, out canonical);

    public static bool TryNormalizeFixtureType(string? value, out string canonical) =>
        TryNormalize(value, FixtureTypeLookup, out canonical);

    public static bool TryNormalizeModelRole(string? value, out string canonical) =>
        TryNormalize(value, ModelRoleLookup, out canonical);

    public static bool IsValidDoorType(string? value) => TryNormalizeDoorType(value, out _);

    public static bool IsValidAccessControl(string? value) => TryNormalizeAccessControl(value, out _);

    public static bool IsValidOpeningCategory(string? value) => TryNormalizeOpeningCategory(value, out _);

    public static bool IsValidFixtureType(string? value) => TryNormalizeFixtureType(value, out _);

    public static bool IsValidModelRole(string? value) => TryNormalizeModelRole(value, out _);

    public static bool TryGetDomain(string parameterName, out IReadOnlyList<string> values)
    {
        if (parameterName != null && DomainsByParameter.TryGetValue(parameterName, out IReadOnlyList<string>? domain))
        {
            values = domain;
            return true;
        }

        values = Array.Empty<string>();
        return false;
    }

    public static bool IsValidValue(string parameterName, string? value)
    {
        if (parameterName == null || !LookupsByParameter.TryGetValue(parameterName, out Dictionary<string, string>? lookup))
        {
            return false;
        }

        return TryNormalize(value, lookup, out _);
    }

    /// <summary>
    /// Validates an optional domain parameter value. Empty/space-only values return null; any non-empty
    /// or control-character value outside the closed domain returns an error message.
    /// </summary>
    public static string? ValidateOptionalValue(string parameterName, string? value)
    {
        if (!HasValue(value))
        {
            return null;
        }

        if (!TryGetDomain(parameterName, out IReadOnlyList<string> allowed))
        {
            throw new ArgumentException($"Unknown domain parameter '{parameterName}'.", nameof(parameterName));
        }

        if (IsValidValue(parameterName, value))
        {
            return null;
        }

        string display = SanitizeForDisplay(value!);
        return $"Invalid {parameterName} '{display}'. Allowed values: {string.Join(", ", allowed)}.";
    }

    /// <summary>The text of <c>standards/opening-types.json</c>: UTF-8, LF line endings, two-space indent.</summary>
    public static string ToOpeningTypesJson()
    {
        JObject root = new()
        {
            ["standard_version"] = GisStandard.Version,
            ["note"] = "Generated from GisStandardDomains.cs. Do not edit by hand; see standards/README.md.",
            [GisSharedParameters.GisDoor] = new JArray(DoorTypes),
            [GisSharedParameters.GisAccessCon] = new JArray(AccessControls),
            [GisSharedParameters.GisOpeningCategory] = new JArray(OpeningCategories),
            [GisSharedParameters.GisFixtureType] = new JArray(FixtureTypes),
            [GisSharedParameters.GisModelRole] = new JArray(ModelRoles),
        };

        return root.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";
    }

    /// <summary>Parses <c>standards/opening-types.json</c> into parameter-keyed closed domain lists.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseOpeningTypesJson(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        JObject root = JObject.Parse(json);
        Dictionary<string, IReadOnlyList<string>> parsed = new(StringComparer.Ordinal);
        foreach (string key in DomainsByParameter.Keys)
        {
            if (root[key] is not JArray array)
            {
                throw new InvalidDataException($"{OpeningTypesFileName} is missing domain array '{key}'.");
            }

            List<string> values = array
                .Select(token => token.Type == JTokenType.String
                    ? (string)token!
                    : throw new InvalidDataException($"{OpeningTypesFileName}: '{key}' must contain only strings."))
                .ToList();
            parsed[key] = Array.AsReadOnly(values.ToArray());
        }

        return parsed;
    }

    private static bool TryNormalize(string? value, Dictionary<string, string> lookup, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || ContainsDisallowedCharacters(value))
        {
            return false;
        }

        string trimmed = value!.Trim();
        if (lookup.TryGetValue(trimmed, out string? matched) && matched != null)
        {
            canonical = matched;
            return true;
        }

        return false;
    }

    private static Dictionary<string, string> BuildLookup(IReadOnlyList<string> values)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (string value in values)
        {
            map[value] = value;
        }

        return map;
    }

    private static string SanitizeForDisplay(string value) =>
        value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
}
