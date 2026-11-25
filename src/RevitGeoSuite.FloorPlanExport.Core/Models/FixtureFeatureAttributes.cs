using System;
using System.Collections.Generic;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.Models;

/// <summary>
/// The attributes of a family unit that resolved to a fixture: the fixture layer's columns, plus
/// the source keys that are not columns but that the <c>gis_key</c> write-back, validation and
/// diagnostics read. <c>is_linked_source</c> must travel with <c>source_element_id</c>, or a linked
/// element's id would be written back to whichever host element shares it.
/// </summary>
public static class FixtureFeatureAttributes
{
    public const string DefaultType = "fixture";

    private static readonly string[] SourceKeys =
    {
        "source_element_id",
        "source_document_key",
        "source_document_name",
        "has_persisted_export_id",
        "is_linked_source",
        "source_link_instance_id",
        "source_link_instance_name",
    };

    /// <summary>
    /// Resolves the exported fixture <c>type</c>. On standard v3+ a valid <c>gis_fixture_type</c> (trimmed,
    /// case-insensitive, no control characters) wins; otherwise, and always on v1/v2 or unset models,
    /// <paramref name="fallbackType"/> (default <c>"fixture"</c>).
    /// </summary>
    public static string ResolveType(
        string? gisFixtureType,
        int? standardVersion,
        string fallbackType = DefaultType)
    {
        return TryResolveV3Type(gisFixtureType, standardVersion, out string fixtureType)
            ? fixtureType
            : fallbackType;
    }

    public static Dictionary<string, object?> FromUnit(IReadOnlyDictionary<string, object?> unit)
    {
        return FromUnit(unit, gisFixtureType: null, standardVersion: null);
    }

    /// <summary>
    /// Maps a fixture unit to the fixture layer. <c>type</c> is the unit's own category (the legacy value)
    /// unless the model is on standard v3+ and <paramref name="gisFixtureType"/> is a valid fixture type.
    /// </summary>
    public static Dictionary<string, object?> FromUnit(
        IReadOnlyDictionary<string, object?> unit,
        string? gisFixtureType,
        int? standardVersion)
    {
        if (unit is null)
        {
            throw new ArgumentNullException(nameof(unit));
        }

        object? fixtureType = TryResolveV3Type(gisFixtureType, standardVersion, out string resolvedType)
            ? resolvedType
            : Read(unit, "category");

        Dictionary<string, object?> attributes = new()
        {
            ["id"] = Read(unit, "id"),
            [RevitKey.AttributeName] = Read(unit, RevitKey.AttributeName),
            ["type"] = fixtureType,
            ["name"] = Read(unit, "name"),
            ["alt_name"] = Read(unit, "alt_name"),
            ["level_id"] = Read(unit, "level_id"),
            ["source"] = Read(unit, "source"),
            ["display_point"] = Read(unit, "display_point"),
        };

        foreach (string key in SourceKeys)
        {
            if (unit.TryGetValue(key, out object? value))
            {
                attributes[key] = value;
            }
        }

        return attributes;
    }

    private static bool TryResolveV3Type(string? gisFixtureType, int? standardVersion, out string fixtureType)
    {
        fixtureType = string.Empty;
        return OpeningFamilyRules.IsStandardV3OrNewer(standardVersion) &&
               GisStandardDomains.TryNormalizeFixtureType(gisFixtureType, out fixtureType);
    }

    private static object? Read(IReadOnlyDictionary<string, object?> attributes, string key)
    {
        return attributes.TryGetValue(key, out object? value) ? value : null;
    }
}
