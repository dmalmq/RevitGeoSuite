using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace RevitGeoSuite.FloorPlanExport.Core.UnitFloors;

/// <summary>
/// <c>&lt;gpkg&gt;.unit-floors.json</c>: which Revit floor was made from which GIS row. It is the
/// starting link between the GDB and Revit: Kanizu reads it to write each floor's Revit key into the
/// GDB, after which Revit is the source of those units. RevitGeoSuite is its only writer.
/// </summary>
public sealed class UnitFloorLinkFile
{
    public const string Format = "revitgeosuite.unit-floors";
    public const int CurrentVersion = 1;
    public const string Suffix = ".unit-floors.json";

    [JsonProperty("format")]
    public string FileFormat { get; set; } = Format;

    [JsonProperty("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonProperty("createdUtc")]
    public string CreatedUtc { get; set; } = string.Empty;

    [JsonProperty("source")]
    public UnitFloorLinkSource Source { get; set; } = new();

    [JsonProperty("model")]
    public UnitFloorLinkModel Model { get; set; } = new();

    [JsonProperty("level")]
    public UnitFloorLinkLevel Level { get; set; } = new();

    [JsonProperty("floors")]
    public List<UnitFloorLinkFloor> Floors { get; set; } = new();

    [JsonProperty("skipped")]
    public List<UnitFloorLinkSkipped> Skipped { get; set; } = new();

    [JsonProperty("replacedFloors")]
    public List<UnitFloorLinkReplaced> ReplacedFloors { get; set; } = new();

    /// <summary>The link file path for a GeoPackage and table: beside the file, named after both.</summary>
    public static string PathFor(string geoPackagePath, string tableName, string levelName)
    {
        string folder = System.IO.Path.GetDirectoryName(geoPackagePath) ?? string.Empty;
        string stem = System.IO.Path.GetFileNameWithoutExtension(geoPackagePath);
        return System.IO.Path.Combine(folder, SafeName(stem + "." + tableName + "." + levelName) + Suffix);
    }

    public static UnitFloorLinkFile Load(string path)
    {
        UnitFloorLinkFile? file = JsonConvert.DeserializeObject<UnitFloorLinkFile>(File.ReadAllText(path, Encoding.UTF8));
        if (file is null || !string.Equals(file.FileFormat, Format, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{path} is not a unit floors link file.");
        }

        if (file.Version > CurrentVersion)
        {
            throw new InvalidDataException($"{path} has version {file.Version}; this build reads up to {CurrentVersion}.");
        }

        return file;
    }

    /// <summary>Writes the file through a temporary file and a rename, so a reader never sees half of it.</summary>
    public void Save(string path)
    {
        string json = JsonConvert.SerializeObject(this, Formatting.Indented);
        string temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        if (File.Exists(path))
        {
            File.Replace(temp, path, null);
        }
        else
        {
            File.Move(temp, path);
        }
    }

    private static string SafeName(string name)
    {
        StringBuilder builder = new(name.Length);
        foreach (char c in name)
        {
            builder.Append(Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        }

        return builder.ToString();
    }
}

public sealed class UnitFloorLinkSource
{
    [JsonProperty("path")]
    public string Path { get; set; } = string.Empty;

    [JsonProperty("table")]
    public string Table { get; set; } = string.Empty;

    /// <summary>The column the keys come from, or null for the table's fid.</summary>
    [JsonProperty("keyColumn")]
    public string? KeyColumn { get; set; }

    [JsonProperty("filterColumn")]
    public string? FilterColumn { get; set; }

    [JsonProperty("filterValue")]
    public string? FilterValue { get; set; }

    [JsonProperty("epsg")]
    public int? Epsg { get; set; }

    [JsonProperty("projectEpsg")]
    public int ProjectEpsg { get; set; }
}

public sealed class UnitFloorLinkModel
{
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("path")]
    public string Path { get; set; } = string.Empty;
}

public sealed class UnitFloorLinkLevel
{
    [JsonProperty("uniqueId")]
    public string UniqueId { get; set; } = string.Empty;

    [JsonProperty("elementId")]
    public long ElementId { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class UnitFloorLinkFloor
{
    [JsonProperty("key")]
    public string Key { get; set; } = string.Empty;

    [JsonProperty("floorUniqueId")]
    public string FloorUniqueId { get; set; } = string.Empty;

    [JsonProperty("floorElementId")]
    public long FloorElementId { get; set; }

    [JsonProperty("typeName")]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>The floor whose type it took, or null when it took the fallback type.</summary>
    [JsonProperty("hostUniqueId")]
    public string? HostUniqueId { get; set; }

    [JsonProperty("areaSquareMetres")]
    public double AreaSquareMetres { get; set; }
}

public sealed class UnitFloorLinkSkipped
{
    [JsonProperty("key")]
    public string Key { get; set; } = string.Empty;

    [JsonProperty("reason")]
    public string Reason { get; set; } = string.Empty;
}

public sealed class UnitFloorLinkReplaced
{
    [JsonProperty("uniqueId")]
    public string UniqueId { get; set; } = string.Empty;

    [JsonProperty("elementId")]
    public long ElementId { get; set; }

    [JsonProperty("typeName")]
    public string TypeName { get; set; } = string.Empty;

    [JsonProperty("coveredShare")]
    public double CoveredShare { get; set; }
}
