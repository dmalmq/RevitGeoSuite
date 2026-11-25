using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>Final verdict a handover receipt carries once waivers have been applied.</summary>
public enum HandoverReceiptVerdict
{
    Pass,
    PassWithWaivers,
    Fail,
}

/// <summary>Severity surface used inside a handover receipt (independent of UI nouns).</summary>
public enum HandoverRuleSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Status of one rule row inside a handover receipt.</summary>
public enum HandoverRuleStatus
{
    Pass,
    Warn,
    Fail,
    NotApplicable,
}

/// <summary>
/// One external maintainer waiver, matched by canonical <c>KZ-*</c> ID and subject to a UTC expiry. A waived failing rule turns the receipt verdict into
/// <see cref="HandoverReceiptVerdict.PassWithWaivers"/> but the rule row keeps its raw status/count.
/// </summary>
public sealed class HandoverWaiver
{
    public HandoverWaiver(string ruleId, string reason, string approvedBy, DateTime? expiresUtc = null)
    {
        RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        Reason = reason ?? string.Empty;
        ApprovedBy = approvedBy ?? string.Empty;
        ExpiresUtc = expiresUtc.HasValue ? HandoverReceipt.AsUtc(expiresUtc.Value) : (DateTime?)null;
    }

    public string RuleId { get; }

    public string Reason { get; }

    public string ApprovedBy { get; }

    public DateTime? ExpiresUtc { get; }

    public bool IsActive(DateTime nowUtc) =>
        ExpiresUtc.HasValue && ExpiresUtc.Value > HandoverReceipt.AsUtc(nowUtc);
}

/// <summary>One sanitised rule row of a handover receipt.</summary>
public sealed class HandoverRuleRow
{
    public HandoverRuleRow(
        string ruleId,
        string legacyItemId,
        HandoverRuleSeverity severity,
        HandoverRuleStatus status,
        int count,
        string summary,
        HandoverWaiver? waiver = null)
    {
        RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        LegacyItemId = legacyItemId ?? string.Empty;
        Severity = severity;
        Status = status;
        Count = Math.Max(0, count);
        Summary = summary ?? string.Empty;
        Waiver = waiver;
    }

    public string RuleId { get; }

    public string LegacyItemId { get; }

    public HandoverRuleSeverity Severity { get; }

    public HandoverRuleStatus Status { get; }

    public int Count { get; }

    public string Summary { get; }

    public HandoverWaiver? Waiver { get; }
}

/// <summary>
/// The model version inside a handover receipt. Use a role-based alias or an anonymous model-N label. <see cref="DocumentVersionGuid"/> and <see cref="NumberOfSaves"/> come from Revit's
/// <c>BasicFileInfo.Extract(path).GetDocumentVersion()</c> and are what
/// <see cref="HandoverReceiptVerifier"/> rechecks without opening the .rvt.
/// </summary>
public sealed class HandoverReceiptModel
{
    public HandoverReceiptModel(string modelAlias, Guid documentVersionGuid, int numberOfSaves, string role)
    {
        ModelAlias = modelAlias ?? string.Empty;
        DocumentVersionGuid = documentVersionGuid;
        NumberOfSaves = Math.Max(0, numberOfSaves);
        Role = role ?? string.Empty;
    }

    public string ModelAlias { get; }

    public Guid DocumentVersionGuid { get; }

    public int NumberOfSaves { get; }

    /// <summary>Model role, such as <c>host</c> or <c>link</c>.</summary>
    public string Role { get; }
}

/// <summary>A privacy-clean, hash-verified handover receipt produced for a contractor exchange.</summary>
public sealed class HandoverReceipt
{
    public const string SchemaName = "kanizu.handover-receipt";
    public const int SchemaVersion = 1;

    public HandoverReceipt(
        string kitVersion,
        int standardVersion,
        string kanizuRevitVersion,
        DateTime generatedUtc,
        string mode,
        string profile,
        HandoverReceiptVerdict verdict,
        int scorePercent,
        HandoverReceiptModel model,
        IReadOnlyList<HandoverRuleRow> rules,
        IReadOnlyDictionary<string, long>? metrics = null)
    {
        KitVersion = VersionsOnly(kitVersion);
        StandardVersion = standardVersion;
        KanizuRevitVersion = VersionsOnly(kanizuRevitVersion);
        GeneratedUtc = AsUtc(generatedUtc);
        Mode = mode is "template" or "project" ? mode : "unspecified";
        Profile = profile is "contractor" or "maintainer" ? profile : "default";
        Verdict = verdict;
        ScorePercent = Math.Max(0, Math.Min(100, scorePercent));

        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        string role = model.Role is "host" or "link" ? model.Role : "model";
        string alias = AnonymousAlias.IsMatch(model.ModelAlias) ? model.ModelAlias : role;
        Model = new HandoverReceiptModel(alias, model.DocumentVersionGuid, model.NumberOfSaves, role);
        Rules = Array.AsReadOnly((rules ?? Array.Empty<HandoverRuleRow>()).Where(row => row != null).Select(SanitizeRow).ToArray());

        Dictionary<string, long> safeMetrics = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, long> entry in metrics ?? new Dictionary<string, long>())
        {
            if (entry.Key is "levelCount" or "elementCount" or "viewCount" or "linkCount"
                && entry.Value >= 0 && entry.Value <= int.MaxValue)
            {
                safeMetrics.Add(entry.Key, entry.Value);
            }
        }

        Metrics = new ReadOnlyDictionary<string, long>(safeMetrics);
    }

    public string KitVersion { get; }

    public int StandardVersion { get; }

    public string KanizuRevitVersion { get; }

    public DateTime GeneratedUtc { get; }

    public string Mode { get; }

    public string Profile { get; }

    public HandoverReceiptVerdict Verdict { get; }

    public int ScorePercent { get; }

    public HandoverReceiptModel Model { get; }

    public IReadOnlyList<HandoverRuleRow> Rules { get; }

    public IReadOnlyDictionary<string, long> Metrics { get; }

    /// <summary>Serialise the receipt to canonical UTF-8 JSON (sorted keys, LF endings, SHA-256 last).</summary>
    public string ToJson() => ToJsonInternal(computeHash: true);

    /// <summary>Serialise without the <c>sha256</c> property. Used by <see cref="HandoverReceiptVerifier"/>.</summary>
    public string ToJsonWithoutHash() => ToJsonInternal(computeHash: false);

    /// <summary>Lowercase hex SHA-256 digest of the canonical JSON excluding the <c>sha256</c> property.</summary>
    public string ComputeSha256() => HandoverReceiptHashing.Sha256Hex(ToJsonWithoutHash());

    /// <summary>Build a receipt from a <see cref="ProjectReadinessReport"/> and apply a waiver set.</summary>
    public static HandoverReceipt FromReadinessReport(
        ProjectReadinessReport report,
        string kitVersion,
        string kanizuRevitVersion,
        string profile,
        HandoverReceiptModel model,
        DateTime generatedUtc,
        IReadOnlyList<HandoverWaiver>? waivers = null,
        int? standardVersionOverride = null,
        IReadOnlyDictionary<string, long>? metrics = null)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        Dictionary<string, HandoverWaiver> waiverIndex = new(StringComparer.Ordinal);
        foreach (HandoverWaiver waiver in waivers ?? Array.Empty<HandoverWaiver>())
        {
            if (waiver == null || !RuleCatalog.TryLookup(waiver.RuleId, out _)
                || string.IsNullOrWhiteSpace(waiver.ApprovedBy) || string.IsNullOrWhiteSpace(waiver.Reason))
            {
                continue;
            }

            if (!waiver.IsActive(generatedUtc))
            {
                continue;
            }

            if (!waiverIndex.ContainsKey(waiver.RuleId))
            {
                waiverIndex[waiver.RuleId] = waiver;
            }
        }

        List<HandoverRuleRow> rows = new();
        bool anyFailRemains = !report.HasRunMandatoryChecks;
        bool anyWaivedFail = false;
        foreach (ReadinessCheckItem item in report.Groups.SelectMany(g => g.Items))
        {
            HandoverRuleSeverity severity = ResolveSeverity(item.RuleId);
            HandoverRuleStatus status = MapStatus(item.Status);
            int count = Math.Max(item.ElementIds?.Count ?? 0, status == HandoverRuleStatus.Fail || status == HandoverRuleStatus.Warn ? 1 : 0);
            HandoverWaiver? waiver = null;
            if (status is HandoverRuleStatus.Fail or HandoverRuleStatus.Warn)
            {
                if (waiverIndex.TryGetValue(item.RuleId, out HandoverWaiver? foundWaiver) && foundWaiver != null)
                {
                    waiver = foundWaiver;
                    anyWaivedFail = true;
                }
                else
                {
                    anyFailRemains = true;
                }
            }

            rows.Add(new HandoverRuleRow(
                item.RuleId,
                item.Id,
                severity,
                status,
                count,
                string.Empty,
                waiver));
        }

        HandoverReceiptVerdict verdict = anyFailRemains
            ? HandoverReceiptVerdict.Fail
            : anyWaivedFail
                ? HandoverReceiptVerdict.PassWithWaivers
                : HandoverReceiptVerdict.Pass;

        string modeText = report.Mode == ReadinessMode.Template ? "template" : "project";
        int standardVersion = standardVersionOverride ?? GisStandard.Version;

        return new HandoverReceipt(
            kitVersion,
            standardVersion,
            kanizuRevitVersion,
            generatedUtc,
            modeText,
            profile,
            verdict,
            report.ScorePercent,
            model,
            rows,
            metrics);
    }

    // Receipt fields accept a small public vocabulary. Model data and waiver notes can contain
    // arbitrary names or paths, so public summaries come only from the rule catalog.
    private static readonly Regex AnonymousAlias = new(@"\Amodel-[1-9][0-9]{0,3}\z", RegexOptions.CultureInvariant);
    private static readonly Regex VersionText = new(@"\A[0-9]{1,5}(?:\.[0-9]{1,5}){0,3}\z", RegexOptions.CultureInvariant);

    private static string VersionsOnly(string? value) =>
        value != null && VersionText.IsMatch(value) ? value : string.Empty;

    internal static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();

    private static HandoverRuleRow SanitizeRow(HandoverRuleRow row)
    {
        RuleCatalogEntry rule = RuleCatalog.Lookup(row.RuleId);
        string alias = rule.LegacyItemIds.Contains(row.LegacyItemId, StringComparer.Ordinal)
            ? row.LegacyItemId
            : string.Empty;
        HandoverWaiver? waiver = row.Waiver != null && row.Waiver.RuleId == rule.RuleId
            ? new HandoverWaiver(rule.RuleId, "External waiver on file.", "Maintainer", row.Waiver.ExpiresUtc)
            : null;
        return new HandoverRuleRow(rule.RuleId, alias, row.Severity, row.Status, row.Count, rule.Title, waiver);
    }

    private static HandoverRuleSeverity ResolveSeverity(string ruleId)
    {
        if (RuleCatalog.TryLookup(ruleId, out RuleCatalogEntry entry))
        {
            return entry.Severity switch
            {
                RuleSeverity.Error => HandoverRuleSeverity.Error,
                RuleSeverity.Warning => HandoverRuleSeverity.Warning,
                _ => HandoverRuleSeverity.Info,
            };
        }

        return HandoverRuleSeverity.Warning;
    }

    private static HandoverRuleStatus MapStatus(ReadinessCheckStatus status) => status switch
    {
        ReadinessCheckStatus.Pass => HandoverRuleStatus.Pass,
        ReadinessCheckStatus.Warn => HandoverRuleStatus.Warn,
        ReadinessCheckStatus.Fail => HandoverRuleStatus.Fail,
        _ => HandoverRuleStatus.NotApplicable,
    };

    private string ToJsonInternal(bool computeHash)
    {
        JObject root = new()
        {
            ["schema"] = SchemaName,
            ["version"] = SchemaVersion,
            ["kitVersion"] = KitVersion,
            ["standardVersion"] = StandardVersion,
            ["kanizuRevitVersion"] = KanizuRevitVersion,
            ["generatedUtc"] = GeneratedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            ["mode"] = Mode,
            ["profile"] = Profile,
            ["verdict"] = VerdictText(Verdict),
            ["scorePercent"] = ScorePercent,
            ["model"] = new JObject
            {
                ["modelAlias"] = Model.ModelAlias,
                ["documentVersionGuid"] = Model.DocumentVersionGuid.ToString("D"),
                ["numberOfSaves"] = Model.NumberOfSaves,
                ["role"] = Model.Role,
            },
            ["rules"] = new JArray(Rules.Select(RuleToJson)),
            ["metrics"] = MetricsToJson(),
        };

        JObject sorted = (JObject)CanonicalJson.SortKeys(root);
        if (computeHash)
        {
            sorted["sha256"] = HandoverReceiptHashing.Sha256Hex(sorted.ToString(Formatting.None));
        }

        return sorted.ToString(Formatting.None);
    }

    private JObject MetricsToJson()
    {
        JObject metrics = new();
        foreach (KeyValuePair<string, long> entry in Metrics.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            metrics[entry.Key] = entry.Value;
        }

        return metrics;
    }

    private static JObject RuleToJson(HandoverRuleRow row)
    {
        JObject rule = new()
        {
            ["ruleId"] = row.RuleId,
            ["legacyItemId"] = row.LegacyItemId,
            ["severity"] = SeverityText(row.Severity),
            ["status"] = StatusText(row.Status),
            ["count"] = row.Count,
            ["summary"] = row.Summary,
        };

        if (row.Waiver != null)
        {
            JObject waiver = new()
            {
                ["ruleId"] = row.Waiver.RuleId,
                ["reason"] = row.Waiver.Reason,
                ["approvedBy"] = row.Waiver.ApprovedBy,
            };
            if (row.Waiver.ExpiresUtc.HasValue)
            {
                waiver["expiresUtc"] = row.Waiver.ExpiresUtc.Value.ToUniversalTime()
                    .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            }

            rule["waiver"] = waiver;
        }

        return rule;
    }

    internal static string VerdictText(HandoverReceiptVerdict verdict) => verdict switch
    {
        HandoverReceiptVerdict.Pass => "PASS",
        HandoverReceiptVerdict.PassWithWaivers => "PASS_WITH_WAIVERS",
        _ => "FAIL",
    };

    internal static string SeverityText(HandoverRuleSeverity severity) => severity switch
    {
        HandoverRuleSeverity.Error => "error",
        HandoverRuleSeverity.Warning => "warning",
        _ => "info",
    };

    internal static string StatusText(HandoverRuleStatus status) => status switch
    {
        HandoverRuleStatus.Pass => "pass",
        HandoverRuleStatus.Warn => "warn",
        HandoverRuleStatus.Fail => "fail",
        _ => "not_applicable",
    };
}

/// <summary>Deterministic JSON writer used so <see cref="HandoverReceipt"/> hashes reproducibly.</summary>
internal static class CanonicalJson
{
    public static string Serialize(JToken token) => SortKeys(token).ToString(Formatting.None);

    public static JToken SortKeys(JToken token)
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

internal static class HandoverReceiptHashing
{
    public static string Sha256Hex(string input)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input ?? string.Empty);
        using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(bytes);
            System.Text.StringBuilder builder = new(hash.Length * 2);
            for (int index = 0; index < hash.Length; index++)
            {
                builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
