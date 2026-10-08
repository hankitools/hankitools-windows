using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using IWin32Window = System.Windows.Forms.IWin32Window;
using WpfButton = System.Windows.Controls.Button;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace IgezziGuard.Shell;

/// <summary>A WinForms-compatible owner for dialogs shown from the WPF window.</summary>
internal sealed class Win32Owner(IntPtr handle) : IWin32Window { public IntPtr Handle { get; } = handle; }

/// <summary>
/// The WPF shell: navigation rail, page header, command palette, status footer and window behavior around the hosted
/// <see cref="LegacyWorkspace"/> pages. Pages are ported out of the workspace one at a time (docs/REDESIGN.md).
/// </summary>
internal sealed partial class ShellWindow : Window, IShellServices
{
    private readonly LegacyWorkspace workspace;
    private readonly List<(WpfRadioButton Button, ProductArea Area, string Page)> rail = [];
    private readonly DispatcherTimer taskTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private string? backTarget;
    private string statusMessage = "";
    private readonly FullScanController scan = new();
    private readonly Dictionary<string, NativePage> nativePages = [];
    private Dictionary<string, Func<NativePage>> nativeFactories = [];

    internal LegacyWorkspace Workspace => workspace;
    internal IReadOnlyList<(WpfRadioButton Button, ProductArea Area, string Page)> Rail => rail;
    internal string CurrentTitle => TitleText.Text;
    internal string CurrentIntroduction => IntroText.Text;
    internal bool BackVisible => BackButton.Visibility == Visibility.Visible;
    internal IntPtr Hwnd => new WindowInteropHelper(this).EnsureHandle();
    internal IWin32Window Win32 => new Win32Owner(Hwnd);

    internal ShellWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Title = "Hanki Tools • " + AppInfo.Version;
        VersionText.Text = "v" + AppInfo.Version + "  ·  hanki.tools";
        LoadBranding();
        workspace = new LegacyWorkspace(() => Win32);
        // Pages built natively take over their destination; every other page stays a hosted WinForms page.
        nativeFactories = new() { ["Home"] = () => new HomePage(this), ["System overview"] = () => new FixLandingPage(this), ["Fix My PC"] = () => new FixScanPage(this) };
        // Create the native handles up front: WinForms raises tab-change events only for a control that has one, and the workspace
        // starts hidden when Home is a native page.
        _ = workspace.Handle; _ = workspace.Tabs.Handle;
        Host.Child = workspace;
        BuildRail();
        BuildQuickAccess();
        workspace.Tabs.SelectedIndexChanged += (_, _) => RefreshNavigation();
        workspace.StatusMessage += message => Dispatcher.BeginInvoke(() => { statusMessage = message; UpdateFooter(); });
        SearchButton.Click += (_, _) => ShowPalette();
        BackButton.Click += (_, _) => { if (backTarget is not null) workspace.Navigate(backTarget); };
        AboutButton.Click += (_, _) => AboutDialog.Show(Win32);
        CancelTasksButton.Click += (_, _) => { workspace.CancelTasks(); scan.Cancel(); };
        scan.Changed += () => Dispatcher.BeginInvoke(UpdateFooter);
        taskTimer.Tick += (_, _) => UpdateFooter();
        taskTimer.Start();
        RefreshNavigation();
        RestorePlacement();
        SourceInitialized += (_, _) => ComponentDispatcher.ThreadPreprocessMessage += OnThreadMessage;
        Closing += OnClosing;
        Closed += (_, _) => { ComponentDispatcher.ThreadPreprocessMessage -= OnThreadMessage; taskTimer.Stop(); };
        // Contacts Polar only when a Technician licence is due for its weekly check; offline, the stored licence keeps working.
        ContentRendered += async (_, _) => { RefreshNavigation(); try { await AppLicensing.RefreshAsync(CancellationToken.None); } catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException) { } };
    }

    private void LoadBranding()
    {
        var assembly = typeof(ShellWindow).Assembly;
        using (var png = assembly.GetManifestResourceStream("IgezziGuard.Brand.hanki-smile.png")) {
            if (png is not null) { var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = png; image.EndInit(); image.Freeze(); BrandMark.Source = image; }
        }
        using var ico = assembly.GetManifestResourceStream("IgezziGuard.Brand.hanki.ico");
        if (ico is not null) { var frame = BitmapFrame.Create(ico, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); frame.Freeze(); Icon = frame; }
    }

    private static string Glyph(string icon) => icon switch {
        "Home" => "", "Fix" => "", "Performance" => "", "Sessions" => "", "Help" => "", _ => ""
    };

    private void BuildRail()
    {
        foreach (var pageId in Navigation.Sidebar) {
            var item = Navigation.Find(pageId)!;
            var accent = (Brush)FindResource(item.Area == ProductArea.Performance ? "AccentPerformance" : "Accent");
            var content = new WpfStackPanel { Orientation = WpfOrientation.Horizontal };
            content.Children.Add(new WpfTextBlock { Text = Glyph(item.Icon), FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 18, Width = 28, VerticalAlignment = VerticalAlignment.Center, Foreground = accent });
            content.Children.Add(new WpfTextBlock { Text = item.Label, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            var button = new WpfRadioButton { Style = (Style)FindResource("RailButton"), GroupName = "rail", Tag = accent, Content = content };
            System.Windows.Automation.AutomationProperties.SetName(button, "Open " + item.Label);
            var page = item.Page;
            button.Click += (_, _) => workspace.Navigate(page);
            rail.Add((button, item.Area, page));
            RailItems.Children.Add(button);
        }
    }

    private void BuildQuickAccess()
    {
        foreach (var (text, key) in new[] { ("PowerShell (Admin)", "powershell"), ("CMD (Admin)", "cmd"), ("File Explorer", "explorer"), ("Task Manager", "task-manager"), ("Windows Settings", "settings"), ("Event Viewer", "event-viewer") }) {
            var shortcut = new WpfButton { Style = (Style)FindResource("QuietButton"), Content = text, FontSize = 12.5, HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
            var captured = key;
            shortcut.Click += (_, _) => DesktopShortcuts.Open(Win32, captured);
            QuickPanel.Children.Add(shortcut);
        }
        QuickPanel.Children.Add(new WpfTextBlock { Text = "Admin shortcuts use Windows UAC.", FontSize = 11.5, Foreground = (Brush)FindResource("TextMuted"), Margin = new Thickness(20, 4, 0, 4), TextWrapping = TextWrapping.Wrap });
        QuickToggle.Click += (_, _) => {
            bool open = QuickPanel.Visibility != Visibility.Visible;
            QuickPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            QuickToggle.Content = open ? "⌄  Quick access" : "›  Quick access";
            System.Windows.Automation.AutomationProperties.SetName(QuickToggle, open ? "Collapse quick access" : "Expand quick access");
        };
    }

    private void RefreshNavigation()
    {
        var tabs = workspace.Tabs;
        var current = tabs.SelectedTab ?? (tabs.TabCount > 0 ? tabs.TabPages[0] : null);
        var destination = current is null ? null : Navigation.Find(current.Text);
        var area = destination?.Area ?? ProductArea.Home;
        foreach (var entry in rail) entry.Button.IsChecked = entry.Area == area;
        TitleText.Text = destination is null ? current?.Text ?? "" : Navigation.Title(destination);
        bool landing = destination is null || Navigation.IsLanding(destination.Page);
        backTarget = landing ? null : Navigation.Landing(area);
        BackButton.Visibility = backTarget is null ? Visibility.Collapsed : Visibility.Visible;
        if (backTarget is not null) {
            BackButton.Content = "←  " + Navigation.Title(Navigation.Find(backTarget)!);
            System.Windows.Automation.AutomationProperties.SetName(BackButton, "Back to " + Navigation.Title(Navigation.Find(backTarget)!));
        }
        // Pages with their own hero don't repeat an introduction.
        IntroText.Text = destination is null || destination.Page is "Home" or "System overview" or "Performance overview" ? "" : destination.Introduction;
        IntroText.Visibility = IntroText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowDestination(current?.Text);
    }

    private void UpdateFooter()
    {
        var active = ActiveTasks();
        CancelTasksButton.Visibility = active.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RunningText.Text = active.Length == 0 ? "" : "Running: " + string.Join(", ", active);
        StatusText.Text = statusMessage;
        // The footer appears only while something runs or has a message; idle, the page gets the space.
        FooterBar.Visibility = active.Length > 0 || statusMessage.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }


    private string[] ActiveTasks() => scan.IsBusy ? ["Fix My PC", .. workspace.ActiveTasks()] : workspace.ActiveTasks();

    /// <summary>The native page currently shown in place of the hosted workspace, or null while a hosted page is shown.</summary>
    internal NativePage? CurrentNative { get; private set; }

    /// <summary>Shows the native page for a destination if there is one, otherwise the hosted workspace.</summary>
    private void ShowDestination(string? page)
    {
        NativePage? native = null;
        try {
            if (page is not null && nativeFactories.TryGetValue(page, out var create)) {
                if (!nativePages.TryGetValue(page, out native)) nativePages[page] = native = create();
            }
            if (ReferenceEquals(native, CurrentNative) && native is null) return;
            CurrentNative = native;
            NativeHost.Content = native;
            NativeHost.Visibility = native is null ? Visibility.Collapsed : Visibility.Visible;
            // Hidden, not collapsed: the workspace keeps its size so its pages stay laid out.
            Host.Visibility = native is null ? Visibility.Visible : Visibility.Hidden;
            native?.OnShown();
        } catch (Exception ex) {
            // A native page that fails must not leave the window on the wrong page: log it and show the hosted page instead.
            if (UiSmokeTest.Active) throw;
            try { File.AppendAllText(SecurityPaths.ErrorLog, $"[{DateTimeOffset.Now:O}] Native page '{page}' failed: {ex}\n\n"); } catch (Exception io) when (io is IOException or UnauthorizedAccessException) { }
            nativeFactories.Remove(page!); nativePages.Remove(page!);
            CurrentNative = null; NativeHost.Content = null; NativeHost.Visibility = Visibility.Collapsed; Host.Visibility = Visibility.Visible;
        }
    }

    // IShellServices: what native pages may ask of the shell.
    void IShellServices.Navigate(string page) => workspace.Navigate(page);
    IReadOnlyList<ToolLauncher.Route> IShellServices.Routes => workspace.Routes;
    void IShellServices.OpenGuide(int index) => workspace.OpenGuide(index);
    IWin32Window IShellServices.DialogOwner => Win32;
    void IShellServices.PrepareForAssistant(string text) => workspace.PrepareForAssistant(text);
    FullScanController IShellServices.Scan => scan;
    /// <summary>Opens Fix my PC and starts its scan (no network probes, so nothing needs confirming).</summary>
    public void StartFixMyPc()
    {
        workspace.Navigate("Fix My PC");
        _ = scan.StartAsync(false, () => true);
    }
    internal void ShowPalette()
    {
        var palette = new Palette(workspace.Routes) { Owner = this };
        palette.ShowDialog();
        palette.Chosen?.Open();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (workspace.CancelBeforeClose() | scan.IsBusy) { scan.Cancel(); e.Cancel = true; statusMessage = "Cancelling; close again when finished."; UpdateFooter(); return; }
        SavePlacement();
    }

    // Ctrl+K and F1 work wherever focus is, including inside a hosted page.
    private const int WmKeyDown = 0x0100, VkK = 0x4B, VkControl = 0x11;
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    private void OnThreadMessage(ref MSG msg, ref bool handled)
    {
        if (handled || msg.message != WmKeyDown || !IsActive) return;
        int key = (int)msg.wParam;
        if (key == VkK && (GetKeyState(VkControl) & 0x8000) != 0) { handled = true; Dispatcher.BeginInvoke(ShowPalette); }
        else if (key == 0x70) { handled = true; Dispatcher.BeginInvoke(() => workspace.Navigate("Help & community")); }
    }

    private sealed record Placement(double Left, double Top, double Width, double Height, bool Maximized);
    private static string PlacementPath => Path.Combine(SecurityPaths.Root, "window.json");
    private void RestorePlacement()
    {
        try {
            if (!File.Exists(PlacementPath)) return;
            var saved = JsonSerializer.Deserialize<Placement>(File.ReadAllText(PlacementPath));
            if (saved is null || saved.Width < MinWidth || saved.Height < MinHeight) return;
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var bounds = new Rect(saved.Left, saved.Top, saved.Width, saved.Height);
            // Only restore a position that is still on a connected screen.
            var overlap = Rect.Intersect(screen, bounds);
            if (overlap.IsEmpty || overlap.Width < 200 || overlap.Height < 120) return;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = saved.Left; Top = saved.Top; Width = saved.Width; Height = saved.Height;
            if (saved.Maximized) WindowState = WindowState.Maximized;
        } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }
    private void SavePlacement()
    {
        try {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            File.WriteAllText(PlacementPath, JsonSerializer.Serialize(new Placement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized)));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
