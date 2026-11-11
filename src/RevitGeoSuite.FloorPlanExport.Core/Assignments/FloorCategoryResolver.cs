using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.Assignments;

public sealed class FloorCategoryResolver
{
    private readonly ZoneCatalog _zoneCatalog;
    private readonly Dictionary<string, string> _floorTypeOverrides;
    private readonly IReadOnlyList<string> _supportedCategories;

    public FloorCategoryResolver(
        ZoneCatalog zoneCatalog,
        IReadOnlyDictionary<string, string>? floorTypeOverrides = null)
    {
        _zoneCatalog = zoneCatalog ?? throw new ArgumentNullException(nameof(zoneCatalog));
        _floorTypeOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in floorTypeOverrides ?? EmptyOverrides)
        {
            string floorTypeName = NormalizeFloorTypeName(entry.Key);
            string category = NormalizeCategory(entry.Value);
            if (floorTypeName.Length == 0 || category.Length == 0)
            {
                continue;
            }

            _floorTypeOverrides[floorTypeName] = category;
        }

        _supportedCategories = BuildSupportedCategories(zoneCatalog);
    }

    public IReadOnlyList<string> SupportedCategories => _supportedCategories;

    public ResolvedFloorCategory Resolve(string rawFloorTypeName, string? parsedZoneCandidate)
    {
        string normalizedFloorTypeName = NormalizeFloorTypeName(rawFloorTypeName);
        string normalizedCandidate = NormalizeFloorTypeName(parsedZoneCandidate);

        if (normalizedFloorTypeName.Length > 0 &&
            _floorTypeOverrides.TryGetValue(normalizedFloorTypeName, out string overrideCategory) &&
            _zoneCatalog.TryGetCategoryInfo(overrideCategory, out ZoneInfo overrideInfo))
        {
            return new ResolvedFloorCategory(
                normalizedFloorTypeName,
                normalizedCandidate.Length == 0 ? null : normalizedCandidate,
                overrideInfo,
                FloorCategoryResolutionSource.Override,
                isUnassigned: false);
        }

        if (normalizedCandidate.Length > 0 &&
            _zoneCatalog.TryGetZoneInfo(normalizedCandidate, out ZoneInfo catalogInfo))
        {
            return new ResolvedFloorCategory(
                normalizedFloorTypeName,
                normalizedCandidate,
                catalogInfo,
                FloorCategoryResolutionSource.Catalog,
                isUnassigned: false);
        }

        return new ResolvedFloorCategory(
            normalizedFloorTypeName,
            normalizedCandidate.Length == 0 ? null : normalizedCandidate,
            _zoneCatalog.DefaultZoneInfo,
            FloorCategoryResolutionSource.FallbackUnspecified,
            isUnassigned: true);
    }

    /// <summary>
    /// Resolves a floor from its standard parameters first: the floor's own <c>category</c>, then
    /// its type's <c>gis_category</c> and <c>gis_restriction</c>. Without a usable category there,
    /// it falls back to the per-PC override and the floor type name, as
    /// <see cref="Resolve(string, string?)"/> does. A value that is not a known unit category is
    /// ignored with a warning.
    /// </summary>
    public ResolvedFloorCategory Resolve(
        string rawFloorTypeName,
        string? parsedZoneCandidate,
        FloorStandardParameters? parameters,
        ICollection<string>? warnings = null)
    {
        ResolvedFloorCategory byName = Resolve(rawFloorTypeName, parsedZoneCandidate);
        if (parameters is null)
        {
            return byName;
        }

        FloorCategoryResolutionSource source;
        ZoneInfo? categoryInfo;
        if (TryReadCategory(parameters.InstanceCategory, "category", byName.FloorTypeName, warnings, out categoryInfo) &&
            !IsLegacyExportEcho(categoryInfo!, byName, parameters))
        {
            source = FloorCategoryResolutionSource.InstanceParameter;
        }
        else if (TryReadCategory(parameters.TypeCategory, "gis_category", byName.FloorTypeName, warnings, out categoryInfo))
        {
            source = FloorCategoryResolutionSource.TypeParameter;
        }
        else
        {
            return byName;
        }

        // The restriction and fill a parameter category inherits come from the parsed name alone; a
        // per-PC override ranks below the parameters, so it must not change them.
        string category = categoryInfo!.Category;
        string parsedName = NormalizeFloorTypeName(parsedZoneCandidate);
        ZoneInfo? nameInfo = null;
        if (parsedName.Length > 0 && _zoneCatalog.TryGetZoneInfo(parsedName, out ZoneInfo catalogInfo) &&
            string.Equals(catalogInfo.Category, category, StringComparison.OrdinalIgnoreCase))
        {
            nameInfo = catalogInfo;
        }

        string? restriction = NormalizeOptional(parameters.TypeRestriction) ?? nameInfo?.Restriction;
        string fillColor = nameInfo?.FillColor ?? categoryInfo.FillColor;

        return new ResolvedFloorCategory(
            byName.FloorTypeName,
            byName.ParsedZoneCandidate,
            new ZoneInfo(category, fillColor, restriction).Normalized(),
            source,
            isUnassigned: false);
    }

    /// <summary>
    /// Exports before the Kanizu GIS standard wrote the resolved category back into every floor's own
    /// <c>category</c>. In a model not yet set up with the standard, a value that equals what the type
    /// name and per-PC override give is taken as that echo, not as a deliberate override, when the type
    /// has its own <c>gis_category</c>; otherwise those models could never pick up their type parameters.
    /// Once the model is set up (Set up model clears the echoes), every value counts.
    /// </summary>
    private static bool IsLegacyExportEcho(ZoneInfo instance, ResolvedFloorCategory byName, FloorStandardParameters parameters)
    {
        return !parameters.ModelSetUpWithStandard &&
               !byName.IsUnassigned &&
               !string.IsNullOrWhiteSpace(parameters.TypeCategory) &&
               string.Equals(instance.Category, byName.ZoneInfo.Category, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryReadCategory(
        string? value,
        string parameterName,
        string floorTypeName,
        ICollection<string>? warnings,
        out ZoneInfo? info)
    {
        info = null;
        string? category = NormalizeOptional(value);
        if (category == null)
        {
            return false;
        }

        if (!string.Equals(category, "unspecified", StringComparison.OrdinalIgnoreCase) &&
            _zoneCatalog.TryGetCategoryInfo(category, out ZoneInfo known))
        {
            info = known;
            return true;
        }

        warnings?.Add(
            $"Floor type '{floorTypeName}': {parameterName} '{category}' is not a known unit category, so it was ignored.");
        return false;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    private static IReadOnlyList<string> BuildSupportedCategories(ZoneCatalog zoneCatalog)
    {
        return ImdfUnitCategoryCatalog.GetCategories(includeLegacy: true, includeUnspecified: false)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeFloorTypeName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value!.Trim();
    }

    private static string NormalizeCategory(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value!.Trim();
    }

    private static IReadOnlyDictionary<string, string> EmptyOverrides =>
        new Dictionary<string, string>(StringComparer.Ordinal);
}
