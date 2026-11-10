using System;

namespace RevitGeoSuite.SharedUI.Web.Contracts;

/// <summary>Payload of the <c>unitFloors.inspect</c> request: the GeoPackage to read.</summary>
[TsExport]
public sealed class UnitFloorsInspectRequest
{
    public string Path { get; set; } = string.Empty;
}

/// <summary>The feature tables of a GeoPackage and the model's levels and floor types.</summary>
[TsExport]
public sealed class UnitFloorsInspectResponse
{
    public UnitFloorsTable[] Tables { get; set; } = Array.Empty<UnitFloorsTable>();

    public GisLevelOption[] Levels { get; set; } = Array.Empty<GisLevelOption>();

    public long? DefaultLevelId { get; set; }

    public UnitFloorsFloorType[] FloorTypes { get; set; } = Array.Empty<UnitFloorsFloorType>();
}

/// <summary>One feature table of the GeoPackage.</summary>
[TsExport]
public sealed class UnitFloorsTable
{
    public string Name { get; set; } = string.Empty;

    public string[] Columns { get; set; } = Array.Empty<string>();

    public int FeatureCount { get; set; }

    public int? Epsg { get; set; }
}

/// <summary>A floor type the user can give units that have no floor under them.</summary>
[TsExport]
public sealed class UnitFloorsFloorType
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>Payload of <c>unitFloors.preview</c> and <c>unitFloors.create</c>.</summary>
[TsExport]
public sealed class UnitFloorsRequest
{
    public string Path { get; set; } = string.Empty;

    public string Table { get; set; } = string.Empty;

    /// <summary>The column whose value keys each row; empty for the table's fid.</summary>
    public string KeyColumn { get; set; } = string.Empty;

    /// <summary>Only rows whose <see cref="FilterColumn"/> equals <see cref="FilterValue"/>; empty for every row.</summary>
    public string FilterColumn { get; set; } = string.Empty;

    public string FilterValue { get; set; } = string.Empty;

    public long LevelId { get; set; }

    /// <summary>The type for units with no floor under them; null leaves those units out.</summary>
    public long? FallbackTypeId { get; set; }

    /// <summary>Create only: delete the existing floors the new ones cover.</summary>
    public bool DeleteReplaced { get; set; }
}

/// <summary>What <c>unitFloors.preview</c> plans, or what <c>unitFloors.create</c> did.</summary>
[TsExport]
public sealed class UnitFloorsResponse
{
    public int Units { get; set; }

    public int Matched { get; set; }

    public int NoHost { get; set; }

    public int NotPolygon { get; set; }

    public int DuplicateKey { get; set; }

    /// <summary>Create only: floors made.</summary>
    public int Created { get; set; }

    /// <summary>Create only: existing floors deleted.</summary>
    public int Deleted { get; set; }

    /// <summary>Create only: units whose floor Revit refused.</summary>
    public int Failed { get; set; }

    public UnitFloorsHost[] Hosts { get; set; } = Array.Empty<UnitFloorsHost>();

    public UnitFloorsUnit[] Unplaced { get; set; } = Array.Empty<UnitFloorsUnit>();

    /// <summary>Create only: the link file written beside the GeoPackage.</summary>
    public string? LinkFile { get; set; }

    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>An existing floor on the level, and how much of it the units cover.</summary>
[TsExport]
public sealed class UnitFloorsHost
{
    public long ElementId { get; set; }

    public string TypeName { get; set; } = string.Empty;

    public int UnitCount { get; set; }

    /// <summary>0 to 1.</summary>
    public double CoveredShare { get; set; }

    public bool Replaceable { get; set; }
}

/// <summary>A unit that gets no floor from its own floor type, and why.</summary>
[TsExport]
public sealed class UnitFloorsUnit
{
    public string Key { get; set; } = string.Empty;

    /// <summary>NoHost, NotPolygon, DuplicateKey or Failed.</summary>
    public string Status { get; set; } = string.Empty;

    public double AreaSquareMetres { get; set; }
}
