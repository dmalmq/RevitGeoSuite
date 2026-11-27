using System;
using RevitGeoSuite.Core.Text;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DObjectMetadata
{
    public string RevitElementId { get; set; } = string.Empty;

    public string RevitUniqueId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string FamilyName { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public string LevelName { get; set; } = "Unassigned";

    public string LevelKey { get; set; } = Tiles3DLevelMetadata.UnassignedLevelKey;

    /// <summary>
    /// The Revit <c>Level.UniqueId</c> of the element's level, empty when it has none. Stable
    /// across renames, unlike <see cref="LevelKey"/>, and unique per document, so together
    /// with <see cref="SourceDocument"/> it names one level even when two links both have a "1F".
    /// </summary>
    public string LevelUniqueId { get; set; } = string.Empty;

    public double LevelElevationMeters { get; set; }

    public double HeightMeters { get; set; }

    public double MinZMeters { get; set; }

    public double MaxZMeters { get; set; }

    public string SourceDocument { get; set; } = string.Empty;

    public string SourceLinkName { get; set; } = string.Empty;

    /// <summary>
    /// The <c>RevitLinkInstance.UniqueId</c> the element came through, empty for host elements.
    /// One linked file placed twice gives the same element UniqueIds twice; this tells them apart.
    /// </summary>
    public string SourceLinkInstanceUniqueId { get; set; } = string.Empty;

    /// <summary>Optional Kanizu GIS <c>id</c> shared parameter value for 2D/3D joins.</summary>
    public string GisId { get; set; } = string.Empty;

    /// <summary>Optional Kanizu GIS <c>gis_key</c> shared parameter value (exported <c>revit_key</c>).</summary>
    public string GisRevitKey { get; set; } = string.Empty;

    /// <summary>Optional Kanizu GIS <c>gis_kind</c> shared parameter value (e.g. escalator, elevator).</summary>
    public string GisKind { get; set; } = string.Empty;

    /// <summary>Optional Kanizu GIS category (<c>category</c> instance override or <c>gis_category</c> type value).</summary>
    public string GisCategory { get; set; } = string.Empty;

    /// <summary>Optional Kanizu GIS <c>level_id</c> shared parameter value from the element's level.</summary>
    public string GisLevelId { get; set; } = string.Empty;

    public bool HasGisMetadata =>
        !string.IsNullOrWhiteSpace(GisId)
        || !string.IsNullOrWhiteSpace(GisRevitKey)
        || !string.IsNullOrWhiteSpace(GisKind)
        || !string.IsNullOrWhiteSpace(GisCategory)
        || !string.IsNullOrWhiteSpace(GisLevelId);

    public Tiles3DObjectMetadata Clone()
    {
        return new Tiles3DObjectMetadata
        {
            RevitElementId = RevitElementId,
            RevitUniqueId = RevitUniqueId,
            Name = Name,
            Category = Category,
            FamilyName = FamilyName,
            TypeName = TypeName,
            LevelName = LevelName,
            LevelKey = LevelKey,
            LevelUniqueId = LevelUniqueId,
            LevelElevationMeters = LevelElevationMeters,
            HeightMeters = HeightMeters,
            MinZMeters = MinZMeters,
            MaxZMeters = MaxZMeters,
            SourceDocument = SourceDocument,
            SourceLinkName = SourceLinkName,
            SourceLinkInstanceUniqueId = SourceLinkInstanceUniqueId,
            GisId = GisId,
            GisRevitKey = GisRevitKey,
            GisKind = GisKind,
            GisCategory = GisCategory,
            GisLevelId = GisLevelId
        };
    }
}

public static class Tiles3DLevelMetadata
{
    public const string UnassignedLevelKey = LevelKey.Unassigned;
    public const string UnassignedLevelName = LevelKey.UnassignedName;

    public static string BuildLevelKey(string? levelName) => LevelKey.Build(levelName);
}
