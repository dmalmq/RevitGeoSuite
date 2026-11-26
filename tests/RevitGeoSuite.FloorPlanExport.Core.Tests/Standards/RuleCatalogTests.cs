using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

public sealed class RuleCatalogTests
{
    [Fact]
    public void EntriesHaveUniqueCanonicalIds()
    {
        HashSet<string> seen = new();
        foreach (RuleCatalogEntry entry in RuleCatalog.Entries)
        {
            Assert.True(seen.Add(entry.RuleId), $"Duplicate rule id '{entry.RuleId}'.");
            Assert.StartsWith("KZ-", entry.RuleId);
        }
    }

    [Theory]
    [InlineData("KZ-PARAM-001")]
    [InlineData("KZ-PARAM-002")]
    [InlineData("KZ-TPL-001")]
    [InlineData("KZ-TPL-002")]
    [InlineData("KZ-TPL-003")]
    [InlineData("KZ-FLOOR-001")]
    [InlineData("KZ-FLOOR-002")]
    [InlineData("KZ-FLOOR-003")]
    [InlineData("KZ-RFA-001")]
    [InlineData("KZ-RFA-002")]
    [InlineData("KZ-RFA-003")]
    [InlineData("KZ-RFA-004")]
    [InlineData("KZ-RFA-005")]
    [InlineData("KZ-RFA-006")]
    [InlineData("KZ-RFA-007")]
    [InlineData("KZ-RFA-008")]
    [InlineData("KZ-RFA-009")]
    [InlineData("KZ-RFA-010")]
    [InlineData("KZ-RFA-011")]
    [InlineData("KZ-RFA-012")]
    [InlineData("KZ-RFA-013")]
    [InlineData("KZ-ELEM-001")]
    [InlineData("KZ-ELEM-002")]
    [InlineData("KZ-ELEM-003")]
    [InlineData("KZ-ELEM-004")]
    [InlineData("KZ-ELEM-005")]
    [InlineData("KZ-VIEW-001")]
    [InlineData("KZ-VIEW-002")]
    [InlineData("KZ-VIEW-003")]
    [InlineData("KZ-VIEW-004")]
    [InlineData("KZ-VIEW-005")]
    [InlineData("KZ-FED-001")]
    [InlineData("KZ-FED-002")]
    [InlineData("KZ-FED-003")]
    [InlineData("KZ-FED-004")]
    [InlineData("KZ-FED-009")]
    public void EveryExpectedCanonicalIdIsRegistered(string ruleId)
    {
        Assert.True(RuleCatalog.TryLookup(ruleId, out RuleCatalogEntry entry));
        Assert.Equal(ruleId, entry.RuleId);
        Assert.False(string.IsNullOrWhiteSpace(entry.Title));
        Assert.False(string.IsNullOrWhiteSpace(entry.Description));
    }

    [Theory]
    [InlineData("standard-version", "KZ-PARAM-001")]
    [InlineData("shared-parameters", "KZ-PARAM-002")]
    [InlineData("gis-materials", "KZ-FLOOR-001")]
    [InlineData("standard-floor-types", "KZ-FLOOR-002")]
    [InlineData("untyped-floor-types", "KZ-FLOOR-003")]
    [InlineData("circulation-families", "KZ-RFA-001")]
    [InlineData("door-families", "KZ-RFA-002")]
    [InlineData("view-template-export-plan", "KZ-VIEW-001")]
    [InlineData("project-crs", "KZ-FED-001")]
    [InlineData("model-ids", "KZ-FED-002")]
    [InlineData("level-metadata", "KZ-FED-003")]
    [InlineData("qa-schedule-floors", "KZ-TPL-003")]
    [InlineData("qa-schedule-circulation", "KZ-TPL-003")]
    [InlineData("qa-schedule-doors", "KZ-TPL-003")]
    [InlineData("showcase-elements", "KZ-ELEM-005")]
    [InlineData("showcase-model-role", "KZ-FED-009")]
    public void LegacyItemIdsResolveToCanonicalRule(string legacyId, string expectedRuleId)
    {
        Assert.Equal(expectedRuleId, RuleCatalog.CanonicalRuleId(legacyId));
        Assert.True(RuleCatalog.TryLookupByLegacyItemId(legacyId, out RuleCatalogEntry entry));
        Assert.Equal(expectedRuleId, entry.RuleId);
    }

    [Theory]
    [InlineData("KZ-ELEM-005")]
    [InlineData("KZ-FED-009")]
    public void ShowcaseIsolationRulesHaveErrorSeverityAndBilingualMessages(string ruleId)
    {
        RuleCatalogEntry entry = RuleCatalog.Lookup(ruleId);
        Assert.Equal(RuleSeverity.Error, entry.Severity);
        Assert.False(string.IsNullOrWhiteSpace(entry.Title));
        Assert.False(string.IsNullOrWhiteSpace(entry.Description));
        Assert.False(string.IsNullOrWhiteSpace(entry.TitleJa));
        Assert.False(string.IsNullOrWhiteSpace(entry.DescriptionJa));
    }

    [Fact]
    public void DeepRfaLinterRules001To013HaveErrorSeverityAndBilingualMessages()
    {
        for (int i = 1; i <= 13; i++)
        {
            string ruleId = $"KZ-RFA-{i:000}";
            RuleCatalogEntry entry = RuleCatalog.Lookup(ruleId);
            Assert.Equal("family", entry.Group);
            Assert.Equal(RuleSeverity.Error, entry.Severity);
            Assert.False(string.IsNullOrWhiteSpace(entry.Title), $"{ruleId} missing English title.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Description), $"{ruleId} missing English description.");
            Assert.False(string.IsNullOrWhiteSpace(entry.TitleJa), $"{ruleId} missing Japanese title.");
            Assert.False(string.IsNullOrWhiteSpace(entry.DescriptionJa), $"{ruleId} missing Japanese description.");
        }
    }

    [Fact]
    public void UnknownLegacyItemIdIsReturnedUnchanged()
    {
        Assert.Equal("some-new-id", RuleCatalog.CanonicalRuleId("some-new-id"));
    }
}
