using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>Fix floor types proposes that a non-standard floor type is this standard type.</summary>
public sealed class FloorTypeProposal
{
    public FloorTypeProposal(FloorTypeInfo floorType, StandardFloorType standardType, string gisRestriction, string reason, string? newName = null)
    {
        FloorType = floorType;
        StandardType = standardType;
        GisRestriction = gisRestriction;
        Reason = reason;
        NewName = newName;
    }

    public FloorTypeInfo FloorType { get; }

    public StandardFloorType StandardType { get; }

    public string GisCategory => StandardType.GisCategory;

    /// <summary>The standard type's restriction, or for a type restricted by location the one its name states; may be empty.</summary>
    public string GisRestriction { get; }

    public string Material => StandardType.Material;

    /// <summary>"name", "old name" or "words in the name".</summary>
    public string Reason { get; }

    /// <summary>
    /// The standard name the type is renamed to: set only for an old name, and only while no floor
    /// type in the model has that name yet. Null leaves the name as it is.
    /// </summary>
    public string? NewName { get; }

    public FloorTypeProposal WithoutRename() => NewName is null ? this : new FloorTypeProposal(FloorType, StandardType, GisRestriction, Reason);

    public string Label()
    {
        string restriction = GisRestriction.Length > 0 ? ", " + GisRestriction : string.Empty;
        string rename = NewName is null ? string.Empty : $", renames to {NewName}";
        return $"{FloorType.Name} ({FloorType.FloorIds.Count} floors) -> {StandardType.Name} [{GisCategory}{restriction}] by {Reason}{rename}";
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

    // Words in a type name, checked in order: all of these, none of these, the standard types it can be.
    // The first rule whose words all appear wins. A rule with two targets (a gate side) is narrowed
    // to one when the name says inside or outside.
    private static readonly (string[] Required, string[] Excluded, string[] Targets)[] Rules =
    {
        (new[] { "新幹線", "ホーム" }, Array.Empty<string>(), new[] { "j 新幹線ホーム_床" }),
        (new[] { "ホーム" }, Array.Empty<string>(), new[] { "j ホーム_床" }),
        (new[] { "新幹線" }, new[] { "店舗", "トイレ", "待合", "事務" }, new[] { "j 新幹線コンコース_床" }),
        (new[] { "男子", "トイレ" }, Array.Empty<string>(), new[] { "j 男子トイレ_床" }),
        (new[] { "女子", "トイレ" }, Array.Empty<string>(), new[] { "j 女子トイレ_床" }),
        (new[] { "多目的" }, Array.Empty<string>(), new[] { "j 多目的トイレ_床" }),
        (new[] { "みどりの窓口" }, Array.Empty<string>(), new[] { "j みどりの窓口_床" }),
        (new[] { "緑の窓口" }, Array.Empty<string>(), new[] { "j みどりの窓口_床" }),
        (new[] { "店舗" }, Array.Empty<string>(), new[] { "j ラチ外店舗_床", "j ラチ内店舗_床" }),
        (new[] { "待合" }, Array.Empty<string>(), new[] { "j 待合室_床" }),
        (new[] { "事務室" }, Array.Empty<string>(), new[] { "j 駅事務室_床" }),
        (new[] { "駅務" }, Array.Empty<string>(), new[] { "j 駅事務室_床" }),
        (new[] { "スロープ" }, Array.Empty<string>(), new[] { "j ラチ外スロープ_床", "j ラチ内スロープ_床" }),
        (new[] { "案内" }, Array.Empty<string>(), new[] { "j 案内所_床" }),
        (new[] { "喫煙" }, Array.Empty<string>(), new[] { "j 喫煙所_床" }),
        (new[] { "ベビー" }, Array.Empty<string>(), new[] { "j ベビー休憩室_床" }),
        (new[] { "授乳" }, Array.Empty<string>(), new[] { "j ベビー休憩室_床" }),
        (new[] { "屋外" }, Array.Empty<string>(), new[] { "j 屋外_床" }),
        (new[] { "屋上" }, Array.Empty<string>(), new[] { "j 屋外_床" }),
        (new[] { "道路" }, Array.Empty<string>(), new[] { "j 道路_床" }),
        (new[] { "その他" }, Array.Empty<string>(), new[] { "j その他_床" }),
        (new[] { "ラチ内" }, Array.Empty<string>(), new[] { "j ラチ内コンコース_床" }),
        (new[] { "改札内" }, Array.Empty<string>(), new[] { "j ラチ内コンコース_床" }),
        (new[] { "ラチ外" }, Array.Empty<string>(), new[] { "j ラチ外コンコース_床" }),
        (new[] { "改札外" }, Array.Empty<string>(), new[] { "j ラチ外コンコース_床" }),
        (new[] { "コンコース" }, Array.Empty<string>(), new[] { "j ラチ外コンコース_床" }),
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

    /// <summary>
    /// True for a floor type in use that is not a standard type and either has no usable gis_category
    /// or carries an old name that one of its current names, still free in the model, can replace.
    /// A renamed type is a standard type, so the check converges.
    /// </summary>
    public static bool NeedsFix(FloorTypeInfo floorType, ModelSnapshot snapshot, GisToolStandard standard)
    {
        if (floorType.FloorIds.Count == 0 || standard.FloorType(floorType.Name) != null)
        {
            return false;
        }

        return !standard.IsKnownCategory(floorType.GisCategory) || FreeTargets(floorType, snapshot, standard).Count > 0;
    }

    /// <summary>
    /// The current names an old-named type could take that no floor type in the model has yet. A
    /// type that already states its gate side can only take the name of that side.
    /// </summary>
    public static IReadOnlyList<string> FreeTargets(FloorTypeInfo floorType, ModelSnapshot snapshot, GisToolStandard standard) =>
        FormerCandidates(floorType, standard)
            .Select(standardType => standardType.Name)
            .Where(name => snapshot.FloorTypeByName(name) is null)
            .ToList();

    private static List<StandardFloorType> FormerCandidates(FloorTypeInfo floorType, GisToolStandard standard)
    {
        List<StandardFloorType> former = Resolve(standard, standard.FormerTargets(floorType.Name));
        string side = floorType.GisRestriction is "rachi_nai" or "rachi_gai" ? floorType.GisRestriction : Side(ZoneName(floorType.Name));
        return former.Count > 0 ? NarrowBySide(former, side) : former;
    }

    /// <summary>The proposals for every floor type that needs a fix and matches a standard type, most floors first.</summary>
    public static IReadOnlyList<FloorTypeProposal> Propose(ModelSnapshot snapshot, GisToolStandard standard)
    {
        List<FloorTypeProposal> proposals = new();
        foreach (FloorTypeInfo floorType in snapshot.FloorTypes.OrderByDescending(entry => entry.FloorIds.Count))
        {
            if (NeedsFix(floorType, snapshot, standard))
            {
                proposals.AddRange(Candidates(floorType, snapshot, standard));
            }
        }

        return proposals;
    }

    /// <summary>Floor types that need a fix but match no standard type; they stay for a person to decide.</summary>
    public static IReadOnlyList<FloorTypeInfo> Unmatched(ModelSnapshot snapshot, GisToolStandard standard)
    {
        HashSet<long> matched = new(Propose(snapshot, standard).Select(proposal => proposal.FloorType.ElementId));
        return snapshot.FloorTypes
            .Where(floorType => NeedsFix(floorType, snapshot, standard) && !matched.Contains(floorType.ElementId))
            .ToList();
    }

    /// <summary>
    /// The proposals for the accepted (floor type id, standard type name) pairs, matched again from
    /// <paramref name="snapshot"/>. A type that is gone, no longer needs a fix or no longer matches,
    /// or that has two accepted candidates, is reported in <see cref="FloorTypeFix.Skipped"/> instead
    /// of being dropped. When two accepted types would take one new name, the one with more floors
    /// renames and the other only gets its parameters.
    /// </summary>
    public static FloorTypeFix Accept(ModelSnapshot snapshot, GisToolStandard standard, IEnumerable<(long FloorTypeId, string StandardTypeName)> accepted)
    {
        IReadOnlyList<FloorTypeProposal> current = Propose(snapshot, standard);
        List<FloorTypeProposal> proposals = new();
        List<string> skipped = new();
        foreach (IGrouping<long, (long FloorTypeId, string StandardTypeName)> group in accepted.Distinct().GroupBy(entry => entry.FloorTypeId))
        {
            long id = group.Key;
            FloorTypeInfo? floorType = snapshot.FloorTypes.FirstOrDefault(entry => entry.ElementId == id);
            if (group.Count() > 1)
            {
                skipped.Add($"Floor type {floorType?.Name ?? id.ToString(System.Globalization.CultureInfo.InvariantCulture)} has more than one accepted standard type. Choose one.");
                continue;
            }

            string standardName = group.Single().StandardTypeName;
            FloorTypeProposal? proposal = current.FirstOrDefault(entry => entry.FloorType.ElementId == id && entry.StandardType.Name == standardName);
            if (proposal != null)
            {
                proposals.Add(proposal);
                continue;
            }

            skipped.Add(floorType is null
                ? $"Floor type {id} is no longer in the model."
                : NeedsFix(floorType, snapshot, standard)
                    ? $"Floor type {floorType.Name} no longer matches {standardName}."
                    : $"Floor type {floorType.Name} no longer needs a fix.");
        }

        foreach (IGrouping<string?, FloorTypeProposal> clash in proposals.Where(entry => entry.NewName != null).GroupBy(entry => entry.NewName).Where(entry => entry.Count() > 1))
        {
            FloorTypeProposal winner = clash.OrderByDescending(entry => entry.FloorType.FloorIds.Count).First();
            foreach (FloorTypeProposal loser in clash.Where(entry => entry != winner))
            {
                proposals[proposals.IndexOf(loser)] = loser.WithoutRename();
                skipped.Add($"Floor type {loser.FloorType.Name} keeps its name: {winner.FloorType.Name} is renamed to {clash.Key}.");
            }
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

    /// <summary>
    /// The standard types a floor type can be, first match wins: the same zone name, an old name
    /// (one proposal per current name, narrowed by the type's own restriction or words saying inside
    /// or outside), then words in the name. Empty when none fits.
    /// </summary>
    public static IReadOnlyList<FloorTypeProposal> Candidates(FloorTypeInfo floorType, ModelSnapshot snapshot, GisToolStandard standard)
    {
        string zone = ZoneName(floorType.Name);
        foreach (StandardFloorType standardType in standard.FloorTypes)
        {
            if (ZoneName(standardType.Name) == zone)
            {
                return new[] { new FloorTypeProposal(floorType, standardType, Restriction(zone, standardType), "name") };
            }
        }

        List<StandardFloorType> former = FormerCandidates(floorType, standard);
        if (former.Count > 0)
        {
            return former
                .Select(standardType => new FloorTypeProposal(
                    floorType,
                    standardType,
                    Restriction(zone, standardType),
                    "old name",
                    snapshot.FloorTypeByName(standardType.Name) is null ? standardType.Name : null))
                .ToList();
        }

        foreach ((string[] required, string[] excluded, string[] targets) in Rules)
        {
            if (!required.All(word => Contains(zone, word)) || excluded.Any(word => Contains(zone, word)))
            {
                continue;
            }

            List<StandardFloorType> matched = Resolve(standard, targets);
            if (matched.Count > 0)
            {
                return NarrowBySide(matched, Side(zone))
                    .Select(standardType => new FloorTypeProposal(floorType, standardType, Restriction(zone, standardType), "words in the name"))
                    .ToList();
            }
        }

        return Array.Empty<FloorTypeProposal>();
    }

    private static List<StandardFloorType> Resolve(GisToolStandard standard, IEnumerable<string> names) =>
        names.Select(standard.FloorType).Where(standardType => standardType != null).Select(standardType => standardType!).ToList();

    private static List<StandardFloorType> NarrowBySide(List<StandardFloorType> targets, string side)
    {
        List<StandardFloorType> picked = targets.Where(target => side.Length > 0 && target.GisRestriction == side).ToList();
        return picked.Count > 0 ? picked : targets;
    }

    private static string Side(string zone)
    {
        if (InsideWords.Any(word => Contains(zone, word)))
        {
            return "rachi_nai";
        }

        return OutsideWords.Any(word => Contains(zone, word)) ? "rachi_gai" : string.Empty;
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

        return Side(zone);
    }

    private static bool Contains(string text, string word) => text.IndexOf(word, StringComparison.Ordinal) >= 0;
}
