using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace IgezziGuard.Shell;

/// <summary>Cards that open a page, in a grid of three, two or one across: the body of the landing pages.</summary>
internal static class LandingCards
{
    internal static UIElement Grid(IEnumerable<(string Icon, string Title, string Text, Action Open)> tiles, string accent = "Accent")
    {
        var grid = new UniformGrid { Columns = 3 };
        grid.SizeChanged += (_, e) => grid.Columns = e.NewSize.Width >= 900 ? 3 : e.NewSize.Width >= 560 ? 2 : 1;
        foreach (var (icon, title, text, open) in tiles) { var card = UiKit.ToolCard(icon, title, text, open, accent); card.Margin = new Thickness(0, 0, 14, 14); grid.Children.Add(card); }
        return grid;
    }
    internal static IEnumerable<(string, string, string, Action)> For(IShellServices shell, ProductArea area) =>
        Navigation.Tools(area).Select(i => (i.Icon, Navigation.Title(i), i.Introduction, (Action)(() => shell.Navigate(i.Page))));
    internal static TextBlock Heading(string text) => new() { Text = text, Style = (Style)Application.Current.FindResource("SectionHeading") };
}

/// <summary>History: Recovery to undo a change, and what Hanki has done (system actions and performance sessions).</summary>
internal sealed class HistoryLandingPage : NativePage
{
    internal HistoryLandingPage(IShellServices shell)
    {
        var (scroller, stack) = Blocks.Page();
        var recovery = Navigation.Find("Recovery")!;
        stack.Children.Add(LandingCards.Heading("Undo a change")); stack.Children.Add(LandingCards.Grid([(recovery.Icon, "Recovery", recovery.Introduction, () => shell.Navigate("Recovery"))]));
        stack.Children.Add(LandingCards.Heading("What Hanki has done")); stack.Children.Add(LandingCards.Grid(LandingCards.For(shell, ProductArea.History)));
        Content = scroller;
    }
}

/// <summary>Help: remote help, then guides and community, the Assistant and Hanki Pro.</summary>
internal sealed class HelpLandingPage : NativePage
{
    internal HelpLandingPage(IShellServices shell)
    {
        var (scroller, stack) = Blocks.Page();
        stack.Children.Add(LandingCards.Heading("More help"));
        stack.Children.Add(LandingCards.Grid([("Help", "Remote help", "Opens Windows' Quick Assist so someone you trust can see your screen. Hanki shows a scam warning first.", () => QuickAssist.Open(shell.DialogOwner, gettingHelp: true))]));
        stack.Children.Add(LandingCards.Heading("Help and support")); stack.Children.Add(LandingCards.Grid(LandingCards.For(shell, ProductArea.Support)));
        Content = scroller;
    }
}

/// <summary>Help &amp; community: the community, remote help, a bug report draft, version details and privacy information.</summary>
internal sealed class SupportPage : NativePage
{
    private const string Repository = "https://github.com/hankitools/hankitools-windows";
    internal int SectionCount { get; private set; }

    internal SupportPage(IShellServices shell)
    {
        var (scroller, stack) = Blocks.Page();
        void Add(Border section) { stack.Children.Add(section); SectionCount++; }
        Add(Blocks.Section("Meet the Hanki community", "Ask a question, share feedback or follow the project. Links open in your browser; no report is attached.",
            ("Open hanki.tools ↗", () => Blocks.OpenLink("https://hanki.tools/")), ("Join Discord ↗", () => Blocks.OpenLink("https://discord.gg/qprzjtTaQ")), ("Read the user guide ↗", () => Blocks.OpenLink(Repository + "/blob/main/README-PORTABLE.md"))));
        Add(Blocks.Section("Get help from someone you trust", QuickAssist.HowItWorks + "\n\nHanki only opens Quick Assist; the session runs through Microsoft's service. To show what Hanki found, run a scan first and use Review / share report.",
            ("Get help (Quick Assist)", () => QuickAssist.Open(shell.DialogOwner, gettingHelp: true)), ("Help someone (Quick Assist)", () => QuickAssist.Open(shell.DialogOwner, gettingHelp: false))));
        Add(Blocks.Section("Something not working?", "Prepare a useful report with steps to reproduce the problem. Review and copy the draft, then paste it into a GitHub issue. Avoid passwords, API keys and personal logs.",
            ("Prepare bug report", PrepareReport), ("Open GitHub issues ↗", () => Blocks.OpenLink(Repository + "/issues"))));
        Add(Blocks.Section("Your version", AppDetails + "\n\nUpdates are manual. Check release notes and package instructions before replacing your app. Keep recovery data until supported changes are undone.",
            ("Copy app details", () => Blocks.Copy(AppDetails)), ("View releases ↗", () => Blocks.OpenLink(Repository + "/releases")), ("Read release notes ↗", () => Blocks.OpenLink(Repository + "/blob/main/RELEASE-NOTES.md"))));
        Add(Blocks.Section("Privacy & project information", "Hanki Tools is distributed under the MIT license. The experimental scanner is not a replacement antivirus. Discord and GitHub issues may be public: share only information you have reviewed.",
            ("Privacy information ↗", () => Blocks.OpenLink(Repository + "/blob/main/PRIVACY.md")), ("Source & license ↗", () => Blocks.OpenLink(Repository))));
        Content = scroller;
    }

    internal static string AppDetails => SupportPanel.AppDetails.Replace("\r\n", "\n");

    private static void PrepareReport()
    {
        string draft = "## What happened?\n[Describe the problem]\n\n## Steps to reproduce\n1. \n2. \n3. \n\n## Expected result\n\n## Actual result / error message\n\n## App details\n" + AppDetails + "\n\nNothing here is sent anywhere. Review it, remove anything private, then copy it into a GitHub issue.";
        Blocks.EditText(Application.Current.MainWindow, "Prepare a bug report", draft,
            ("Copy reviewed draft", text => { if (!string.IsNullOrWhiteSpace(text)) Blocks.Copy(text); }, true),
            ("Open new GitHub issue ↗", _ => Blocks.OpenLink(Repository + "/issues/new"), false));
    }
}

/// <summary>Hanki Pro: which edition this PC has, the licence key, and what Pro and Technician add.</summary>
internal sealed class LicensePage : NativePage
{
    private readonly TextBlock heading = UiKit.Text("", 19, weight: FontWeights.SemiBold, wrap: true), details = UiKit.Text("", 14, UiKit.Res("TextMuted"), wrap: true), message = UiKit.Text("", 14, wrap: true);
    private readonly TextBox key = Buttons.Field("Paste your licence key", 420);
    private readonly Button activate = Buttons.Primary("Activate"), check = Buttons.Secondary("Check licence now"), remove = Buttons.Secondary("Remove from this PC");
    private readonly StackPanel entry = new(), licensed = new();
    private readonly TextBlock entryNote = UiKit.Text("Activating sends the key to Polar, the store that sells Hanki Pro, to check it. No scan results, files or PC names are sent.", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private bool busy;
    internal string Heading => heading.Text;

    internal LicensePage(IShellServices shell)
    {
        var (scroller, stack) = Blocks.Page();
        key.MaxLength = 200;
        var top = new StackPanel(); top.Children.Add(heading); details.Margin = new Thickness(0, 8, 0, 14); top.Children.Add(details);
        var entryRow = new WrapPanel(); entryRow.Children.Add(key); entryRow.Children.Add(activate); entry.Children.Add(entryRow); entryNote.Margin = new Thickness(0, 6, 0, 0); entry.Children.Add(entryNote);
        var licensedRow = new WrapPanel(); licensedRow.Children.Add(check); licensedRow.Children.Add(remove); licensed.Children.Add(licensedRow);
        top.Children.Add(entry); top.Children.Add(licensed); message.Margin = new Thickness(0, 10, 0, 0); top.Children.Add(message);
        stack.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(22, 18, 22, 16), Margin = new Thickness(0, 0, 0, 14), Child = top });
        var compare = new StackPanel(); compare.Children.Add(UiKit.Text("What Pro and Technician add", 17, weight: FontWeights.SemiBold));
        foreach (var paragraph in new[] {
            "Hanki Community, free: every tool in the app, Fix my PC scans, saved history, guided checks and Quick Assist. They stay free.",
            "Hanki Pro, one-time purchase: scheduled checks that run while you're away, and automatic repairs for the Windows component store (DISM), protected system files (SFC) and the DNS cache. You review every repair before it runs, and you can undo what can be undone.",
            "Hanki Technician, yearly per technician: everything in Pro, plus customer reports you can print or save as PDF for the people whose PCs you fix.",
            "Buying either one supports the free edition." }) { var t = UiKit.Text(paragraph, 14, UiKit.Res("TextMuted"), wrap: true); t.Margin = new Thickness(0, 10, 0, 0); compare.Children.Add(t); }
        var links = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var prices = Buttons.Secondary("See prices ↗"); prices.Click += (_, _) => Blocks.OpenLink(LicensePanel.BuyPage);
        var terms = Buttons.Secondary("Licence terms ↗"); terms.Click += (_, _) => Blocks.OpenLink(LicensePanel.TermsPage);
        links.Children.Add(prices); links.Children.Add(terms); compare.Children.Add(links);
        stack.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(22, 18, 22, 12), Child = compare });
        Content = scroller;
        activate.Click += async (_, _) => await Activate();
        check.Click += async (_, _) => await CheckNow();
        remove.Click += async (_, _) => await Remove();
        key.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Activate(); } };
        AppLicensing.Changed += () => Dispatcher.BeginInvoke(ShowState);
        Unloaded += (_, _) => { };
        ShowState();
    }

    internal override void OnShown() => ShowState();

    private void ShowState()
    {
        var provider = AppLicensing.Provider; var stored = provider?.Stored;
        bool onSale = provider?.Config.OnSale == true;
        entry.Visibility = onSale && stored is null ? Visibility.Visible : Visibility.Collapsed;
        licensed.Visibility = stored is not null ? Visibility.Visible : Visibility.Collapsed;
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
                + "\n\nOn a customer's PC, choose Remove from this PC before you hand it back. That frees the slot for your next job.";
        else text += ". Pro is a one-time purchase: Hanki doesn't need to check it again.";
        details.Text = text;
    }

    private async Task Activate()
    {
        if (AppLicensing.Provider is not { } provider || busy) return;
        string typed = key.Text;
        await Busy("Checking the key with Polar…", async token => {
            var stored = await provider.ActivateAsync(typed, token);
            AppLicensing.Use(stored); Dispatcher.Invoke(key.Clear);
            return $"{AppLicensing.Name(stored.Edition)} is active on this PC. Thank you for supporting Hanki!";
        });
    }
    private async Task CheckNow()
    {
        if (AppLicensing.Provider is not { } provider || provider.Stored is not { } stored || busy) return;
        await Busy("Checking the licence with Polar…", async token => {
            var updated = await provider.CheckAsync(stored, token); AppLicensing.Use(updated);
            return updated is null ? "Polar no longer accepts this key (it may have been cancelled or refunded), so it was removed from this PC." : "Licence checked. Everything is in order.";
        });
    }
    private async Task Remove()
    {
        if (AppLicensing.Provider is not { } provider || busy) return;
        if (!Blocks.Confirm("Remove the licence from this PC?", "You can activate the same key again later, here or on another PC.", "Remove licence", danger: true)) return;
        await Busy("Removing the licence…", async token => {
            try { await provider.RevokeDeviceAsync(token); return "Removed from this PC. The key is free to use on another PC."; }
            catch (LicenseException ex) { return "Removed from this PC, but this PC may still count towards the key's limit on Polar. " + ex.Message; }
            finally { AppLicensing.Use(null); }
        });
    }
    private async Task Busy(string working, Func<CancellationToken, Task<string>> work)
    {
        busy = true; activate.IsEnabled = check.IsEnabled = remove.IsEnabled = key.IsEnabled = false; message.Text = working;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { message.Text = await Task.Run(() => work(timeout.Token), timeout.Token); }
        catch (LicenseException ex) { message.Text = ex.Message; }
        catch (OperationCanceledException) { message.Text = PolarLicenseProvider.Offline; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException) {
            message.Text = "Hanki couldn't save the licence in Windows Credential Manager: " + ex.Message;
        }
        finally { busy = false; activate.IsEnabled = check.IsEnabled = remove.IsEnabled = key.IsEnabled = true; ShowState(); }
    }
}
