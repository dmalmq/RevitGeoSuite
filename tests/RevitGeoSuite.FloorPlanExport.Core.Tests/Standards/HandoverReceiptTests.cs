using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

public sealed class HandoverReceiptTests
{
    private static readonly DateTime FixedUtc = new(2026, 1, 15, 9, 30, 0, DateTimeKind.Utc);
    private static readonly Guid DocVersion = new("11111111-2222-3333-4444-555555555555");

    private static HandoverReceiptModel SampleModel() =>
        new("model-1", DocVersion, 42, "host");

    private static HandoverReceipt SampleReceipt(
        HandoverReceiptVerdict verdict = HandoverReceiptVerdict.Pass,
        IReadOnlyList<HandoverRuleRow>? rules = null)
    {
        return new HandoverReceipt(
            kitVersion: "1.0.0",
            standardVersion: 2,
            kanizuRevitVersion: "2024.0.0",
            generatedUtc: FixedUtc,
            mode: "project",
            profile: "contractor",
            verdict: verdict,
            scorePercent: verdict == HandoverReceiptVerdict.Pass ? 100 : 80,
            model: SampleModel(),
            rules: rules ?? new[]
            {
                new HandoverRuleRow("KZ-PARAM-001", "standard-version", HandoverRuleSeverity.Error, HandoverRuleStatus.Pass, 0, "Standard version 2 stamped."),
                new HandoverRuleRow("KZ-FLOOR-001", "gis-materials", HandoverRuleSeverity.Error, HandoverRuleStatus.Pass, 0, "Materials match the theme."),
            });
    }

    private static JObject ParseRaw(string json)
    {
        using StringReader input = new(json);
        using JsonTextReader reader = new(input) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }

    [Fact]
    public void JsonIsCanonicalAndDeterministic()
    {
        HandoverReceipt a = SampleReceipt();
        HandoverReceipt b = SampleReceipt();
        Assert.Equal(a.ToJson(), b.ToJson());
    }

    [Fact]
    public void ComputeSha256IsLowercaseHexAndMatchesVerifier()
    {
        HandoverReceipt receipt = SampleReceipt();
        string json = receipt.ToJson();
        string sha = receipt.ComputeSha256();
        Assert.Equal(64, sha.Length);
        Assert.Equal(sha, sha.ToLowerInvariant());
        Assert.Matches("^[0-9a-f]{64}$", sha);

        HandoverVerificationResult result = HandoverReceiptVerifier.Verify(json, DocVersion, 42);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void JsonIncludesSha256AsLastWriteAndMatchesContentHash()
    {
        HandoverReceipt receipt = SampleReceipt();
        string json = receipt.ToJson();
        JObject root = ParseRaw(json);
        Assert.Equal("sha256", root.Properties().Last().Name);
        string declared = (string)root["sha256"]!;
        Assert.False(string.IsNullOrEmpty(declared));

        root.Remove("sha256");
        string canonical = CanonicalJsonHelper.Serialize(root);
        string computed = Sha256Helper.Hex(canonical);
        Assert.Equal(declared, computed);
    }

    [Fact]
    public void TamperedJsonFailsVerification()
    {
        HandoverReceipt receipt = SampleReceipt();
        string json = receipt.ToJson();
        string tampered = json.Replace("\"scorePercent\":100", "\"scorePercent\":99");
        Assert.NotEqual(json, tampered);

        HandoverVerificationResult result = HandoverReceiptVerifier.Verify(tampered, DocVersion, 42);
        Assert.False(result.IsValid);
        Assert.Equal(HandoverVerificationStatus.HashMismatch, result.Status);
    }

    [Fact]
    public void SaveCountMismatchFailsVerification()
    {
        HandoverReceipt receipt = SampleReceipt();
        HandoverVerificationResult result = HandoverReceiptVerifier.Verify(receipt.ToJson(), DocVersion, 43);
        Assert.False(result.IsValid);
        Assert.Equal(HandoverVerificationStatus.NumberOfSavesMismatch, result.Status);
    }

    [Fact]
    public void DocumentVersionGuidMismatchFailsVerification()
    {
        HandoverReceipt receipt = SampleReceipt();
        HandoverVerificationResult result = HandoverReceiptVerifier.Verify(receipt.ToJson(), Guid.NewGuid(), 42);
        Assert.False(result.IsValid);
        Assert.Equal(HandoverVerificationStatus.DocumentVersionGuidMismatch, result.Status);
    }

    [Fact]
    public void EmptyDocumentVersionGuidOrNegativeSaveCountReportsInvalidDocumentVersion()
    {
        HandoverReceipt receipt = SampleReceipt();

        HandoverVerificationResult emptyGuid = HandoverReceiptVerifier.Verify(receipt.ToJson(), Guid.Empty, 42);
        Assert.False(emptyGuid.IsValid);
        Assert.Equal(HandoverVerificationStatus.InvalidDocumentVersion, emptyGuid.Status);

        HandoverVerificationResult negativeSaves = HandoverReceiptVerifier.Verify(receipt.ToJson(), DocVersion, -1);
        Assert.False(negativeSaves.IsValid);
        Assert.Equal(HandoverVerificationStatus.InvalidDocumentVersion, negativeSaves.Status);
    }

    [Fact]
    public void MalformedJsonAndSchemaMismatchAreReported()
    {
        HandoverVerificationResult malformed = HandoverReceiptVerifier.Verify("not json", DocVersion, 42);
        Assert.False(malformed.IsValid);
        Assert.Equal(HandoverVerificationStatus.MalformedJson, malformed.Status);

        HandoverVerificationResult empty = HandoverReceiptVerifier.Verify(string.Empty, DocVersion, 42);
        Assert.False(empty.IsValid);
        Assert.Equal(HandoverVerificationStatus.MalformedJson, empty.Status);

        HandoverVerificationResult trailing = HandoverReceiptVerifier.Verify(SampleReceipt().ToJson() + "{}", DocVersion, 42);
        Assert.False(trailing.IsValid);
        Assert.Equal(HandoverVerificationStatus.MalformedJson, trailing.Status);

        HandoverVerificationResult duplicate = HandoverReceiptVerifier.Verify(
            "{\"schema\":\"kanizu.handover-receipt\",\"schema\":\"kanizu.handover-receipt\",\"version\":1}",
            DocVersion,
            42);
        Assert.False(duplicate.IsValid);
        Assert.Equal(HandoverVerificationStatus.MalformedJson, duplicate.Status);

        HandoverVerificationResult wrongSchema = HandoverReceiptVerifier.Verify(
            "{\"schema\":\"other.receipt\",\"version\":1}",
            DocVersion,
            42);
        Assert.False(wrongSchema.IsValid);
        Assert.Equal(HandoverVerificationStatus.SchemaMismatch, wrongSchema.Status);
    }

    [Fact]
    public void PrivacySanitizationStripsPathsCoordinatesUsernamesAndStationNames()
    {
        HandoverRuleRow row = new(
            "KZ-TPL-003",
            "qa-schedule-floors",
            HandoverRuleSeverity.Error,
            HandoverRuleStatus.Fail,
            2,
            @"Missing on C:\Users\alice\TokyoStation_35.6812_139.7671.rvt",
            new HandoverWaiver(
                "KZ-TPL-003",
                "Accepted for TokyoStation by alice",
                "alice@contractor.example",
                expiresUtc: FixedUtc.AddDays(30)));

        HandoverReceipt receipt = new(
            kitVersion: "1.0.0",
            standardVersion: 2,
            kanizuRevitVersion: "2024.0.0",
            generatedUtc: FixedUtc,
            mode: "project",
            profile: "contractor-alice",
            verdict: HandoverReceiptVerdict.PassWithWaivers,
            scorePercent: 90,
            model: new HandoverReceiptModel(@"C:\Users\alice\TokyoStation.rvt", DocVersion, 42, "host"),
            rules: new[] { row });

        Assert.Equal("host", receipt.Model.ModelAlias);
        Assert.Equal("default", receipt.Profile);

        string json = receipt.ToJson();
        string html = HandoverReceiptHtmlRenderer.Render(receipt);
        string[] forbidden = { "alice", "TokyoStation", "35.6812", "139.7671", @"C:\Users", "contractor.example" };
        foreach (string leak in forbidden)
        {
            Assert.DoesNotContain(leak, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(leak, html, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal("model-1", SampleReceipt().Model.ModelAlias);
        Assert.Equal("contractor", SampleReceipt().Profile);
    }

    [Fact]
    public void WaiverTurnsFailingRuleIntoPassWithWaiversAndKeepsRawFailureRow()
    {
        HandoverRuleRow failingRow = new(
            "KZ-TPL-003",
            "qa-schedule-floors",
            HandoverRuleSeverity.Error,
            HandoverRuleStatus.Fail,
            1,
            "QA schedule missing.",
            new HandoverWaiver("KZ-TPL-003", "Contractor accepted missing schedule.", "pm@contractor.example", expiresUtc: FixedUtc.AddDays(30)));

        HandoverReceipt receipt = new(
            kitVersion: "1.0.0",
            standardVersion: 2,
            kanizuRevitVersion: "2024.0.0",
            generatedUtc: FixedUtc,
            mode: "project",
            profile: "contractor",
            verdict: HandoverReceiptVerdict.PassWithWaivers,
            scorePercent: 90,
            model: SampleModel(),
            rules: new[] { failingRow });

        JObject root = ParseRaw(receipt.ToJson());
        Assert.Equal("PASS_WITH_WAIVERS", (string)root["verdict"]!);
        Assert.Equal("fail", (string)root["rules"]![0]!["status"]!);
        Assert.Equal(1, (int)root["rules"]![0]!["count"]!);
        Assert.Equal("KZ-TPL-003", (string)root["rules"]![0]!["waiver"]!["ruleId"]!);
        Assert.Equal("Maintainer", (string)root["rules"]![0]!["waiver"]!["approvedBy"]!);
    }

    [Fact]
    public void ExpiredAndUnboundedWaiversAreInactive()
    {
        HandoverWaiver expired = new("KZ-TPL-003", "Was accepted.", "pm", expiresUtc: FixedUtc.AddDays(-1));
        Assert.False(expired.IsActive(FixedUtc));

        HandoverWaiver unbounded = new("KZ-TPL-003", "No expiry.", "pm", expiresUtc: null);
        Assert.False(unbounded.IsActive(FixedUtc));

        HandoverWaiver live = new("KZ-TPL-003", "Still valid.", "pm", expiresUtc: FixedUtc.AddDays(10));
        Assert.True(live.IsActive(FixedUtc));
    }

    [Fact]
    public void FromReadinessReportAppliesActiveWaiversAndProducesPassWithWaivers()
    {
        ProjectReadinessReport report = BuildFailingReport();

        HandoverReceipt receipt = HandoverReceipt.FromReadinessReport(
            report,
            "1.0.0",
            "2024.0.0",
            "contractor",
            SampleModel(),
            FixedUtc,
            new[] { new HandoverWaiver("KZ-TPL-003", "Accepted", "pm", expiresUtc: FixedUtc.AddDays(30)) });

        Assert.Equal(HandoverReceiptVerdict.PassWithWaivers, receipt.Verdict);
        HandoverRuleRow waivedRow = receipt.Rules.Single(r => r.RuleId == "KZ-TPL-003");
        Assert.Equal(HandoverRuleStatus.Fail, waivedRow.Status);
        Assert.NotNull(waivedRow.Waiver);
    }

    [Fact]
    public void FromReadinessReportIgnoresExpiredOrUnboundedWaiversAndFails()
    {
        ProjectReadinessReport report = BuildFailingReport();

        HandoverReceipt expiredReceipt = HandoverReceipt.FromReadinessReport(
            report,
            "1.0.0",
            "2024.0.0",
            "contractor",
            SampleModel(),
            FixedUtc,
            new[] { new HandoverWaiver("KZ-TPL-003", "Expired", "pm", expiresUtc: FixedUtc.AddDays(-1)) });
        Assert.Equal(HandoverReceiptVerdict.Fail, expiredReceipt.Verdict);

        HandoverReceipt unboundedReceipt = HandoverReceipt.FromReadinessReport(
            report,
            "1.0.0",
            "2024.0.0",
            "contractor",
            SampleModel(),
            FixedUtc,
            new[] { new HandoverWaiver("KZ-TPL-003", "No expiry", "pm", expiresUtc: null) });
        Assert.Equal(HandoverReceiptVerdict.Fail, unboundedReceipt.Verdict);
    }

    [Fact]
    public void HtmlRendererProducesSelfContainedBilingualEscapedDocument()
    {
        HandoverReceipt passReceipt = SampleReceipt(HandoverReceiptVerdict.Pass);
        string passHtml = HandoverReceiptHtmlRenderer.Render(passReceipt);

        Assert.Contains("<!doctype html>", passHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<style>", passHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", passHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", passHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", passHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Kanizu Handover Receipt", passHtml, StringComparison.Ordinal);
        Assert.Contains("Kanizu 引渡しレシート", passHtml, StringComparison.Ordinal);
        Assert.Contains("PASS", passHtml, StringComparison.Ordinal);
        Assert.Contains("合格", passHtml, StringComparison.Ordinal);
        Assert.Contains(passReceipt.ComputeSha256(), passHtml, StringComparison.Ordinal);

        HandoverReceipt waivedReceipt = SampleReceipt(
            HandoverReceiptVerdict.PassWithWaivers,
            new[]
            {
                new HandoverRuleRow(
                    "KZ-TPL-003",
                    "qa-schedule-floors",
                    HandoverRuleSeverity.Error,
                    HandoverRuleStatus.Fail,
                    1,
                    "Missing schedule.",
                    new HandoverWaiver("KZ-TPL-003", "Waived", "pm", expiresUtc: FixedUtc.AddDays(15))),
            });
        string waivedHtml = HandoverReceiptHtmlRenderer.Render(waivedReceipt);
        Assert.Contains("PASS WITH WAIVERS", waivedHtml, StringComparison.Ordinal);
        Assert.Contains("条件付き合格", waivedHtml, StringComparison.Ordinal);
        Assert.Contains("2026-01-30", waivedHtml, StringComparison.Ordinal);

        HandoverReceipt failReceipt = SampleReceipt(HandoverReceiptVerdict.Fail);
        string failHtml = HandoverReceiptHtmlRenderer.Render(failReceipt);
        Assert.Contains("FAIL", failHtml, StringComparison.Ordinal);
        Assert.Contains("不合格", failHtml, StringComparison.Ordinal);

        Assert.Equal(
            "&lt;script&gt;alert(&quot;a&amp;&#39;b&quot;)&lt;/script&gt;",
            HandoverReceiptHtmlRenderer.EscapeHtml("<script>alert(\"a&'b\")</script>"));
    }

    private static ProjectReadinessReport BuildFailingReport()
    {
        ReadinessCheckItem failing = new(
            "qa-schedule-floors",
            "Kanizu GIS - Floors",
            ReadinessCheckStatus.Fail,
            "Missing QA schedule.",
            ReadinessActionKind.PrepareTemplate,
            new long[] { 1L });
        ReadinessCheckGroup group = new("template-views", "QA", new[] { failing });

        return new ProjectReadinessReport(
            ReadinessMode.Template,
            ReadinessMode.Template,
            new[] { group },
            new TemplatePreparationPlan(Array.Empty<QaSchedulePlanStep>(), createExportPlanTemplate: false, repairExportPlanTemplate: false),
            canSetupModel: false,
            canFixFloorTypes: false);
    }
}

internal static class CanonicalJsonHelper
{
    public static string Serialize(JToken token) =>
        SortKeys(token).ToString(Formatting.None).Replace("\r\n", "\n");

    private static JToken SortKeys(JToken token)
    {
        if (token is JObject obj)
        {
            JObject sorted = new();
            foreach (JProperty property in obj.Properties().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                sorted.Add(property.Name, SortKeys(property.Value));
            }

            return sorted;
        }

        if (token is JArray array)
        {
            JArray sortedArray = new();
            foreach (JToken item in array)
            {
                sortedArray.Add(SortKeys(item));
            }

            return sortedArray;
        }

        return token.DeepClone();
    }
}

internal static class Sha256Helper
{
    public static string Hex(string input)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input ?? string.Empty);
        using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(bytes);
        System.Text.StringBuilder builder = new(hash.Length * 2);
        for (int index = 0; index < hash.Length; index++)
        {
            builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
