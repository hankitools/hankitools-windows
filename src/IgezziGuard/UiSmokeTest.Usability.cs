namespace IgezziGuard;

internal static partial class UiSmokeTest
{
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void CheckUsability(HankiForm form)
    {
        void Require(bool condition, string message) { if (!condition) throw new IOException("Usability: " + message); }
        void Open(string route) { form.Routes.Single(r => r.Name == route).Open(); form.PerformLayout(); Application.DoEvents(); }
        void Click(Control parent, string text) {
            var button = Descendants(parent).OfType<HankiButton>().Single(b => b.Text == Localizer.T(text) || b.AccessibleName == Localizer.T(text));
            Require(button.Wanted && button.Enabled, "Action unavailable: " + text);
            button.Invoke(); form.PerformLayout(); Application.DoEvents();
        }
        Open("Fix My PC");
        var scan = Find<FullScanPanel>(form)!;
        Require(!Descendants(scan).OfType<HankiButton>().Single(b => b.Text == Localizer.T("Review repairs")).Wanted, "Repairs must not appear before a scan.");
        var now = DateTimeOffset.UtcNow;
        DiagnosticResult Result(string id, string title, CollectionOutcome outcome, FindingSeverity severity) => new("storage", id, DiagnosticCategory.Storage, outcome, severity,
            title, "Example data for the UI check. Nothing was read or changed.", now, now, coverage: "Example coverage");
        scan.Preview(new(Guid.NewGuid(), now, now, 3, 3, false, [
            Result("capacity", "Example — low free space", CollectionOutcome.Completed, FindingSeverity.Warning),
            Result("healthy", "Example healthy check", CollectionOutcome.Completed, FindingSeverity.Healthy),
            Result("missing", "Example unavailable check", CollectionOutcome.Unavailable, FindingSeverity.Unknown)
        ]));
        Require(!Descendants(scan).OfType<Label>().Any(l => l.Text == "Example healthy check"), "Healthy findings must be collapsed initially.");
        Click(scan, "Show other checks");
        Require(Descendants(scan).OfType<Label>().Any(l => l.Text == "Example healthy check"), "Expanding other checks must reveal healthy findings.");
        Click(scan, "Hide other checks");
        Click(scan, "Review incomplete checks");
        Require(Descendants(scan).OfType<Label>().Any(l => l.Text == "Example unavailable check"), "Missing evidence must be available separately.");
        Click(scan, "Hide incomplete checks");
        Click(scan, "See next step");
        Require(Descendants(scan).OfType<Label>().Any(l => l.Text == Localizer.T("Try this next")), "A finding must lead with manual guidance.");
        Click(scan, "Back to scan results");

        Open("Connect  /  Guided troubleshooting");
        var internet = Find<InternetTroubleshootingPanel>(form)!;
        var sample = new ConnectionCheck(new(1, "Example Wi-Fi", true, true, false, false, null, true, 20, null, null), "Example report. No probes were run.");
        internet.Preview(sample);
        Click(internet, "More help");
        Click(internet, "Open next check");
        Click(form, "Return to troubleshooting");
        Require(internet.Visible, "Back must restore the exact guided tab.");
        Require(Descendants(internet).Any(c => c.Text == Localizer.T("A server answered, but website name lookups failed")), "Returning must retain the previous finding.");
        Require(Descendants(internet).OfType<HankiButton>().Any(b => b.Wanted && b.Text == Localizer.T("Hide extra help")), "Returning must retain the journey's expanded help state.");
        Click(internet, "It works now");
        Require(!Descendants(internet).OfType<HankiButton>().Single(b => b.Text == Localizer.T("It works now")).Wanted, "Resolution must be an explicit terminal state.");
        internet.Preview(sample);
        Note("usability: summary, coverage, next step, contextual actions and Back passed with fixed fixtures");
    }
}
