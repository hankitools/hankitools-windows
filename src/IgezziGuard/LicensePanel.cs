using System.Diagnostics;
namespace IgezziGuard;

/// <summary>Hanki Pro page: which edition this PC has, the licence key, and what Pro and Technician add.</summary>
internal sealed class LicensePanel : UserControl
{
    internal const string BuyPage = "https://hanki.tools/pro";
    internal const string TermsPage = "https://hanki.tools/terms";
    private readonly Label heading = new() { AutoSize = true, Dock = DockStyle.Top, Font = new Font("Segoe UI", 14, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10) };
    private readonly Label details = new() { AutoSize = true, Dock = DockStyle.Top, Tag = "intro", Margin = new Padding(0, 0, 0, 14) };
    private readonly Label message = new() { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 0), AccessibleName = "Licence status message" };
    private readonly TextBox key = new() { Width = 420, PlaceholderText = "Paste your licence key", AccessibleName = "Licence key", MaxLength = 200, Margin = new Padding(0, 4, 8, 0) };
    private readonly FlowLayoutPanel entry = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
    private readonly FlowLayoutPanel licensed = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
    private readonly Label entryNote = new() { AutoSize = true, Dock = DockStyle.Top, Tag = "intro", Margin = new Padding(0, 10, 0, 0),
        Text = "Activating sends the key to Polar, the store that sells Hanki Pro, to check it. No scan results, files or PC names are sent." };
    private readonly List<HankiButton> actions = [];
    private bool busy;

    public LicensePanel()
    {
        Dock = DockStyle.Fill; AutoScroll = true; Padding = new Padding(16);
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(4) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        TableLayoutPanel Card(params Control[] controls)
        {
            var card = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(20), Margin = new Padding(0, 0, 0, 16), Tag = "card" };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            card.SizeChanged += (_, _) => { var wrap = new Size(Math.Max(100, card.ClientSize.Width - card.Padding.Horizontal), 0); foreach (var label in controls.OfType<Label>()) label.MaximumSize = wrap; };
            card.Controls.AddRange(controls); layout.Controls.Add(card); return card;
        }
        HankiButton Action(FlowLayoutPanel bar, string text, Action action, bool primary = false)
        {
            var button = new HankiButton { Text = text, AutoSize = true, Primary = primary, Appearance = primary ? HankiButtonStyle.Secondary : HankiButtonStyle.Quiet, Margin = new Padding(0, 3, 6, 0) };
            button.Click += (_, _) => { if (!busy) action(); }; bar.Controls.Add(button); actions.Add(button); return button;
        }
        entry.Controls.Add(key);
        var activate = Action(entry, "Activate", Activate, primary: true);
        key.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; activate.PerformClick(); } };
        Action(licensed, "Check licence now", CheckNow);
        Action(licensed, "Remove from this PC", Remove);
        Card(heading, details, entry, licensed, entryNote, message);

        var compareTitle = new Label { Text = "What Pro and Technician add", AutoSize = true, Dock = DockStyle.Top, Font = new Font("Segoe UI", 14, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10) };
        var compare = new Label { AutoSize = true, Dock = DockStyle.Top, Tag = "intro", Margin = new Padding(0, 0, 0, 14), Text =
            "Hanki Community, free: every tool in the app, Full System Scan, saved history, guided checks and Quick Assist. They stay free.\r\n\r\n" +
            "Hanki Pro, one-time purchase: scheduled checks that run while you're away, and automatic repairs for the Windows component store (DISM), protected system files (SFC) and the DNS cache. You review every repair before it runs, and Hanki checks afterwards whether it worked.\r\n\r\n" +
            "Hanki Technician, yearly per technician: everything in Pro, plus customer reports you can print or save as PDF for the people whose PCs you fix.\r\n\r\n" +
            "Buying either one supports the free edition." };
        var links = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        Action(links, "See prices ↗", () => Open(BuyPage));
        Action(links, "Licence terms ↗", () => Open(TermsPage));
        Card(compareTitle, compare, links);
        Controls.Add(layout);

        AppLicensing.Changed += OnChanged;
        VisibleChanged += (_, _) => { if (Visible) ShowState(); };
        ShowState();
    }
    protected override void Dispose(bool disposing) { if (disposing) AppLicensing.Changed -= OnChanged; base.Dispose(disposing); }
    private void OnChanged() { if (IsHandleCreated && !IsDisposed) BeginInvoke(ShowState); }

    private void ShowState()
    {
        var provider = AppLicensing.Provider; var stored = provider?.Stored;
        bool onSale = provider?.Config.OnSale == true;
        entry.Visible = entryNote.Visible = onSale && stored is null;
        licensed.Visible = stored is not null;
        if (stored is null) {
            heading.Text = AppLicensing.Name(HankiEdition.Community);
            details.Text = onSale ? "Enter the licence key from your receipt email to unlock Hanki Pro or Technician on this PC."
                : "Hanki Pro and Technician aren't on sale yet. When they are, you'll enter your licence key here. Everything you use today stays free.";
            return;
        }
        var ends = PolarLicenseProvider.Validated(stored)!.Expires;
        bool active = DateTimeOffset.UtcNow < ends;
        heading.Text = AppLicensing.Name(stored.Edition) + (active ? "" : " (needs a check)");
        var text = $"Key {stored.DisplayKey} · last checked {stored.LastChecked.ToLocalTime():d}";
        if (stored.Edition == HankiEdition.Technician)
            text += (active ? $" · works offline until {ends.ToLocalTime():d}. Hanki checks the subscription with Polar about once a week."
                : ". Hanki couldn't check the subscription for 30 days. Connect to the internet and choose Check licence now.")
                + "\r\n\r\nOn a customer's PC, choose Remove from this PC before you hand it back. That frees the slot for your next job.";
        else text += ". Pro is a one-time purchase: Hanki doesn't need to check it again.";
        details.Text = text;
    }

    private async void Activate()
    {
        if (AppLicensing.Provider is not { } provider) return;
        await Busy("Checking the key with Polar…", async token => {
            var stored = await provider.ActivateAsync(key.Text, token);
            AppLicensing.Use(stored); key.Clear();
            return $"{AppLicensing.Name(stored.Edition)} is active on this PC. Thank you for supporting Hanki!";
        });
    }
    private async void CheckNow()
    {
        if (AppLicensing.Provider is not { } provider || provider.Stored is not { } stored) return;
        await Busy("Checking the licence with Polar…", async token => {
            var updated = await provider.CheckAsync(stored, token);
            AppLicensing.Use(updated);
            return updated is null ? "Polar no longer accepts this key (it may have been cancelled or refunded), so it was removed from this PC." : "Licence checked. Everything is in order.";
        });
    }
    private async void Remove()
    {
        if (AppLicensing.Provider is not { } provider) return;
        if (MessageBox.Show(this, "Remove the licence from this PC? You can activate the same key again later, here or on another PC.", "Hanki Pro",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
        await Busy("Removing the licence…", async token => {
            try { await provider.RevokeDeviceAsync(token); return "Removed from this PC. The key is free to use on another PC."; }
            catch (LicenseException ex) { return "Removed from this PC, but this PC may still count towards the key's limit on Polar. " + ex.Message; }
            finally { AppLicensing.Use(null); }
        });
    }
    private async Task Busy(string working, Func<CancellationToken, Task<string>> work)
    {
        busy = true; foreach (var button in actions) button.Enabled = false; key.Enabled = false; message.Text = working;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { message.Text = await work(timeout.Token); }
        catch (LicenseException ex) { message.Text = ex.Message; }
        catch (OperationCanceledException) { message.Text = PolarLicenseProvider.Offline; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException) {
            message.Text = "Hanki couldn't save the licence in Windows Credential Manager: " + ex.Message;
        }
        finally { busy = false; foreach (var button in actions) button.Enabled = true; key.Enabled = true; ShowState(); }
    }
    private void Open(string url)
    {
        try { using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open browser"); }
    }
}
