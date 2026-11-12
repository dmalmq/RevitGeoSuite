using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>Fix floor types proposes that a non-standard floor type is this standard type.</summary>
public sealed class FloorTypeProposal
{
    public FloorTypeProposal(FloorTypeInfo floorType, StandardFloorType standardType, string gisRestriction, string reason)
    {
        FloorType = floorType;
        StandardType = standardType;
        GisRestriction = gisRestriction;
        Reason = reason;
    }

    public FloorTypeInfo FloorType { get; }

    public StandardFloorType StandardType { get; }

    public string GisCategory => StandardType.GisCategory;

    /// <summary>The standard type's restriction, or for a type restricted by location the one its name states; may be empty.</summary>
    public string GisRestriction { get; }

    public string Material => StandardType.Material;

    /// <summary>"name" or "words in the name".</summary>
    public string Reason { get; }

    public string Label()
    {
        string restriction = GisRestriction.Length > 0 ? ", " + GisRestriction : string.Empty;
        return $"{FloorType.Name} ({FloorType.FloorIds.Count} floors) -> {StandardType.Name} [{GisCategory}{restriction}] by {Reason}";
    }
}

/// <summary>What Fix floor types applies for the floor types a user accepted, planned again from the model as it is now.</summary>
public sealed class FloorTypeFix
{
    public FloorTypeFix(IReadOnlyList<FloorTypeProposal> proposals, IReadOnlyList<long> echoFloorIds, IReadOnlyList<string> skipped)
    {
        Proposals = proposals;
        EchoFloorIds = echoFloorIds;
        Skipped = skipped;
    }

    public IReadOnlyList<FloorTypeProposal> Proposals { get; }

    public IReadOnlyList<long> EchoFloorIds { get; }

    /// <summary>One line per accepted floor type that no longer has a proposal, and why.</summary>
    public IReadOnlyList<string> Skipped { get; }
}

/// <summary>
/// Fix floor types: which standard floor type a non-standard one most likely is. A proposal never
/// changes a floor's type, so geometry and UniqueIds stay as they are; applying it writes
/// <c>gis_category</c>, <c>gis_restriction</c> and the standard material onto the existing type.
/// </summary>
public static class FloorTypeMatcher
{
    private const string Prefix = "j ";
    private const string Suffix = "_床";

    // Words in a type name, checked in order: all of these, none of these, the standard type.
    // The first rule whose words all appear wins.
    private static readonly (string[] Required, string[] Excluded, string Target)[] Rules =
    {
        (new[] { "新幹線", "ホーム" }, Array.Empty<string>(), "j 新幹線ホーム_床"),
        (new[] { "ホーム" }, Array.Empty<string>(), "j ホーム_床"),
        (new[] { "新幹線" }, new[] { "店舗", "トイレ", "待合", "事務" }, "j 新幹線コンコース_床"),
        (new[] { "男子", "トイレ" }, Array.Empty<string>(), "j 男子トイレ_床"),
        (new[] { "女子", "トイレ" }, Array.Empty<string>(), "j 女子トイレ_床"),
        (new[] { "多目的" }, Array.Empty<string>(), "j 多目的トイレ_床"),
        (new[] { "みどりの窓口" }, Array.Empty<string>(), "j みどりの窓口_床"),
        (new[] { "緑の窓口" }, Array.Empty<string>(), "j みどりの窓口_床"),
        (new[] { "店舗" }, Array.Empty<string>(), "j 店舗_床"),
        (new[] { "待合" }, Array.Empty<string>(), "j 待合室_床"),
        (new[] { "事務室" }, Array.Empty<string>(), "j 駅事務室_床"),
        (new[] { "駅務" }, Array.Empty<string>(), "j 駅事務室_床"),
        (new[] { "ラチ内" }, Array.Empty<string>(), "j ラチ内コンコース_床"),
        (new[] { "改札内" }, Array.Empty<string>(), "j ラチ内コンコース_床"),
        (new[] { "ラチ外" }, Array.Empty<string>(), "j ラチ外コンコース_床"),
        (new[] { "改札外" }, Array.Empty<string>(), "j ラチ外コンコース_床"),
        (new[] { "コンコース" }, Array.Empty<string>(), "j ラチ外コンコース_床"),
    };

    private static readonly string[] InsideWords = { "ラチ内", "改札内" };
    private static readonly string[] OutsideWords = { "ラチ外", "改札外" };

    /// <summary>The zone part of a type name: without the <c>j </c> prefix and the <c>_床</c> suffix.</summary>
    public static string ZoneName(string? typeName)
    {
        string name = GisToolStandard.Normalize(typeName);
        if (name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            name = name.Substring(Prefix.Length);
        }

        if (name.EndsWith(Suffix, StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - Suffix.Length);
        }

        return name.Trim();
    }

    /// <summary>True for a floor type in use that is not a standard type and has no usable gis_category.</summary>
    public static bool NeedsFix(FloorTypeInfo floorType, GisToolStandard standard)
    {
        if (floorType.FloorIds.Count == 0 || standard.FloorType(floorType.Name) != null)
        {
            return false;
        }

        return !standard.IsKnownCategory(floorType.GisCategory);
    }

    /// <summary>A proposal for every floor type that needs a fix and matches a standard type, most floors first.</summary>
    public static IReadOnlyList<FloorTypeProposal> Propose(ModelSnapshot snapshot, GisToolStandard standard)
    {
        List<FloorTypeProposal> proposals = new();
        foreach (FloorTypeInfo floorType in snapshot.FloorTypes.OrderByDescending(entry => entry.FloorIds.Count))
        {
            if (!NeedsFix(floorType, standard))
            {
                continue;
            }

            FloorTypeProposal? proposal = ProposeOne(floorType, standard);
            if (proposal != null)
            {
                proposals.Add(proposal);
            }
        }

        return proposals;
    }

    /// <summary>Floor types that need a fix but match no standard type; they stay for a person to decide.</summary>
    public static IReadOnlyList<FloorTypeInfo> Unmatched(ModelSnapshot snapshot, GisToolStandard standard)
    {
        HashSet<long> matched = new(Propose(snapshot, standard).Select(proposal => proposal.FloorType.ElementId));
        return snapshot.FloorTypes
            .Where(floorType => NeedsFix(floorType, standard) && !matched.Contains(floorType.ElementId))
            .ToList();
    }

    /// <summary>
    /// The proposals for <paramref name="acceptedIds"/>, matched again from <paramref name="snapshot"/>.
    /// An accepted type that is gone, or no longer needs a fix or matches, is reported in
    /// <see cref="FloorTypeFix.Skipped"/> instead of being dropped.
    /// </summary>
    public static FloorTypeFix Accept(ModelSnapshot snapshot, GisToolStandard standard, IEnumerable<long> acceptedIds)
    {
        Dictionary<long, FloorTypeProposal> byType = Propose(snapshot, standard).ToDictionary(proposal => proposal.FloorType.ElementId);
        List<FloorTypeProposal> proposals = new();
        List<string> skipped = new();
        foreach (long id in acceptedIds.Distinct())
        {
            if (byType.TryGetValue(id, out FloorTypeProposal? proposal))
            {
                proposals.Add(proposal);
                continue;
            }

            FloorTypeInfo? floorType = snapshot.FloorTypes.FirstOrDefault(entry => entry.ElementId == id);
            skipped.Add(floorType is null
                ? $"Floor type {id} is no longer in the model."
                : NeedsFix(floorType, standard)
                    ? $"Floor type {floorType.Name} no longer matches a standard type."
                    : $"Floor type {floorType.Name} no longer needs a fix.");
        }

        return new FloorTypeFix(proposals, EchoFloorIds(snapshot, proposals), skipped);
    }

    /// <summary>
    /// Why Fix floor types cannot write yet, or empty when it can: it writes gis_category and
    /// gis_restriction onto floor types, so both must be bound, and bound to types.
    /// </summary>
    public static string WriteBlocker(ModelSnapshot snapshot)
    {
        foreach (string name in new[] { GisSharedParameters.GisCategory, GisSharedParameters.GisRestriction })
        {
            if (!snapshot.BoundParameters.TryGetValue(name, out BoundParameterInfo? bound))
            {
                return $"{name} is not bound yet. Run Set up model first.";
            }

            if (bound.Binding != GisParameterBinding.Type)
            {
                return $"{name} is bound as an instance parameter, so it cannot be written onto floor types. Rebind it as a type parameter first.";
            }
        }

        return string.Empty;
    }

    public static FloorTypeProposal? ProposeOne(FloorTypeInfo floorType, GisToolStandard standard)
    {
        string zone = ZoneName(floorType.Name);
        foreach (StandardFloorType candidate in standard.FloorTypes)
        {
            if (ZoneName(candidate.Name) == zone)
            {
                return new FloorTypeProposal(floorType, candidate, Restriction(zone, candidate), "name");
            }
        }

        foreach ((string[] required, string[] excluded, string target) in Rules)
        {
            if (required.All(word => Contains(zone, word)) && !excluded.Any(word => Contains(zone, word)))
            {
                StandardFloorType? candidate = standard.FloorType(target);
                if (candidate != null)
                {
                    return new FloorTypeProposal(floorType, candidate, Restriction(zone, candidate), "words in the name");
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Floors of the proposed types whose own category equals the gis_category the proposal gives
    /// their type. Older exports wrote it back into every floor; once the type carries it, clearing
    /// the floor's copy changes nothing in the export and lets the type decide again.
    /// </summary>
    public static IReadOnlyList<long> EchoFloorIds(ModelSnapshot snapshot, IEnumerable<FloorTypeProposal> proposals)
    {
        Dictionary<long, string> given = new();
        foreach (FloorTypeProposal proposal in proposals)
        {
            given[proposal.FloorType.ElementId] = proposal.GisCategory.Trim().ToLowerInvariant();
        }

        return snapshot.Floors
            .Where(floor => given.TryGetValue(floor.TypeId, out string? category) && floor.Category.Trim().ToLowerInvariant() == category)
            .Select(floor => floor.ElementId)
            .ToList();
    }

    private static string Restriction(string zone, StandardFloorType standardType)
    {
        if (!string.IsNullOrEmpty(standardType.GisRestriction))
        {
            return standardType.GisRestriction!;
        }

        if (InsideWords.Any(word => Contains(zone, word)))
        {
            return "rachi_nai";
        }

        if (OutsideWords.Any(word => Contains(zone, word)))
        {
            return "rachi_gai";
        }

        return string.Empty;
    }

    private static bool Contains(string text, string word) => text.IndexOf(word, StringComparison.Ordinal) >= 0;
}
