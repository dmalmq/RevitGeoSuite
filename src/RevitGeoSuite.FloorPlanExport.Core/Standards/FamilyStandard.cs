using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>One gis_kind of <c>standards/families.json</c>: the families allowed for it and what their types carry.</summary>
public sealed class StandardFamilyKind
{
    public StandardFamilyKind(string kind, IEnumerable<string> families, IEnumerable<string> requiredTypeParameters)
    {
        Kind = kind;
        Families = families.ToList();
        RequiredTypeParameters = requiredTypeParameters.ToList();
    }

    /// <summary>escalator, moving_walkway, elevator or ticket_gate.</summary>
    public string Kind { get; }

    /// <summary>The family names allowed for this kind, exactly as Revit shows them.</summary>
    public IReadOnlyList<string> Families { get; }

    /// <summary>The type parameters every type of a listed family fills; gis_kind must equal <see cref="Kind"/>.</summary>
    public IReadOnlyList<string> RequiredTypeParameters { get; }
}

/// <summary>
/// The closed list of families per gis_kind, from <c>standards/families.json</c>. The audit checks
/// families against it, and the export maps a listed family to its kind's unit category when the
/// family's type has no gis_kind.
/// </summary>
public sealed class FamilyStandard
{
    public const string FileName = "families.json";

    private static readonly Lazy<FamilyStandard> EmbeddedStandard = new(() => Parse(GisStandardFiles.ReadFamilies()));

    private readonly Dictionary<string, StandardFamilyKind> _kindByFamily;

    public FamilyStandard(IEnumerable<StandardFamilyKind> kinds)
    {
        Kinds = kinds.ToList();
        _kindByFamily = new Dictionary<string, StandardFamilyKind>(StringComparer.Ordinal);
        foreach (StandardFamilyKind kind in Kinds)
        {
            if (!VerticalCirculation.TryMapGisKind(kind.Kind, out _))
            {
                throw new FormatException($"{FileName}: '{kind.Kind}' is not a gis_kind.");
            }

            foreach (string family in kind.Families)
            {
                string name = family.Trim();
                if (name.Length == 0 || _kindByFamily.ContainsKey(name))
                {
                    throw new FormatException($"{FileName}: family '{family}' is empty or listed twice.");
                }

                _kindByFamily[name] = kind;
            }
        }
    }

    public IReadOnlyList<StandardFamilyKind> Kinds { get; }

    /// <summary>The list this build ships.</summary>
    public static FamilyStandard Embedded => EmbeddedStandard.Value;

    public static FamilyStandard Parse(string json)
    {
        JObject root = JObject.Parse(json);
        return new FamilyStandard(((JArray?)root["kinds"] ?? new JArray()).Select(token => new StandardFamilyKind(
            (string)token["kind"]!,
            ((JArray?)token["families"] ?? new JArray()).Select(name => (string)name!),
            ((JArray?)token["required_type_parameters"] ?? new JArray()).Select(name => (string)name!))));
    }

    /// <summary>The kind whose list holds this family name, or null for a family not on the list.</summary>
    public StandardFamilyKind? KindOf(string? familyName) =>
        _kindByFamily.TryGetValue((familyName ?? string.Empty).Trim(), out StandardFamilyKind? kind) ? kind : null;

    /// <summary>The kind entry for a gis_kind value, or null.</summary>
    public StandardFamilyKind? Kind(string? gisKind)
    {
        if (!VerticalCirculation.TryMapGisKind(gisKind, out string category))
        {
            return null;
        }

        return Kinds.FirstOrDefault(kind => VerticalCirculation.TryMapGisKind(kind.Kind, out string listed) && listed == category);
    }

    /// <summary>
    /// The export's family catalog entries: each listed family with the unit category its kind
    /// maps to, as gis_kind would give it.
    /// </summary>
    public IReadOnlyDictionary<string, ZoneInfo> ZoneLookup()
    {
        Dictionary<string, ZoneInfo> lookup = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, StandardFamilyKind> entry in _kindByFamily)
        {
            if (VerticalCirculation.TryMapGisKind(entry.Value.Kind, out string category) &&
                ImdfUnitCategoryCatalog.TryGetInfo(category, out ZoneInfo info))
            {
                lookup[entry.Key] = info;
            }
        }

        return lookup;
    }
}
