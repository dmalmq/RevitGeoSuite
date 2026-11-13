using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>
/// The Kanizu GIS standard as the model tools (Set up model, Audit, Fix floor types, Parameters,
/// Review GDB edits) read it: the version, the shared parameters, the theme materials, the
/// standard floor types and the unit categories the export accepts. Everything comes from the
/// files embedded in this assembly, so the tools and the export can never disagree.
/// </summary>
public sealed class GisToolStandard
{
    private readonly HashSet<string> _knownCategories;

    public GisToolStandard(
        int version,
        IReadOnlyList<GisParameterDefinition> parameters,
        IReadOnlyList<StandardMaterial> materials,
        IReadOnlyList<StandardFloorType> floorTypes,
        IEnumerable<string> knownCategories,
        string outlineHex,
        IReadOnlyList<FormerFloorTypeName>? formerNames = null)
    {
        FormerNames = formerNames ?? Array.Empty<FormerFloorTypeName>();
        Version = version;
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Materials = materials ?? throw new ArgumentNullException(nameof(materials));
        FloorTypes = floorTypes ?? throw new ArgumentNullException(nameof(floorTypes));
        OutlineHex = outlineHex ?? string.Empty;
        _knownCategories = new HashSet<string>(
            (knownCategories ?? throw new ArgumentNullException(nameof(knownCategories))).Select(category => category.Trim().ToLowerInvariant()),
            StringComparer.Ordinal);
    }

    public int Version { get; }

    public IReadOnlyList<GisParameterDefinition> Parameters { get; }

    public IReadOnlyList<StandardMaterial> Materials { get; }

    public IReadOnlyList<StandardFloorType> FloorTypes { get; }

    public string OutlineHex { get; }

    public IReadOnlyList<FormerFloorTypeName> FormerNames { get; }

    /// <summary>The standard this build ships.</summary>
    public static GisToolStandard LoadEmbedded()
    {
        FloorTypeStandard floorTypes = FloorTypeStandard.LoadEmbedded();
        return new GisToolStandard(
            floorTypes.StandardVersion,
            GisSharedParameters.All,
            floorTypes.Materials,
            floorTypes.FloorTypes,
            FloorTypeStandard.KnownCategories,
            floorTypes.OutlineHex,
            floorTypes.FormerNames);
    }

    public GisParameterDefinition? Parameter(string name) =>
        Parameters.FirstOrDefault(parameter => parameter.Name == name);

    public StandardMaterial? Material(string name) =>
        Materials.FirstOrDefault(material => material.Name == name);

    public StandardFloorType? FloorType(string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        return FloorTypes.FirstOrDefault(floorType => floorType.Name == trimmed);
    }

    /// <summary>
    /// The standard names a floor type called <paramref name="name"/> is an old name for, or none
    /// when it is not an old name. A name is old when its zone equals a former name's zone, ignoring
    /// case, and it is not itself a standard name.
    /// </summary>
    public IReadOnlyList<string> FormerTargets(string? name)
    {
        if (FloorType(name) != null)
        {
            return Array.Empty<string>();
        }

        string zone = FloorTypeMatcher.ZoneName(name);
        FormerFloorTypeName? former = FormerNames.FirstOrDefault(entry =>
            string.Equals(FloorTypeMatcher.ZoneName(entry.Name), zone, StringComparison.OrdinalIgnoreCase));
        return former?.Now ?? Array.Empty<string>();
    }

    /// <summary>True for a unit category the export accepts, ignoring case and surrounding spaces.</summary>
    public bool IsKnownCategory(string? value) => _knownCategories.Contains(Normalize(value).ToLowerInvariant());

    /// <summary>The value trimmed, with null read as empty.</summary>
    public static string Normalize(string? value) => value == null ? string.Empty : value.Trim();

    /// <summary>A whole number written as text, as the export reads it, or null.</summary>
    public static int? ParseWholeNumber(string? text)
    {
        string value = Normalize(text);
        return value.Length > 0 && value.Skip(value[0] is '+' or '-' ? 1 : 0).All(character => character is >= '0' and <= '9') &&
               int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number)
            ? number
            : null;
    }

    /// <summary>The red, green and blue of a <c>#RRGGBB</c> colour.</summary>
    public static (byte Red, byte Green, byte Blue) HexToRgb(string hex)
    {
        string value = (hex ?? throw new ArgumentNullException(nameof(hex))).TrimStart('#');
        if (value.Length != 6)
        {
            throw new FormatException($"'{hex}' is not a #RRGGBB colour.");
        }

        return (
            byte.Parse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public static string RgbToHex(byte red, byte green, byte blue) =>
        string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", red, green, blue);
}
