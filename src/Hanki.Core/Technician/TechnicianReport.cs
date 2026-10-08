using System.Globalization;
using System.Net;
using System.Text;
namespace IgezziGuard;

/// <summary>
/// Customer-facing PC check report: one self-contained HTML file in plain language that prints cleanly
/// (browser Print → Save as PDF). Built only from the scan and repair records; nothing is uploaded.
/// </summary>
public static class TechnicianReport
{
    // Saved history keeps titles and statuses but replaces explanations with this placeholder.
    internal const string MinimizedPrefix = "Local result retained without raw evidence";
    internal enum Group { Attention, Review, Info, Good, NotChecked, Fixed }

    internal static Group Classify(DiagnosticResult r) =>
        r.Outcome is CollectionOutcome.Failed or CollectionOutcome.Unavailable or CollectionOutcome.Cancelled ? Group.NotChecked : r.Severity switch {
            FindingSeverity.Critical => Group.Attention, FindingSeverity.Warning => Group.Review, FindingSeverity.Healthy => Group.Good,
            FindingSeverity.Informational => Group.Info, _ => Group.NotChecked };
    private static string Explain(DiagnosticResult r) => r.Explanation.StartsWith(MinimizedPrefix, StringComparison.Ordinal) ? "" : r.Explanation;
    private static string NextStep(DiagnosticResult r, Group group) =>
        group is Group.Attention or Group.Review ? FindingAnalysis.Recommend(r)?.ManualAction ?? "" : "";

    /// <summary>"PC check JOB-1042 2026-09-23.html", without characters Windows doesn't allow in file names.</summary>
    public static string FileName(string jobLabel, DateTimeOffset checkedAt)
    {
        var job = jobLabel == "Unlabelled" ? "" : new string(jobLabel.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim().TrimEnd('.');
        return $"PC check {(job.Length > 0 ? job + " " : "")}{checkedAt.ToLocalTime():yyyy-MM-dd}.html";
    }

    public static string Html(TechnicianSession session, TechnicianBusiness business, bool includeTechnical, string appVersion, DateTimeOffset generated)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var culture = CultureInfo.CurrentCulture;
        var results = session.Scan.Results;
        bool limited = results.Any(r => r.Explanation.StartsWith(MinimizedPrefix, StringComparison.Ordinal));
        var attempts = session.Repairs?.Attempts ?? [];
        // A finding whose repair passed its check afterwards is shown with that repair, not as an open issue.
        var fixedActions = attempts.Where(a => a.State == RepairState.Executed && a.Verification == VerificationState.Fixed).Select(a => a.ActionId).ToHashSet(StringComparer.Ordinal);
        Group Place(DiagnosticResult r) => Classify(r) is Group.Attention or Group.Review && FindingAnalysis.Recommend(r)?.RepairActionId is { } id && fixedActions.Contains(id) ? Group.Fixed : Classify(r);
        // Identical observations (same title, status and wording) are listed once, with a count.
        var items = results.Select(r => (Result: r, Group: Place(r)))
            .GroupBy(x => (x.Group, x.Result.Title, Text: Explain(x.Result), Next: NextStep(x.Result, x.Group)))
            .Select(g => (g.Key.Group, g.Key.Title, g.Key.Text, g.Key.Next, Count: g.Count()))
            .ToList();
        int Count(Group g) => items.Count(i => i.Group == g);
        int attention = Count(Group.Attention), review = Count(Group.Review);
        string Plural(int n, string one, string many) => n == 1 ? one : many;
        var (tone, headline) = attention > 0 ? ("attention", $"{attention} {Plural(attention, "thing needs", "things need")} attention")
            : review > 0 ? ("review", $"{review} {Plural(review, "thing is", "things are")} worth reviewing")
            : ("good", "Nothing needs attention");

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.Append("<title>PC check report").Append(session.CustomerLabel == "Unlabelled" ? "" : " – " + E(session.CustomerLabel)).Append("</title><style>").Append(Style).Append("</style></head><body>");
        html.Append("<p class=\"hint noprint\">To save as PDF, press Ctrl+P and choose <b>Save as PDF</b>.</p><main>");
        html.Append("<header><div><h1>").Append(E(business.DisplayName == "Unlabelled" ? "PC check report" : business.DisplayName)).Append("</h1>");
        if (business.Contact != "Unlabelled") html.Append("<p class=\"contact\">").Append(E(business.Contact)).Append("</p>");
        html.Append("</div><dl class=\"meta\"><dt>Report</dt><dd>PC check</dd>");
        if (session.CustomerLabel != "Unlabelled") html.Append("<dt>Job</dt><dd>").Append(E(session.CustomerLabel)).Append("</dd>");
        if (session.DeviceLabel != "Unlabelled") html.Append("<dt>Device</dt><dd>").Append(E(session.DeviceLabel)).Append("</dd>");
        html.Append("<dt>Checked</dt><dd>").Append(E(session.Scan.Ended.ToLocalTime().ToString("g", culture))).Append("</dd></dl></header>");

        html.Append("<section class=\"summary ").Append(tone).Append("\"><h2>").Append(E(headline)).Append("</h2><p>")
            .Append(E($"We checked {results.Where(r => Classify(r) != Group.NotChecked).Select(r => r.ModuleId).Distinct().Count()} areas of this PC with read-only checks."))
            .Append("</p><ul class=\"counts\">");
        foreach (var (group, label) in new[] { (Group.Attention, "need attention"), (Group.Review, "worth reviewing"), (Group.Fixed, "fixed"), (Group.Good, "look OK"), (Group.Info, "for information"), (Group.NotChecked, "not checked") })
            if (Count(group) > 0) html.Append("<li class=\"").Append(group.ToString().ToLowerInvariant()).Append("\">").Append(Count(group)).Append(' ').Append(label).Append("</li>");
        html.Append("</ul></section>");

        void Section(Group group, string title, string css)
        {
            var list = items.Where(i => i.Group == group).ToList();
            if (list.Count == 0) return;
            html.Append("<section><h3>").Append(title).Append("</h3>");
            foreach (var i in list) {
                html.Append("<div class=\"item ").Append(css).Append("\"><b>").Append(E(i.Title));
                if (i.Count > 1) html.Append(" <span class=\"times\">×").Append(i.Count).Append("</span>");
                html.Append("</b>");
                if (i.Text.Length > 0) html.Append("<p>").Append(E(i.Text)).Append("</p>");
                if (i.Next.Length > 0) html.Append("<p class=\"next\"><span>Next step:</span> ").Append(E(i.Next)).Append("</p>");
                html.Append("</div>");
            }
            html.Append("</section>");
        }
        Section(Group.Attention, "Needs attention", "attention");
        Section(Group.Review, "Worth reviewing", "review");

        if (attempts.Count > 0) {
            html.Append("<section><h3>Repairs</h3>");
            foreach (var a in attempts) {
                string css = a.State == RepairState.Executed && a.Verification is VerificationState.Fixed or VerificationState.Improved or VerificationState.RequiresRestart ? "done"
                    : a.State == RepairState.Failed || a.Verification == VerificationState.Worse ? "attention" : "";
                html.Append("<div class=\"item ").Append(css).Append("\"><b>").Append(E(RepairGuidance.Name(a.ActionId))).Append("</b>");
                foreach (var found in results.Where(r => Place(r) == Group.Fixed && FindingAnalysis.Recommend(r)?.RepairActionId == a.ActionId).Select(r => Explain(r) is { Length: > 0 } text ? text : r.Title).Distinct())
                    html.Append("<p><span class=\"when\">Found:</span> ").Append(E(found)).Append("</p>");
                html.Append("<p>").Append(E(RepairGuidance.Outcome(a)))
                    .Append(" <span class=\"when\">").Append(E(a.Ended.ToLocalTime().ToString("g", culture))).Append("</span></p></div>");
            }
            html.Append("</section>");
        }

        Section(Group.Info, "For information", "info");
        var good = items.Where(i => i.Group == Group.Good).Select(i => i.Title).Distinct().ToList();
        if (good.Count > 0) html.Append("<section><h3>Looks OK</h3><ul class=\"ok\">").Append(string.Concat(good.Select(t => "<li>" + E(t) + "</li>"))).Append("</ul></section>");
        var notChecked = items.Where(i => i.Group == Group.NotChecked).ToList();
        if (notChecked.Count > 0) {
            html.Append("<section><h3>Not checked</h3><ul class=\"plain\">");
            foreach (var i in notChecked) html.Append("<li>").Append(E(i.Title)).Append(i.Text.Length > 0 ? " <span class=\"why\">– " + E(i.Text) + "</span>" : "").Append("</li>");
            html.Append("</ul></section>");
        }

        if (includeTechnical) {
            html.Append("<section class=\"technical\"><h3>Technical details</h3><p class=\"why\">Identifiers such as user names, addresses and paths are masked.</p><table><tr><th>Check</th><th>Status</th><th>Evidence</th></tr>");
            foreach (var r in results)
                html.Append("<tr><td>").Append(E(r.Title)).Append("</td><td>").Append(E($"{r.Severity} · {r.Outcome}")).Append("</td><td class=\"ev\">")
                    .Append(E(string.IsNullOrWhiteSpace(r.Evidence) ? "Not kept in saved history." : DiagnosticPrivacy.Redact(r.Evidence))).Append("</td></tr>");
            html.Append("</table></section>");
        }

        html.Append("<footer><p>").Append(E($"This report describes what Windows reported when the PC was checked on {session.Scan.Ended.ToLocalTime().ToString("d", culture)}. It isn't a guarantee of the PC's overall health, and a clean result doesn't rule out problems the checks don't cover."));
        if (limited) html.Append(' ').Append(E("Some details come from saved history, which keeps only titles and results; run a new scan for full explanations."));
        html.Append("</p><p>").Append(E($"Prepared {generated.ToLocalTime().ToString("g", culture)} with Hanki Tools {appVersion}")).Append(" · <a href=\"https://hanki.tools/\">hanki.tools</a></p></footer>");
        html.Append("</main></body></html>");
        return html.ToString();
    }

    private const string Style =
        "body{margin:0;background:#f3f5f8;color:#1d2330;font:15px/1.55 'Segoe UI',system-ui,-apple-system,sans-serif}" +
        ".hint{max-width:820px;margin:18px auto 0;color:#5b6474;font-size:13px}" +
        "main{max-width:820px;margin:12px auto 40px;background:#fff;padding:40px 48px;border-radius:12px;box-shadow:0 10px 30px #1d23301a}" +
        "header{display:flex;justify-content:space-between;gap:24px;align-items:flex-start;border-bottom:2px solid #e3e7ee;padding-bottom:20px}" +
        "h1{font-size:24px;margin:0}.contact{margin:4px 0 0;color:#4b5565}" +
        ".meta{margin:0;display:grid;grid-template-columns:auto auto;gap:2px 12px;font-size:14px;text-align:left}.meta dt{color:#6b7482}.meta dd{margin:0;font-weight:600}" +
        ".summary{margin:24px 0 8px;padding:18px 20px;border-radius:10px;background:#edf7f0;border-left:5px solid #2e9e5b}" +
        ".summary.review{background:#fff7e6;border-left-color:#d99a00}.summary.attention{background:#fdeceb;border-left-color:#d64541}" +
        "h2{font-size:20px;margin:0 0 4px}.summary p{margin:0;color:#394150}" +
        ".counts{list-style:none;padding:0;margin:12px 0 0;display:flex;flex-wrap:wrap;gap:8px}.counts li{background:#fff;border:1px solid #d9dee6;border-radius:99px;padding:2px 10px;font-size:13px}" +
        ".counts .attention{border-color:#d64541}.counts .review{border-color:#d99a00}.counts .good,.counts .fixed{border-color:#2e9e5b}" +
        "h3{font-size:16px;margin:28px 0 10px}" +
        ".item{border:1px solid #e3e7ee;border-left:4px solid #9aa4b2;border-radius:8px;padding:12px 16px;margin:10px 0;break-inside:avoid}" +
        ".item.attention{border-left-color:#d64541}.item.review{border-left-color:#d99a00}.item.info{border-left-color:#3b82c4}.item.done{border-left-color:#2e9e5b}" +
        ".item p{margin:4px 0 0;color:#394150}.next span{font-weight:600;color:#1d2330}.times,.when,.why{color:#6b7482;font-weight:normal}" +
        "ul.ok{columns:2;padding-left:20px;margin:0}ul.plain{padding-left:20px;margin:0}" +
        "table{border-collapse:collapse;width:100%;font-size:12.5px}th,td{border:1px solid #e3e7ee;padding:6px 8px;text-align:left;vertical-align:top}" +
        "td.ev{font-family:Consolas,monospace;white-space:pre-wrap;word-break:break-word}" +
        "footer{margin-top:32px;border-top:1px solid #e3e7ee;padding-top:14px;color:#6b7482;font-size:13px}footer a{color:#2563a8}" +
        "@media print{body{background:#fff}main{box-shadow:none;margin:0;padding:0;max-width:none}.noprint{display:none}}" +
        "@media(max-width:640px){main{padding:24px 20px}header{flex-direction:column}ul.ok{columns:1}}";
}
