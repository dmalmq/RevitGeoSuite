using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>How a shared parameter is bound to its categories.</summary>
public enum GisParameterBinding
{
    Instance,
    Type,
}

/// <summary>The value type of a shared parameter, as the shared parameter file names it.</summary>
public enum GisParameterDataType
{
    Text,
    Integer,
}

/// <summary>One shared parameter of the Kanizu GIS standard.</summary>
public sealed class GisParameterDefinition
{
    public GisParameterDefinition(
        string name,
        Guid guid,
        GisParameterDataType dataType,
        GisParameterBinding binding,
        IReadOnlyList<string> categories,
        string description,
        bool userModifiable = true)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Guid = guid;
        DataType = dataType;
        Binding = binding;
        Categories = categories ?? throw new ArgumentNullException(nameof(categories));
        Description = description ?? string.Empty;
        UserModifiable = userModifiable;
    }

    public string Name { get; }

    /// <summary>The fixed GUID. Every PC binds the same definition, so models agree with each other.</summary>
    public Guid Guid { get; }

    public GisParameterDataType DataType { get; }

    public GisParameterBinding Binding { get; }

    /// <summary>Revit <c>BuiltInCategory</c> names, such as <c>OST_Floors</c>.</summary>
    public IReadOnlyList<string> Categories { get; }

    public string Description { get; }

    public bool UserModifiable { get; }
}

/// <summary>
/// The shared parameters of the Kanizu GIS standard, with fixed GUIDs. The shipped file
/// <c>standards/KanizuGIS.SharedParameters.txt</c> is generated from this list, and a test pins the two
/// together. The six names RevitGeoSuite has always used stay as they are; new ones start with <c>gis_</c>.
/// </summary>
public static class GisSharedParameters
{
    public const string GroupName = "RevitGeoSuite FloorPlanExport";

    public const string FileName = "KanizuGIS.SharedParameters.txt";

    /// <summary>The bindings of <see cref="All"/>, for tools that bind them outside RevitGeoSuite (pyRevit).</summary>
    public const string BindingsFileName = "shared-parameter-bindings.json";

    public const string Id = "id";
    public const string LevelId = "level_id";
    public const string Name = "name";
    public const string ShortName = "short_name";
    public const string Category = "category";
    public const string Ordinal = "ordinal";
    public const string GisCategory = "gis_category";
    public const string GisRestriction = "gis_restriction";
    public const string GisKind = "gis_kind";
    public const string GisDirection = "gis_direction";
    public const string GisLevelsServed = "gis_levels_served";
    public const string GisDoor = "gis_door";
    public const string GisAccessCon = "gis_access_con";
    public const string GisKey = "gis_key";
    public const string GisStandardVersion = "gis_standard_version";

    private const string Floors = "OST_Floors";
    private const string Rooms = "OST_Rooms";
    private const string Stairs = "OST_Stairs";
    private const string GenericModel = "OST_GenericModel";
    private const string SpecialtyEquipment = "OST_SpecialityEquipment";
    private const string Levels = "OST_Levels";
    private const string Doors = "OST_Doors";
    private const string MechanicalEquipment = "OST_MechanicalEquipment";

    /// <summary>The categories that make unit features: floors, rooms, stairs and the escalator and fixture families.</summary>
    private static readonly string[] UnitCategories = { Floors, Rooms, Stairs, GenericModel, SpecialtyEquipment };

    private static readonly string[] FamilyCategories = { GenericModel, SpecialtyEquipment };

    /// <summary>Every category the export makes units of, columns included, so each can hold its gis_key.</summary>
    private static readonly string[] KeyedCategories =
        { Floors, Rooms, Stairs, GenericModel, SpecialtyEquipment, "OST_Columns", "OST_StructuralColumns" };

    public static IReadOnlyList<GisParameterDefinition> All { get; } = new[]
    {
        new GisParameterDefinition(
            Id,
            new Guid("64e7d743-c3ae-4831-a208-33c2762ac50a"),
            GisParameterDataType.Text,
            GisParameterBinding.Instance,
            new[] { Floors, Rooms, Stairs, GenericModel, SpecialtyEquipment, Doors, "OST_Windows", "OST_Columns", "OST_StructuralColumns" },
            "Stable export id of the element."),
        new GisParameterDefinition(
            LevelId,
            new Guid("84f1637a-1421-4c6c-9670-c000206ffea0"),
            GisParameterDataType.Text,
            GisParameterBinding.Instance,
            new[] { Levels, Floors, Rooms },
            "Stable export id of the level."),
        new GisParameterDefinition(
            Name,
            new Guid("47b8db20-e756-4280-a842-f570dc847db5"),
            GisParameterDataType.Text,
            GisParameterBinding.Instance,
            new[] { Floors, Rooms, Stairs, GenericModel, SpecialtyEquipment, Doors },
            "Name of the unit or door in the GIS."),
        new GisParameterDefinition(
            ShortName,
            new Guid("03f1849b-ac06-42c8-aff7-51a973f0a730"),
            GisParameterDataType.Text,
            GisParameterBinding.Instance,
            new[] { Floors, Rooms, Stairs, GenericModel, SpecialtyEquipment, Levels },
            "Short name of the unit or level in the GIS."),
        new GisParameterDefinition(
            Category,
            new Guid("7fb0aa6b-c570-437d-a86a-f25f02537165"),
            GisParameterDataType.Text,
            GisParameterBinding.Instance,
            UnitCategories,
            "Unit category for this element only. Overrides gis_category of the type."),
        new GisParameterDefinition(
            Ordinal,
            new Guid("6724ae86-07fe-44ba-b29b-1e9298bc3c8a"),
            GisParameterDataType.Integer,
            GisParameterBinding.Instance,
            new[] { Levels, Floors, Rooms },
            "Floor number of the level in the GIS."),
        new GisParameterDefinition(
            GisCategory,
            new Guid("d4ed1842-369c-4064-97fd-93fab33f0a87"),
            GisParameterDataType.Text,
            GisParameterBinding.Type,
            new[] { Floors, GenericModel, SpecialtyEquipment },
            "Unit category of every element of this type."),
        new GisParameterDefinition(
            GisRestriction,
            new Guid("6df45ec8-c9ee-4b56-a038-c8c125b3ae1e"),
            GisParameterDataType.Text,
            GisParameterBinding.Type,
            new[] { Floors, GenericModel, SpecialtyEquipment },
            "Restriction of every element of this type, such as rachi_nai. Empty means by location."),
        new GisParameterDefinition(
            GisKind,
            new Guid("ff63e172-c407-4265-b075-4e1d5e9822b8"),
            GisParameterDataType.Text,
            GisParameterBinding.Type,
            FamilyCategories,
            "What a family is in the GIS: escalator, moving_walkway, elevator or ticket_gate."),
        new GisParameterDefinition(
            GisDirection,
            new Guid("343f3126-afff-422c-99c1-0d1d4f1b1c95"),
            GisParameterDataType.Text,
            GisParameterBinding.Instance,
            new[] { Stairs, GenericModel, SpecialtyEquipment },
            "Travel direction of an escalator or moving walkway: up, down or both."),
        new GisParameterDefinition(
            GisLevelsServed,
            new Guid("26b16279-ebcb-4baa-9a7e-b07addf4e5b4"),
            GisParameterDataType.Text,
            GisParameterBinding.Type,
            new[] { GenericModel, SpecialtyEquipment, MechanicalEquipment },
            "Levels an elevator of this type stops at, by level name or short_name, separated by commas. Empty means the levels of the elevator objects in the same plan spot."),
        new GisParameterDefinition(
            GisDoor,
            new Guid("e413bbb1-dab0-4acd-9b16-eeac608dca9b"),
            GisParameterDataType.Text,
            GisParameterBinding.Type,
            new[] { Doors },
            "Door type of every door of this type, exported to the opening's door field."),
        new GisParameterDefinition(
            GisAccessCon,
            new Guid("0156f4b3-7806-4033-baa0-09838c299ddb"),
            GisParameterDataType.Text,
            GisParameterBinding.Type,
            new[] { Doors },
            "Access control of every door of this type, exported to the opening's access_con field."),
        new GisParameterDefinition(
            GisKey,
            new Guid("0816cab3-3717-4632-854f-c6c7c11e0881"),
            GisParameterDataType.Text,
            GisParameterBinding.Instance,
            KeyedCategories,
            "revit_key of the first feature (part 0) this element made in the last export. Written by the export.",
            userModifiable: false),
        new GisParameterDefinition(
            GisStandardVersion,
            new Guid("a5edd0e8-7877-4498-91c6-29691a6a01f5"),
            GisParameterDataType.Text,
            GisParameterBinding.Instance,
            new[] { "OST_ProjectInformation" },
            "Version of the Kanizu GIS standard the model was set up with.",
            userModifiable: false),
    };

    public static bool TryGet(string name, out GisParameterDefinition definition)
    {
        definition = All.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal))!;
        return definition != null;
    }

    /// <summary>
    /// The shared parameter file in the format Revit 2024 and later reads. Plain ASCII and LF line
    /// endings, so the file reads the same whatever encoding a tool assumes.
    /// </summary>
    public static string ToSharedParameterFileText() => ToSharedParameterFileText(All);

    /// <summary>The same file for any list of definitions, such as the standard's plus a field list's.</summary>
    public static string ToSharedParameterFileText(IEnumerable<GisParameterDefinition> definitions)
    {
        StringBuilder builder = new();
        builder.Append("# This is a Revit shared parameter file.\n");
        builder.Append("# Do not edit manually. Generated from GisSharedParameters.cs; see standards/README.md.\n");
        builder.Append("*META\tVERSION\tMINVERSION\n");
        builder.Append("META\t2\t1\n");
        builder.Append("*GROUP\tID\tNAME\n");
        builder.Append("GROUP\t1\t").Append(GroupName).Append('\n');
        builder.Append("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOEXISTENT\n");
        foreach (GisParameterDefinition definition in definitions)
        {
            builder
                .Append("PARAM\t")
                .Append(definition.Guid.ToString("D"))
                .Append('\t').Append(definition.Name)
                .Append('\t').Append(definition.DataType == GisParameterDataType.Integer ? "INTEGER" : "TEXT")
                .Append("\t\t1\t1\t")
                .Append(definition.Description)
                .Append('\t').Append(definition.UserModifiable ? '1' : '0')
                .Append("\t0\n");
        }

        return builder.ToString();
    }

    /// <summary>The text of <c>standards/shared-parameter-bindings.json</c>: UTF-8, LF line endings.</summary>
    public static string ToBindingsJson()
    {
        JObject root = new()
        {
            ["note"] = "Generated from GisSharedParameters.cs. Do not edit by hand; see standards/README.md.",
            ["group"] = GroupName,
            ["parameters"] = new JArray(All.Select(definition => new JObject
            {
                ["name"] = definition.Name,
                ["guid"] = definition.Guid.ToString("D"),
                ["data_type"] = definition.DataType == GisParameterDataType.Integer ? "integer" : "text",
                ["binding"] = definition.Binding == GisParameterBinding.Type ? "type" : "instance",
                ["categories"] = new JArray(definition.Categories),
                ["user_modifiable"] = definition.UserModifiable,
            })),
        };

        return root.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";
    }
}
