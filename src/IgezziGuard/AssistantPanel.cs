using System.Runtime.InteropServices;

namespace IgezziGuard;

public sealed class AssistantPanel : UserControl
{
    public event Action<string>? AiRequested;
    private readonly TextBox source = Area(false);
    private readonly TextBox preview = Area(true);
    private readonly TextBox question = new() { Dock = DockStyle.Fill, PlaceholderText = "What happened? What would you like explained?", MaxLength = 2000 };
    private readonly CheckBox reviewed = new() { Text = "I reviewed the full preview for private data", AutoSize = true };
    private readonly HankiButton copy = MakeButton("Copy reviewed prompt");
    private readonly Label status = new() { AutoSize = true, Text = "Local preparation only — no AI model is running inside Hanki." };
    public AssistantPanel()
    {
        Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { AutoSize = true, Text = "1. Paste/edit a log below. Review usernames, paths, hostnames, IPv4/IPv6, emails, tokens and customer data." }, 0, 0);
        layout.Controls.Add(question, 0, 1); layout.Controls.Add(source, 0, 2);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var redact = MakeButton("Redact selection"); var mask = MakeButton("Mask common patterns"); var prepare = MakeButton("Prepare for ChatGPT"); var clear = MakeButton("Clear draft");
        bar.Controls.AddRange([redact, mask, prepare, clear]); layout.Controls.Add(bar, 0, 3);
        layout.Controls.Add(new Label { AutoSize = true, Text = "2. Review the exact prompt below. Pattern masking is incomplete and may alter useful evidence. Edit the source and prepare again." }, 0, 4);
        layout.Controls.Add(preview, 0, 5);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var open = MakeButton("Open ChatGPT"); actions.Controls.AddRange([reviewed, copy, open]); layout.Controls.Add(actions, 0, 6);
        layout.Controls.Add(status, 0, 7); Controls.Add(layout);
        source.TextChanged += (_, _) => InvalidatePreview(); question.TextChanged += (_, _) => InvalidatePreview();
        reviewed.CheckedChanged += (_, _) => copy.Enabled = reviewed.Checked && preview.TextLength > 0;
        redact.Click += (_, _) => { if (source.SelectionLength > 0) source.SelectedText = "[REDACTED]"; };
        mask.Click += (_, _) => {
            try { source.Text = AssistantPrompt.MaskCommon(source.Text); status.Text = "Masked common user-profile paths, emails and IPv4-like strings. IPv6, secrets and other identifiers may remain. Review manually."; }
            catch (Exception ex) { status.Text = "Masking failed; source retained: " + ex.Message; }
        };
        prepare.Click += (_, _) => {
            try { preview.Text = AssistantPrompt.Build(question.Text, source.Text); reviewed.Checked = false; copy.Enabled = false;
                status.Text = "Preview ready. Nothing sent. Clipboard history/sync may retain what you copy; review before copying."; }
            catch (ArgumentException ex) { status.Text = ex.Message; }
        };
        clear.Click += (_, _) => {
            if (MessageBox.Show(this, "Discard this local draft? This does not erase clipboard history or reports in other modules.", "Clear draft",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.OK)
            { source.Clear(); question.Clear(); status.Text = "Draft cleared. Clipboard unchanged."; }
        };
        copy.Click += (_, _) => {
            if (!reviewed.Checked || preview.TextLength == 0) return;
            try { Clipboard.SetText(preview.Text); status.Text = "Copied. Paste into your chosen ChatGPT conversation yourself. Nothing uploaded by Hanki."; }
            catch (ExternalException) { status.Text = "Clipboard busy. Try copying again."; }
        };
        open.Click += (_, _) => DesktopShortcuts.Open(this, "chatgpt");
        var ai = MakeButton("Use reviewed prompt in in-app AI…");
        ai.Click += (_, _) => { if (reviewed.Checked && preview.TextLength > 0) AiRequested?.Invoke(preview.Text); else MessageBox.Show(this, "Prepare and review the prompt first, then check the review box."); };
        actions.Controls.Add(ai);
        copy.Enabled = false;
    }
    public bool LoadReport(string report)
    {
        if (report.Length > AssistantPrompt.MaximumLength) { MessageBox.Show(this, "Report too long. Paste relevant excerpts (up to 100,000 characters).", "Hanki Assistant"); return false; }
        if (source.TextLength > 0 && MessageBox.Show(this, "Replace your current draft with this report?", "Hanki Assistant",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return false;
        source.Text = report;
        status.Text = "Report loaded locally. Review/redact it, then prepare the prompt. No data uploaded.";
        return true;
    }
    private void InvalidatePreview() { preview.Clear(); reviewed.Checked = false; copy.Enabled = false; }
    private static HankiButton MakeButton(string text) => new() { Text = text, AutoSize = true, ForeColor = Color.Black };
    private static TextBox Area(bool readOnly) => new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = readOnly, MaxLength = int.MaxValue,
        ScrollBars = ScrollBars.Both, WordWrap = true, BackColor = Color.FromArgb(24, 36, 52), ForeColor = Color.WhiteSmoke };
}
