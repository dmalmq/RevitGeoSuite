using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Assignments;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>
/// One change to <c>gis-rules.json</c>, per key: a rule by type and match, or a profile by name.
/// A writer applies its operations to a fresh read of the file, so two PCs changing different
/// keys both land; on the same key the last write wins.
/// </summary>
public abstract class GisRulesOperation
{
    public static GisRulesOperation UpsertRule(MappingRule rule)
    {
        return new UpsertRuleOperation(GisRulesRule.From(rule ?? throw new ArgumentNullException(nameof(rule))));
    }

    public static GisRulesOperation DeleteRule(MappingRuleType type, string match)
    {
        return new DeleteRuleOperation(type, MappingRule.NormalizeMatchValue(match));
    }

    public static GisRulesOperation UpsertProfile(GisRulesProfile profile)
    {
        GisRulesProfile copy = (profile ?? throw new ArgumentNullException(nameof(profile))).Clone();
        copy.Name = copy.Name.Trim();
        if (copy.Name.Length == 0)
        {
            throw new ArgumentException("A profile name is required.", nameof(profile));
        }

        return new UpsertProfileOperation(copy);
    }

    /// <summary>Adds the profile only when the file has none of that name, so the first PC to create it wins.</summary>
    public static GisRulesOperation AddProfile(GisRulesProfile profile)
    {
        return new AddProfileOperation((UpsertProfileOperation)UpsertProfile(profile));
    }

    public static GisRulesOperation DeleteProfile(string name)
    {
        return new DeleteProfileOperation((name ?? string.Empty).Trim());
    }

    internal abstract void ApplyTo(GisRulesFile file);

    /// <summary>True when <paramref name="file"/> holds what this operation leaves.</summary>
    internal abstract bool IsReflectedIn(GisRulesFile file);

    private static bool SameKey(GisRulesRule rule, MappingRuleType type, string match)
    {
        return rule.Type == type && string.Equals(rule.Match, match, StringComparison.Ordinal);
    }

    private static bool SameName(GisRulesProfile profile, string name)
    {
        return string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class UpsertRuleOperation : GisRulesOperation
    {
        private readonly GisRulesRule _rule;

        public UpsertRuleOperation(GisRulesRule rule) => _rule = rule;

        internal override void ApplyTo(GisRulesFile file)
        {
            int index = file.CategoryRules.FindIndex(existing => SameKey(existing, _rule.Type, _rule.Match));
            if (index >= 0)
            {
                file.CategoryRules[index] = _rule;
            }
            else
            {
                file.CategoryRules.Add(_rule);
            }
        }

        internal override bool IsReflectedIn(GisRulesFile file)
        {
            return file.CategoryRules.Any(existing =>
                SameKey(existing, _rule.Type, _rule.Match) && string.Equals(existing.Value, _rule.Value, StringComparison.Ordinal));
        }
    }

    private sealed class DeleteRuleOperation : GisRulesOperation
    {
        private readonly MappingRuleType _type;
        private readonly string _match;

        public DeleteRuleOperation(MappingRuleType type, string match)
        {
            _type = type;
            _match = match;
        }

        internal override void ApplyTo(GisRulesFile file)
        {
            file.CategoryRules.RemoveAll(existing => SameKey(existing, _type, _match));
        }

        internal override bool IsReflectedIn(GisRulesFile file)
        {
            return !file.CategoryRules.Any(existing => SameKey(existing, _type, _match));
        }
    }

    private sealed class UpsertProfileOperation : GisRulesOperation
    {
        private readonly GisRulesProfile _profile;

        public UpsertProfileOperation(GisRulesProfile profile) => _profile = profile;

        public string Name => _profile.Name;

        internal override void ApplyTo(GisRulesFile file)
        {
            int index = file.Profiles.FindIndex(existing => SameName(existing, _profile.Name));
            if (index >= 0)
            {
                file.Profiles[index] = _profile.Clone();
            }
            else
            {
                file.Profiles.Add(_profile.Clone());
            }
        }

        internal override bool IsReflectedIn(GisRulesFile file)
        {
            GisRulesProfile? existing = file.FindProfile(_profile.Name);
            return existing != null && JToken.DeepEquals(existing.ToToken(), _profile.ToToken());
        }
    }

    private sealed class AddProfileOperation : GisRulesOperation
    {
        private readonly UpsertProfileOperation _upsert;

        public AddProfileOperation(UpsertProfileOperation upsert) => _upsert = upsert;

        internal override void ApplyTo(GisRulesFile file)
        {
            if (!IsReflectedIn(file))
            {
                _upsert.ApplyTo(file);
            }
        }

        internal override bool IsReflectedIn(GisRulesFile file)
        {
            return file.FindProfile(_upsert.Name) != null;
        }
    }

    private sealed class DeleteProfileOperation : GisRulesOperation
    {
        private readonly string _name;

        public DeleteProfileOperation(string name) => _name = name;

        internal override void ApplyTo(GisRulesFile file)
        {
            file.Profiles.RemoveAll(existing => SameName(existing, _name));
        }

        internal override bool IsReflectedIn(GisRulesFile file)
        {
            return file.FindProfile(_name) == null;
        }
    }
}
