using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace IgezziGuard.Shell;

/// <summary>A drop-down that looks like the rest of the app, with an accessible name.</summary>
internal static class Pick
{
    internal static ComboBox Box(string name, double width)
    {
        var box = new ComboBox { Width = width, Margin = new Thickness(0, 0, 8, 8) };
        System.Windows.Automation.AutomationProperties.SetName(box, name);
        return box;
    }
}

/// <summary>System actions → Timeline: everything Fix my PC has done on this PC, newest first.</summary>
internal sealed class TimelineView : ScrollViewer
{
    private readonly TextBox output = Blocks.Output(900);
    internal string Text => output.Text;

    internal TimelineView()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var stack = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        stack.Children.Add(UiKit.Text("Everything Fix my PC has done on this PC: scans, repairs, removals and recorded Windows changes, newest first. Undo supported changes in Recovery. Performance tests are listed separately under Performance sessions.", 14, UiKit.Res("TextMuted"), wrap: true));
        var refresh = Buttons.Secondary("Refresh"); refresh.Margin = new Thickness(0, 14, 0, 4); refresh.HorizontalAlignment = HorizontalAlignment.Left; refresh.Click += (_, _) => Load();
        stack.Children.Add(refresh); stack.Children.Add(output); Content = stack;
    }

    internal void Load()
    {
        try {
            var scans = new DiagnosticHistory(Path.Combine(SecurityPaths.Root, "diagnostic-history.json")).Read();
            var repairs = new RepairAudit(Path.Combine(SecurityPaths.Root, "repair-audit.json")).Read();
            var changes = WindowsSettings.Journal().Read();
            output.Text = SystemActions.Format(SystemActions.Timeline(scans, repairs, changes, RemovalLog.Default.Read())).Replace("\r\n", "\n");
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) {
            output.Text = "System history couldn't be read: " + ex.Message + "\nOriginal files were preserved.";
        }
    }
}

/// <summary>System actions → Saved scans: open or compare saved full scans, schedule a local check (Pro), make a customer report (Technician), clear the history.</summary>
internal sealed class SavedScansView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly ComboBox first = Pick.Box("Scan to open", 250), second = Pick.Box("Scan to compare with", 250), frequency = Pick.Box("Scheduled check frequency", 130);
    private readonly TextBlock scheduleStatus = UiKit.Text("", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private readonly TextBox output = Blocks.Output(700);
    private readonly DiagnosticHistory history = new(Path.Combine(SecurityPaths.Root, "diagnostic-history.json"));
    private readonly RepairAudit audit = new(Path.Combine(SecurityPaths.Root, "repair-audit.json"));
    private IReadOnlyList<DiagnosticScan> scans = [];
    internal int ScanCount => scans.Count;

    internal SavedScansView(IShellServices shell)
    {
        this.shell = shell; VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var stack = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        stack.Children.Add(UiKit.Text("Local scan history keeps up to 30 scans for 90 days, without raw diagnostic evidence. Repair audit is retained separately, including incomplete attempts. No cloud account or upload. Existing scanner history and undo journals are not removed by clearing scan history.", 14, UiKit.Res("TextMuted"), wrap: true));
        foreach (var f in new[] { "Daily", "Weekly" }) frequency.Items.Add(f); frequency.SelectedIndex = 0;
        var openRow = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var open = Buttons.Primary("Open selected scan"); var compare = Buttons.Secondary("Compare selected scans");
        var with = UiKit.Text("compare with", 13.5, UiKit.Res("TextMuted")); with.VerticalAlignment = VerticalAlignment.Center; with.Margin = new Thickness(6, 0, 10, 8);
        openRow.Children.Add(first); openRow.Children.Add(open); openRow.Children.Add(with); openRow.Children.Add(second); openRow.Children.Add(compare);
        var more = new WrapPanel();
        var label = UiKit.Text("Scheduled check", 13.5, UiKit.Res("TextMuted")); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 0, 10, 8); more.Children.Add(label); more.Children.Add(frequency);
        Button Extra(string text, Func<Task> action) { var b = Buttons.Quiet(text); b.Click += async (_, _) => await action(); more.Children.Add(b); return b; }
        Extra("Schedule check", Schedule); Extra("Remove schedule", RemoveSchedule); Extra("Refresh saved scans", async () => { Refresh(); await ShowScheduleStatus(); });
        Extra("Customer report", () => { CustomerReport(); return Task.CompletedTask; }); Extra("Clear scan history", () => { Clear(); return Task.CompletedTask; });
        stack.Children.Add(openRow); stack.Children.Add(more); scheduleStatus.Margin = new Thickness(2, 0, 0, 0); stack.Children.Add(scheduleStatus); stack.Children.Add(output);
        Content = stack;
        open.Click += (_, _) => Open(); compare.Click += (_, _) => Compare();
    }

    internal void LoadOnShow()
    {
        try { Load(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { output.Text = "Saved history is unavailable or damaged. Original files were preserved; current scans can still be run."; }
        _ = ShowScheduleStatus();
    }

    private void Load()
    {
        scans = history.Read(); first.Items.Clear(); second.Items.Clear();
        foreach (var s in scans) { var text = $"{s.Ended.ToLocalTime():g} · {s.Results.Count} findings"; first.Items.Add(text); second.Items.Add(text); }
        if (scans.Count > 0) first.SelectedIndex = 0; if (scans.Count > 1) second.SelectedIndex = 1;
    }
    private void Refresh()
    {
        try { Load(); output.Text = $"{scans.Count} saved scans. The newest is selected; choose another to open or compare."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { output.Text = "Saved history is unavailable or damaged. Original files were preserved; current scans can still be run."; }
    }
    private async Task ShowScheduleStatus()
    {
        try { scheduleStatus.Text = ScheduledHealthChecks.Describe(await ScheduledHealthChecks.StatusAsync(CancellationToken.None)); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { scheduleStatus.Text = "Scheduled check status unavailable."; }
    }

    private void Open()
    {
        if (first.SelectedIndex < 0) return;
        var scan = scans[first.SelectedIndex];
        try { output.Text = (FullScanPanel.Summary(scan) + "\r\n\r\n" + RepairReportText.Format(new(scan.Id, audit.Read().Where(a => a.ScanId == scan.Id).Select(a => a.Attempt).ToArray()))).Replace("\r\n", "\n"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { output.Text = "Repair audit unavailable. Existing files were preserved."; }
    }

    private void Compare()
    {
        if (first.SelectedIndex < 0 || second.SelectedIndex < 0) return;
        var a = scans[first.SelectedIndex]; var b = scans[second.SelectedIndex];
        if (a.Ended > b.Ended) (a, b) = (b, a);
        output.Text = $"{a.Ended.ToLocalTime():g} → {b.Ended.ToLocalTime():g}\nChanges are observations, not proof of repair causation.\n\n" + string.Join("\n", DiagnosticHistory.Compare(a, b));
    }

    private async Task Schedule()
    {
        var entitlements = EntitlementComposition.Current();
        if (!entitlements.Allows(HankiCapability.ScheduledChecks)) { output.Text = "Scheduled checks are part of Hanki Pro. See the Hanki Pro page in the sidebar. Running a Full System Scan yourself and saved history stay free."; return; }
        var chosen = frequency.SelectedIndex == 1 ? HealthCheckFrequency.Weekly : HealthCheckFrequency.Daily;
        if (!ReviewPresenter.Ask($"Create a current-user Windows scheduled task for a {chosen.ToString().ToLowerInvariant()} local check at 19:00 (weekly: Sunday)? Runs only while signed in, at standard privilege, without external probes or repairs. Keep this copy of Hanki where it is, or the task will stop working.")) return;
        using var ticket = shell.Tasks.Begin("Saved scans", () => { });
        try { await ScheduledHealthChecks.InstallAsync(chosen, entitlements, CancellationToken.None); output.Text = "Schedule registered. Manage or remove it in Windows Task Scheduler; results appear in local diagnostic history."; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { output.Text = "Operation stopped: " + ex.Message; }
        await ShowScheduleStatus();
    }

    private async Task RemoveSchedule()
    {
        if (!ReviewPresenter.Ask("Remove the Hanki local health-check task? Existing history is kept.")) return;
        try { await ScheduledHealthChecks.RemoveAsync(CancellationToken.None); output.Text = "Schedule removed."; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { output.Text = "Operation stopped: " + ex.Message; }
        await ShowScheduleStatus();
    }

    private void CustomerReport()
    {
        if (first.SelectedIndex < 0) { output.Text = "Choose a saved scan first."; return; }
        var scan = scans[first.SelectedIndex]; RepairReport repairs;
        try { repairs = new(scan.Id, audit.Read().Where(a => a.ScanId == scan.Id).Select(a => a.Attempt).ToArray()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { output.Text = "Repair audit unavailable. Existing files were preserved."; return; }
        if (CustomerReportFlow.Create(shell.DialogOwner, scan, repairs) is { } status) output.Text = status;
    }

    private void Clear()
    {
        if (!ReviewPresenter.Ask("Remove saved scan summaries? Repair audit, recovery backups and legacy scanner history will remain available.")) return;
        try { history.Clear(); Refresh(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { output.Text = "History could not be cleared."; }
    }
}

/// <summary>Performance sessions: measurements and optimization tests with their baseline, changes tested and outcome.</summary>
internal sealed class SessionsView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly ComboBox list = Pick.Box("Performance session", 460);
    private readonly TextBox output = Blocks.Output(700);
    private IReadOnlyList<PerformanceSession> sessions = [];
    internal int SessionCount => sessions.Count;

    internal SessionsView(IShellServices shell)
    {
        this.shell = shell; VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var stack = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        stack.Children.Add(UiKit.Text("Each session keeps a baseline measurement, the changes tested and the measurement afterwards. Hanki only calls a change an improvement when the runs are comparable and the difference is larger than normal variation.", 14, UiKit.Res("TextMuted"), wrap: true));
        var bar = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) }; bar.Children.Add(list);
        Button Add(string text, Action action, bool primary = false) { var b = primary ? Buttons.Primary(text) : Buttons.Secondary(text); b.Click += (_, _) => action(); bar.Children.Add(b); return b; }
        Add("Open session", Open, true); Add("Restore settings", () => _ = Restore()); Add("Refresh", Load); Add("Remove session", Remove);
        stack.Children.Add(bar); stack.Children.Add(output); Content = stack;
        list.SelectionChanged += (_, _) => Open();
    }

    internal void Load()
    {
        try {
            sessions = PerformanceSessionsPanel.Store.Read(); list.Items.Clear();
            foreach (var s in sessions) list.Items.Add($"{s.Created.ToLocalTime():g} · {s.Name}");
            if (sessions.Count > 0) list.SelectedIndex = 0;
            else output.Text = "No performance sessions yet.\n\nIn Performance Lab → Monitor, run a measurement and choose Save to Performance sessions. Optimization tests will be saved here too, with the settings they changed.";
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { output.Text = ex.Message; }
    }

    private void Open()
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= sessions.Count) return;
        var s = sessions[list.SelectedIndex]; IReadOnlyList<SettingChange> changes = [];
        try { changes = WindowsSettings.Journal().Read().Where(c => s.ChangesTested.Contains(c.Id)).ToArray(); } catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { }
        output.Text = ($"{s.Name}\r\n{s.Created.ToLocalTime():f}\r\n\r\n{PerformanceComparison.Describe(s.Outcome)}\r\n\r\n" + PerformanceComparison.Table(s.Baseline, s.After) + "\r\n\r\n" +
            (s.ChangesTested.Count == 0 ? "Changes tested: none (measurement only)." : "Changes tested:\r\n" + string.Join("\r\n", changes.Select(c => $"• {c.Kind}: {c.Target}: {c.Before} → {c.After} ({c.Status})")) + "\r\nTo restore the previous settings, undo these changes in Recovery.") +
            (s.Summary.Length > 0 ? "\r\n\r\n" + s.Summary : "")).Replace("\r\n", "\n");
    }

    private async Task Restore()
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= sessions.Count) return;
        var session = sessions[list.SelectedIndex];
        if (session.ChangesTested.Count == 0) { output.Text = "This session didn't change any settings."; return; }
        if (!ReviewPresenter.Ask($"Put back the settings from before “{session.Name}”? Each change is undone through Recovery; a setting changed again since then is left alone.")) return;
        using var ticket = shell.Tasks.Begin("Performance sessions", () => { });
        var journal = WindowsSettings.Journal(); var lines = new List<string>();
        try {
            foreach (var change in journal.Read().Where(c => session.ChangesTested.Contains(c.Id)).OrderByDescending(c => c.At)) {
                if (change.Status != "Applied") { lines.Add($"• {change.Kind}: already {change.Status.ToLowerInvariant()}."); continue; }
                try { await journal.Undo(change.Id, CancellationToken.None); lines.Add($"✓ {change.Kind}: back to {change.Before}"); }
                catch (IOException ex) { lines.Add($"✗ {change.Kind}: {ex.Message}"); }
            }
            output.Text = "Restoring settings\n\n" + string.Join("\n", lines);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { output.Text = "Operation stopped: " + ex.Message; }
    }

    private void Remove()
    {
        if (list.SelectedIndex < 0 || !ReviewPresenter.Ask("Remove this session from Performance history? Any settings it changed stay as they are; undo them in Recovery.")) return;
        try { PerformanceSessionsPanel.Store.Remove(sessions[list.SelectedIndex].Id); Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { output.Text = "Couldn't remove it: " + ex.Message; }
    }
}

/// <summary>History → System actions (timeline and saved scans).</summary>
internal sealed class SystemActionsPage : NativePage
{
    private readonly SubTabs tabs = new();
    internal TimelineView? Timeline => tabs.ContentOf("Timeline") as TimelineView;
    internal SavedScansView? Saved => tabs.ContentOf("Saved scans") as SavedScansView;
    internal SystemActionsPage(IShellServices shell)
    {
        tabs.Add("Timeline", "Timeline", () => new TimelineView());
        tabs.Add("Saved scans", "Saved scans", () => new SavedScansView(shell));
        tabs.Changed += key => { if (tabs.ContentOf(key) is TimelineView t) t.Load(); else if (tabs.ContentOf(key) is SavedScansView s) s.LoadOnShow(); };
        Content = tabs;
    }
    internal override void OnShown() { if (tabs.Current is null) tabs.Select("Timeline"); }
    internal override void OnRoute(IReadOnlyList<string> path) { if (path.Count > 1 && tabs.Keys.Contains(path[1])) tabs.Select(path[1]); }
}

/// <summary>History → Performance sessions.</summary>
internal sealed class SessionsPage : NativePage
{
    private readonly SessionsView view;
    internal SessionsView View => view;
    internal SessionsPage(IShellServices shell) { view = new SessionsView(shell); Content = view; }
    internal override void OnShown() => view.Load();
}
