using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IgezziGuard;

internal sealed class SupportPanel : UserControl
{
    private const string Repository = "https://github.com/hankitools/hankitools-windows";
    internal static string AppDetails =>
        $"Hanki Tools: {AppInfo.Version}\r\nWindows: {Environment.OSVersion.Version}\r\nOS architecture: {RuntimeInformation.OSArchitecture}\r\nApp architecture: {RuntimeInformation.ProcessArchitecture}\r\nRuntime: {RuntimeInformation.FrameworkDescription}";

    public SupportPanel()
    {
        Dock = DockStyle.Fill; AutoScroll = true; Padding = new Padding(16);
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(4) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Section(string title, string description, params (string Label, Action Action)[] actions)
        {
            var card = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(20), Margin = new Padding(0, 0, 0, 16), Tag = "card" };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var heading = new Label { Text = title, AutoSize = true, Dock = DockStyle.Top, Font = new Font("Segoe UI", 14, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10) };
            var body = new Label { Text = description, AutoSize = true, Dock = DockStyle.Top, Tag = "intro", Margin = new Padding(0, 0, 0, 14) };
            card.SizeChanged += (_, _) => { body.MaximumSize = new Size(Math.Max(100, card.ClientSize.Width - card.Padding.Horizontal), 0); heading.MaximumSize = body.MaximumSize; };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
            foreach (var action in actions) {
                var button = new HankiButton { Text = action.Label, AutoSize = true, Appearance = HankiButtonStyle.Quiet };
                button.Click += (_, _) => action.Action(); bar.Controls.Add(button);
            }
            card.Controls.Add(heading); card.Controls.Add(body); card.Controls.Add(bar); layout.Controls.Add(card);
        }
        Section("Meet the Hanki community", "Ask a question, share feedback or follow the project. Links open in your browser; no report is attached.",
            ("Open hanki.tools ↗", () => OpenLink("https://hanki.tools/")),
            ("Join Discord ↗", () => OpenLink("https://discord.gg/qprzjtTaQ")),
            ("Read the user guide ↗", () => OpenLink(Repository + "/blob/main/README-PORTABLE.md")));
        Section("Something not working?", "Prepare a useful report with steps to reproduce the problem. Review and copy the draft, then paste it into a GitHub issue. Avoid passwords, API keys and personal logs.",
            ("Prepare bug report", PrepareReport),
            ("Open GitHub issues ↗", () => OpenLink(Repository + "/issues")));
        Section("Your version", AppDetails + "\r\n\r\nUpdates are manual. Check release notes and package instructions before replacing your app. Keep recovery data until supported changes are undone.",
            ("Copy app details", () => Copy(AppDetails)),
            ("View releases ↗", () => OpenLink(Repository + "/releases")),
            ("Read release notes ↗", () => OpenLink(Repository + "/blob/main/RELEASE-NOTES.md")));
        Section("Privacy & project information", "Hanki Tools is distributed under the MIT license. The experimental scanner is not a replacement antivirus. Discord and GitHub issues may be public: share only information you have reviewed.",
            ("Privacy information ↗", () => OpenLink(Repository + "/blob/main/PRIVACY.md")),
            ("Source & license ↗", () => OpenLink(Repository)));
        Controls.Add(layout);
    }

    private void OpenLink(string url)
    {
        try { using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open browser"); }
    }
    private void Copy(string text)
    {
        try { Clipboard.SetText(text); MessageBox.Show(this, "Copied. Review before sharing.", "Hanki Tools"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not copy text"); }
    }
    private void PrepareReport()
    {
        using var dialog = new Form { Text = "Prepare a bug report", Size = new Size(820, 640), MinimumSize = new Size(540, 400), StartPosition = FormStartPosition.CenterParent, Font = Font, Padding = new Padding(16) };
        var report = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Bug report draft",
            Text = "## What happened?\r\n[Describe the problem]\r\n\r\n## Steps to reproduce\r\n1. \r\n2. \r\n3. \r\n\r\n## Expected result\r\n\r\n## Actual result / error message\r\n\r\n## App details\r\n" + AppDetails + "\r\n\r\n## Additional context\r\n[Module, display scaling, and whether it happens repeatedly. Remove private information before sharing.]" };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 12, 0, 0) };
        var copy = new HankiButton { Text = "Copy reviewed draft", AutoSize = true, Primary = true };
        var open = new HankiButton { Text = "Open new GitHub issue ↗", AutoSize = true };
        var close = new HankiButton { Text = "Close", AutoSize = true, Appearance = HankiButtonStyle.Quiet, DialogResult = DialogResult.Cancel };
        copy.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(report.Text)) Copy(report.Text); };
        open.Click += (_, _) => OpenLink(Repository + "/issues/new");
        bar.Controls.AddRange([copy, open, close]); dialog.Controls.Add(report); dialog.Controls.Add(bar); dialog.CancelButton = close;
        HankiTheme.Apply(dialog); dialog.ShowDialog(this);
    }
}
