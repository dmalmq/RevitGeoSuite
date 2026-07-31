using System;
using System.Collections.Generic;
using RevitGeoSuite.FloorPlanExport.Core.Assignments;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.Preview;

/// <summary>
/// Re-resolves the category-derived fields of an already-extracted preview feature.
/// A category assignment cannot change geometry, so remapping only needs this pass -
/// never a re-extraction from Revit.
/// </summary>
/// <remarks>
/// The derivations here mirror what <c>UnitExtractor</c> writes onto a freshly
/// extracted unit feature, so that a reprojected feature is indistinguishable from
/// one produced by a full extraction under the same overrides.
/// </remarks>
public sealed class PreviewCategoryReprojector
{
    private readonly FloorCategoryResolver _floorCategoryResolver;
    private readonly RoomCategoryResolver _roomCategoryResolver;
    private readonly PreviewPaletteResolver _paletteResolver;

    public PreviewCategoryReprojector(
        ZoneCatalog zoneCatalog,
        IReadOnlyDictionary<string, string>? floorCategoryOverrides = null,
        IReadOnlyDictionary<string, string>? roomCategoryOverrides = null,
        PreviewPaletteResolver? paletteResolver = null)
    {
        if (zoneCatalog is null)
        {
            throw new ArgumentNullException(nameof(zoneCatalog));
        }

        _floorCategoryResolver = new FloorCategoryResolver(zoneCatalog, floorCategoryOverrides);
        _roomCategoryResolver = new RoomCategoryResolver(zoneCatalog, roomCategoryOverrides);
        _paletteResolver = paletteResolver ?? new PreviewPaletteResolver();
    }

    /// <summary>
    /// Resolves the category fields for a unit feature from its persisted assignment
    /// inputs. Pure: touches no Revit state and allocates no documents or views.
    /// </summary>
    public ReprojectedPreviewCategory Reproject(
        string? assignmentSourceKind,
        string? assignmentMappingKey,
        string? assignmentParsedCandidate,
        string? assignmentParameterName)
    {
        ZoneInfo zoneInfo;
        FloorCategoryResolutionSource resolutionSource;
        bool isUnassigned;

        if (IsRoomDerived(assignmentSourceKind))
        {
            ResolvedMappingCategory resolved = _roomCategoryResolver.Resolve(
                assignmentMappingKey ?? string.Empty,
                assignmentParameterName ?? string.Empty);
            zoneInfo = resolved.ZoneInfo;
            resolutionSource = resolved.ResolutionSource;
            isUnassigned = resolved.IsUnassigned;
        }
        else
        {
            ResolvedFloorCategory resolved = _floorCategoryResolver.Resolve(
                assignmentMappingKey ?? string.Empty,
                assignmentParsedCandidate);
            zoneInfo = resolved.ZoneInfo;
            resolutionSource = resolved.ResolutionSource;
            isUnassigned = resolved.IsUnassigned;
        }

        return new ReprojectedPreviewCategory(
            zoneInfo.Category,
            ImdfRestrictionNormalizer.NormalizeUnitRestriction(zoneInfo.Restriction),
            _paletteResolver.ResolveFillColor(zoneInfo.Category, zoneInfo.FillColor),
            isUnassigned,
            resolutionSource);
    }

    private static bool IsRoomDerived(string? assignmentSourceKind)
    {
        return string.Equals(assignmentSourceKind, "room", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ReprojectedPreviewCategory
{
    public ReprojectedPreviewCategory(
        string category,
        string? restriction,
        string fillColorHex,
        bool isUnassigned,
        FloorCategoryResolutionSource resolutionSource)
    {
        Category = category ?? string.Empty;
        Restriction = restriction;
        FillColorHex = fillColorHex ?? throw new ArgumentNullException(nameof(fillColorHex));
        IsUnassigned = isUnassigned;
        ResolutionSource = resolutionSource;
    }

    public string Category { get; }

    public string? Restriction { get; }

    public string FillColorHex { get; }

    public bool IsUnassigned { get; }

    public FloorCategoryResolutionSource ResolutionSource { get; }
}
