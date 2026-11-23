using System;
using System.Collections.Generic;

namespace RevitGeoSuite.FloorPlanExport.Core.Models;

/// <summary>
/// The attributes of a family unit that resolved to a fixture: the fixture layer's columns, plus
/// the source keys that are not columns but that the <c>gis_key</c> write-back, validation and
/// diagnostics read. <c>is_linked_source</c> must travel with <c>source_element_id</c>, or a linked
/// element's id would be written back to whichever host element shares it.
/// </summary>
public static class FixtureFeatureAttributes
{
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

    public static Dictionary<string, object?> FromUnit(IReadOnlyDictionary<string, object?> unit)
    {
        if (unit is null)
        {
            throw new ArgumentNullException(nameof(unit));
        }

        Dictionary<string, object?> attributes = new()
        {
            ["id"] = Read(unit, "id"),
            [RevitKey.AttributeName] = Read(unit, RevitKey.AttributeName),
            ["type"] = Read(unit, "category"),
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

    private static object? Read(IReadOnlyDictionary<string, object?> attributes, string key)
    {
        return attributes.TryGetValue(key, out object? value) ? value : null;
    }
}
