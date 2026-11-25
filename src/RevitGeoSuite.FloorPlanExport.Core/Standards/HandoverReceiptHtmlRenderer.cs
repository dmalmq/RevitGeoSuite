using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>
/// Renders a <see cref="HandoverReceipt"/> as a self-contained, bilingual EN/JA, print-friendly HTML
/// page. No external resources — all CSS is inline, there are no external fonts, CDNs, scripts, or
/// URLs. All textual content is HTML-escaped against XSS.
/// </summary>
public static class HandoverReceiptHtmlRenderer
{
    public static string Render(HandoverReceipt receipt)
    {
        if (receipt is null)
        {
            throw new ArgumentNullException(nameof(receipt));
        }

        string sha = receipt.ComputeSha256();
        List<HandoverRuleRow> waiverRows = receipt.Rules
            .Where(row => row.Waiver != null)
            .ToList();

        StringBuilder html = new();
        html.Append("<!doctype html>\n");
        html.Append("<html lang=\"en\">\n<head>\n");
        html.Append("<meta charset=\"utf-8\">\n");
        html.Append("<title>Kanizu Handover Receipt / Kanizu 引渡しレシート</title>\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n");
        html.Append("<style>\n").Append(Css).Append("\n</style>\n");
        html.Append("</head>\n<body>\n");

        html.Append("<main class=\"receipt\">\n");

        AppendHeader(html, receipt);
        AppendBanner(html, receipt);
        AppendKpiStrip(html, receipt);
        AppendModelBlock(html, receipt);
        AppendRulesTable(html, receipt);
        AppendWaiversTable(html, waiverRows);
        AppendFooter(html, sha);

        html.Append("</main>\n");
        html.Append("</body>\n</html>\n");
        return html.ToString();
    }

    private const string Css = "" +
        ":root{color-scheme:light;--ink:#1f2937;--muted:#64748b;--card:#f8fafc;--border:#e2e8f0;--pass:#15803d;--warn:#b45309;--fail:#b91c1c;--accent:#0f172a;}" +
        "*{box-sizing:border-box;}" +
        "body{margin:0;font-family:Arial,'Helvetica Neue',Helvetica,sans-serif;color:var(--ink);background:#ffffff;line-height:1.5;}" +
        ".receipt{max-width:960px;margin:0 auto;padding:32px 24px;}" +
        "h1{font-size:24px;margin:0 0 4px 0;}" +
        "h2{font-size:16px;margin:28px 0 8px 0;color:var(--accent);}" +
        ".ja{color:var(--muted);font-weight:normal;}" +
        ".banner{border-radius:8px;padding:14px 18px;font-weight:600;margin:12px 0 20px 0;border:1px solid var(--border);}" +
        ".banner.pass{background:#ecfdf5;border-color:#bbf7d0;color:var(--pass);}" +
        ".banner.waiver{background:#fefce8;border-color:#fde68a;color:var(--warn);}" +
        ".banner.fail{background:#fef2f2;border-color:#fecaca;color:var(--fail);}" +
        ".kpi{display:grid;grid-template-columns:repeat(auto-fit,minmax(130px,1fr));gap:10px;margin:12px 0 18px 0;}" +
        ".kpi .tile{background:var(--card);border:1px solid var(--border);border-radius:8px;padding:10px 12px;}" +
        ".kpi .label{font-size:12px;color:var(--muted);}" +
        ".kpi .value{font-size:18px;font-weight:600;}" +
        "table{width:100%;border-collapse:collapse;margin-top:8px;font-size:13px;}" +
        "th,td{border:1px solid var(--border);padding:6px 8px;text-align:left;vertical-align:top;}" +
        "th{background:var(--card);font-weight:600;}" +
        ".status-pass{color:var(--pass);font-weight:600;}" +
        ".status-warn{color:var(--warn);font-weight:600;}" +
        ".status-fail{color:var(--fail);font-weight:600;}" +
        ".status-na{color:var(--muted);}" +
        ".severity{text-transform:lowercase;color:var(--muted);}" +
        ".footer{margin-top:28px;border-top:1px solid var(--border);padding-top:12px;font-size:11px;color:var(--muted);word-break:break-all;}" +
        ".model{display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:10px;margin-top:6px;}" +
        ".model .tile{background:var(--card);border:1px solid var(--border);border-radius:8px;padding:10px 12px;}" +
        ".model .label{font-size:12px;color:var(--muted);}" +
        ".model .value{font-weight:600;}" +
        "@media print{body{background:#ffffff;}.receipt{max-width:none;padding:12mm;}}";

    private static void AppendHeader(StringBuilder html, HandoverReceipt receipt)
    {
        html.Append("<h1>Kanizu Handover Receipt <span class=\"ja\" lang=\"ja\">／ Kanizu 引渡しレシート</span></h1>\n");
        html.Append("<div class=\"kpi\">\n");
        AppendTile(html, "Generated (UTC)", "生成時刻 (UTC)", Html(receipt.GeneratedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        AppendTile(html, "Mode", "モード", Html(receipt.Mode));
        AppendTile(html, "Profile", "プロファイル", Html(receipt.Profile));
        AppendTile(html, "Kit version", "キットバージョン", Html(receipt.KitVersion));
        AppendTile(html, "Standard version", "標準バージョン", Html(receipt.StandardVersion.ToString(CultureInfo.InvariantCulture)));
        AppendTile(html, "Kanizu Revit", "Kanizu Revit", Html(receipt.KanizuRevitVersion));
        html.Append("</div>\n");
    }

    private static void AppendBanner(StringBuilder html, HandoverReceipt receipt)
    {
        (string css, string en, string ja) = receipt.Verdict switch
        {
            HandoverReceiptVerdict.Pass => ("pass", "PASS", "合格"),
            HandoverReceiptVerdict.PassWithWaivers => ("waiver", "PASS WITH WAIVERS", "条件付き合格"),
            _ => ("fail", "FAIL", "不合格"),
        };
        html.Append("<div class=\"banner ").Append(css).Append("\">")
            .Append(Html(en))
            .Append(" <span class=\"ja\" lang=\"ja\">／ ")
            .Append(Html(ja))
            .Append("</span></div>\n");
    }

    private static void AppendKpiStrip(StringBuilder html, HandoverReceipt receipt)
    {
        int passCount = receipt.Rules.Count(r => r.Status == HandoverRuleStatus.Pass);
        int warnCount = receipt.Rules.Count(r => r.Status == HandoverRuleStatus.Warn);
        int failCount = receipt.Rules.Count(r => r.Status == HandoverRuleStatus.Fail);
        int naCount = receipt.Rules.Count(r => r.Status == HandoverRuleStatus.NotApplicable);
        int waiverCount = receipt.Rules.Count(r => r.Waiver != null);

        html.Append("<h2>Summary <span class=\"ja\" lang=\"ja\">／ 概要</span></h2>\n");
        html.Append("<div class=\"kpi\">\n");
        AppendTile(html, "Score", "スコア", Html(receipt.ScorePercent.ToString(CultureInfo.InvariantCulture) + "%"));
        AppendTile(html, "Pass", "合格", Html(passCount.ToString(CultureInfo.InvariantCulture)));
        AppendTile(html, "Warn", "警告", Html(warnCount.ToString(CultureInfo.InvariantCulture)));
        AppendTile(html, "Fail", "不合格", Html(failCount.ToString(CultureInfo.InvariantCulture)));
        AppendTile(html, "N/A", "対象外", Html(naCount.ToString(CultureInfo.InvariantCulture)));
        AppendTile(html, "Waivers", "免除", Html(waiverCount.ToString(CultureInfo.InvariantCulture)));
        html.Append("</div>\n");
    }

    private static void AppendModelBlock(StringBuilder html, HandoverReceipt receipt)
    {
        html.Append("<h2>Model <span class=\"ja\" lang=\"ja\">／ モデル</span></h2>\n");
        html.Append("<div class=\"model\">\n");
        AppendTile(html, "Alias", "別名", Html(receipt.Model.ModelAlias));
        AppendTile(html, "Role", "役割", Html(receipt.Model.Role));
        AppendTile(html, "Document version GUID", "ドキュメント版 GUID", Html(receipt.Model.DocumentVersionGuid.ToString("D")));
        AppendTile(html, "Number of saves", "保存回数", Html(receipt.Model.NumberOfSaves.ToString(CultureInfo.InvariantCulture)));
        html.Append("</div>\n");
    }

    private static void AppendRulesTable(StringBuilder html, HandoverReceipt receipt)
    {
        html.Append("<h2>Rules <span class=\"ja\" lang=\"ja\">／ ルール</span></h2>\n");
        html.Append("<table><thead><tr>")
            .Append("<th>Rule / ルール</th>")
            .Append("<th>Severity / 重要度</th>")
            .Append("<th>Status / ステータス</th>")
            .Append("<th>Count / 件数</th>")
            .Append("<th>Summary / 概要</th>")
            .Append("</tr></thead><tbody>\n");

        foreach (HandoverRuleRow row in receipt.Rules)
        {
            html.Append("<tr>")
                .Append("<td>").Append(Html(row.RuleId));
            if (!string.IsNullOrEmpty(row.LegacyItemId) && !string.Equals(row.LegacyItemId, row.RuleId, StringComparison.Ordinal))
            {
                html.Append(" <span class=\"ja\" lang=\"ja\">(").Append(Html(row.LegacyItemId)).Append(")</span>");
            }

            html.Append("</td>")
                .Append("<td><span class=\"severity\">").Append(Html(SeverityLabel(row.Severity))).Append("</span></td>")
                .Append("<td><span class=\"").Append(StatusCss(row.Status)).Append("\">").Append(Html(StatusLabel(row.Status))).Append("</span></td>")
                .Append("<td>").Append(Html(row.Count.ToString(CultureInfo.InvariantCulture))).Append("</td>")
                .Append("<td>").Append(Html(row.Summary)).Append("</td>")
                .Append("</tr>\n");
        }

        html.Append("</tbody></table>\n");
    }

    private static void AppendWaiversTable(StringBuilder html, List<HandoverRuleRow> waiverRows)
    {
        html.Append("<h2>Waivers <span class=\"ja\" lang=\"ja\">／ 免除</span></h2>\n");
        if (waiverRows.Count == 0)
        {
            html.Append("<p class=\"ja\" lang=\"ja\">No waivers attached. ／ 免除は適用されていません。</p>\n");
            return;
        }

        html.Append("<table><thead><tr>")
            .Append("<th>Rule / ルール</th>")
            .Append("<th>Approved by / 承認者</th>")
            .Append("<th>Expires (UTC) / 有効期限 (UTC)</th>")
            .Append("<th>Reason / 理由</th>")
            .Append("</tr></thead><tbody>\n");

        foreach (HandoverRuleRow row in waiverRows)
        {
            HandoverWaiver waiver = row.Waiver!;
            string expiry = waiver.ExpiresUtc.HasValue
                ? waiver.ExpiresUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : "—";
            html.Append("<tr>")
                .Append("<td>").Append(Html(row.RuleId)).Append("</td>")
                .Append("<td>").Append(Html(waiver.ApprovedBy)).Append("</td>")
                .Append("<td>").Append(Html(expiry)).Append("</td>")
                .Append("<td>").Append(Html(waiver.Reason)).Append("</td>")
                .Append("</tr>\n");
        }

        html.Append("</tbody></table>\n");
    }

    private static void AppendFooter(StringBuilder html, string sha)
    {
        html.Append("<div class=\"footer\">SHA-256: <code>").Append(Html(sha)).Append("</code></div>\n");
    }

    private static void AppendTile(StringBuilder html, string labelEn, string labelJa, string value)
    {
        html.Append("<div class=\"tile\"><div class=\"label\">")
            .Append(Html(labelEn))
            .Append(" <span class=\"ja\" lang=\"ja\">／ ").Append(Html(labelJa)).Append("</span></div>")
            .Append("<div class=\"value\">").Append(value).Append("</div></div>\n");
    }

    private static string SeverityLabel(HandoverRuleSeverity severity) => severity switch
    {
        HandoverRuleSeverity.Error => "Error / エラー",
        HandoverRuleSeverity.Warning => "Warning / 警告",
        _ => "Info / 情報",
    };

    private static string StatusLabel(HandoverRuleStatus status) => status switch
    {
        HandoverRuleStatus.Pass => "Pass / 合格",
        HandoverRuleStatus.Warn => "Warn / 警告",
        HandoverRuleStatus.Fail => "Fail / 不合格",
        _ => "N/A / 対象外",
    };

    private static string StatusCss(HandoverRuleStatus status) => status switch
    {
        HandoverRuleStatus.Pass => "status-pass",
        HandoverRuleStatus.Warn => "status-warn",
        HandoverRuleStatus.Fail => "status-fail",
        _ => "status-na",
    };

    public static string EscapeHtml(string input) => Html(input);

    internal static string Html(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        StringBuilder builder = new(input.Length + 8);
        foreach (char ch in input)
        {
            switch (ch)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"':
                    builder.Append("&quot;");
                    break;
                case '\'':
                    builder.Append("&#39;");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }
}
