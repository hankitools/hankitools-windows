namespace IgezziGuard;

/// <summary>
/// The legacy WinForms window: sidebar, header, footer and the <see cref="LegacyWorkspace"/> of pages. It is kept as a fallback
/// (<c>--legacy-shell</c>) while the WPF shell replaces it; the pages themselves live in <see cref="LegacyWorkspace"/>.
/// </summary>
public sealed class HankiForm : Form
{
    private readonly LegacyWorkspace workspace;
    private readonly HankiButton cancel = new() { Text = "Cancel", Dock = DockStyle.Bottom, Enabled = false, Visible = false };
    private const string IdleStatus = "Ready — no checks run";
    private readonly Label status = new() { Text = IdleStatus, Dock = DockStyle.Bottom, Height = 32, Tag = "intro" };
    private WorkspacePages tabs => workspace.Tabs;
    /// <summary>Every navigable tool, as listed in Find a tool.</summary>
    internal IReadOnlyList<ToolLauncher.Route> Routes => workspace.Routes;

    internal static int ScaleSidebarDimension(int logical, int dpi) => (int)Math.Round(logical * dpi / 96f);
    internal static int SidebarWidthAtDpi(int dpi) => 192 + ScaleSidebarDimension(64, dpi);

    internal static ContextMenuStrip QuickAccessMenu(Action<string> open)
    {
        var menu = HankiMenu.Create();
        foreach (var (label, command) in new[] {
            ("PowerShell (Admin)", "powershell"), ("CMD (Admin)", "cmd"), ("File Explorer", "explorer"),
            ("Task Manager", "task-manager"), ("Windows Settings", "settings"), ("Event Viewer", "event-viewer")
        })
            menu.Items.Add(HankiMenu.Item(label, () => open(command)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Localizer.T("Admin shortcuts use Windows UAC.")) {
            Enabled = false, Padding = new Padding(4, 6, 12, 6)
        });
        return menu;
    }

    public HankiForm()
    {
        Text = "Hanki Tools • " + AppInfo.Version;
        Size = new Size(1320, 880);
        MinimumSize = new Size(1120, 740);
        Font = new Font("Segoe UI", 10.5f);
        BackColor = HankiTheme.Canvas;
        ForeColor = HankiTheme.Text;
        StartPosition = FormStartPosition.CenterScreen;
        workspace = new LegacyWorkspace(() => this);
        void Navigate(string name) => workspace.Navigate(name);
        // Sidebar: brand and the five areas at the top; quick access, About and the version quietly at the bottom.
        var sidebarHost = new Panel { Tag = "pine", Dock = DockStyle.Left, Width = 240 };
        var sidebar = new FlowLayoutPanel { Tag = "pine", Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            AutoScroll = true, WrapContents = false, Padding = new Padding(12, 6, 12, 12) };
        var sidebarFooter = new FlowLayoutPanel { Tag = "pine", Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12, 0, 12, 12) };
        sidebarHost.Controls.Add(sidebar); sidebarHost.Controls.Add(sidebarFooter);
        sidebar.Controls.Add(new BrandHeader { Margin = new Padding(0, 0, 0, 14) });
        // Five destinations (HANKI-UX-300): each area's landing page opens its other pages as tiles. The item of the
        // current area stays selected on those pages too, so you always know where you are.
        var navigation = new List<(HankiButton Button, ProductArea Area)>();
        foreach (var pageId in Navigation.Sidebar) {
            var item = Navigation.Find(pageId)!;
            var button = new HankiButton { Text = item.Label, Width = 214, Height = 46, Margin = new Padding(0, 2, 0, 2), AccessibleName = "Open " + item.Label,
                IconKind = item.Icon, AreaAccent = HankiTheme.AreaAccent(item.Area), Appearance = HankiButtonStyle.Navigation, Font = new Font("Segoe UI Semibold", 12f) };
            button.Click += (_, _) => Navigate(item.Page);
            navigation.Add((button, item.Area)); sidebar.Controls.Add(button);
        }
        var quick = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false, Margin = Padding.Empty };
        var quickToggle = new HankiButton { Text = "›  Quick access", Width = 214, Height = 34, Appearance = HankiButtonStyle.Navigation,
            Margin = new Padding(0, 0, 0, 2), AccessibleName = "Expand quick access", Font = new Font("Segoe UI", 9.75f) };
        quickToggle.Click += (_, _) => {
            quick.Visible = !quick.Visible; quickToggle.Text = quick.Visible ? "⌄  Quick access" : "›  Quick access";
            quickToggle.AccessibleName = quick.Visible ? "Collapse quick access" : "Expand quick access";
        };
        sidebarFooter.Controls.Add(quick); sidebarFooter.Controls.Add(quickToggle);
        foreach (var item in new[] { ("PowerShell (Admin)", "powershell"), ("CMD (Admin)", "cmd"), ("File Explorer", "explorer"), ("Task Manager", "task-manager"), ("Windows Settings", "settings"), ("Event Viewer", "event-viewer") }) {
            var shortcut = new HankiButton { Text = item.Item1, Width = 204, Height = 30, Appearance = HankiButtonStyle.Navigation, Font = new Font("Segoe UI", 9.25f), Margin = new Padding(10, 0, 0, 0) };
            shortcut.Click += (_, _) => DesktopShortcuts.Open(this, item.Item2); quick.Controls.Add(shortcut);
        }
        quick.Controls.Add(new Label { Text = "Admin shortcuts use Windows UAC.", AutoSize = true, Tag = "intro", Font = new Font("Segoe UI", 8.25f), Margin = new Padding(20, 4, 0, 4) });
        var about = new HankiButton { Text = "About & privacy", Appearance = HankiButtonStyle.Navigation, Width = 214, Height = 34, Font = new Font("Segoe UI", 9.75f), Margin = new Padding(0, 1, 0, 1) };
        about.Click += (_, _) => AboutDialog.Show(this);
        sidebarFooter.Controls.Add(about);
        sidebarFooter.Controls.Add(new Label { Text = "v" + AppInfo.Version + "  ·  hanki.tools", AutoSize = true, Tag = "intro", Font = new Font("Segoe UI", 8.25f), Margin = new Padding(14, 6, 0, 0) });
        var title = new Label { Text = "Overview", Dock = DockStyle.Top, Height = 58, Font = new Font("Segoe UI Semibold", 21f), Padding = new Padding(22, 16, 0, 0), AutoEllipsis = true };
        void FindTool() { using var launcher = new ToolLauncher(Routes.ToList()); launcher.ShowDialog(this); }
        var search = new HankiButton { Text = "Find a tool…", Hint = "Ctrl+K", IconKind = "Search", Appearance = HankiButtonStyle.Field,
            Size = new Size(260, 38), Margin = Padding.Empty, AccessibleName = "Find a tool, Control K", Font = new Font("Segoe UI", 9.75f) };
        search.Click += (_, _) => FindTool();
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.F1) { Navigate("Help & community"); e.SuppressKeyPress = true; } if (e.Control && e.KeyCode == Keys.K) { FindTool(); e.SuppressKeyPress = true; } };
        var header = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(0, 0, 22, 0) };
        var searchHost = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 26, 0, 0), Margin = Padding.Empty };
        searchHost.Controls.Add(search);
        // Above the title: the way back to the area's landing page, on every page that isn't one.
        var crumbs = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, WrapContents = false, Padding = new Padding(16, 8, 0, 0), Margin = Padding.Empty };
        var back = new HankiButton { Text = "←  Back", Appearance = HankiButtonStyle.Quiet, AutoSize = true, Visible = false, Margin = Padding.Empty, Font = new Font("Segoe UI Semibold", 10.5f) };
        string? backTarget = null;
        back.Click += (_, _) => { if (backTarget is not null) Navigate(backTarget); };
        crumbs.Controls.Add(back);
        title.Dock = DockStyle.Fill; title.Padding = new Padding(22, 0, 0, 0);
        header.Controls.Add(title); header.Controls.Add(crumbs); header.Controls.Add(searchHost);
        var introduction = new Label { Dock = DockStyle.Top, AutoSize = false, Padding = new Padding(24, 0, 24, 16),
            Font = new Font("Segoe UI", 11.5f), Tag = "intro", AccessibleName = "About this page" };
        void FitIntroduction() {
            int desired = introduction.GetPreferredSize(new Size(Math.Max(120, introduction.Width), 0)).Height;
            if (introduction.Height != desired) introduction.Height = desired;
        }
        introduction.SizeChanged += (_, _) => FitIntroduction();
        var content = new Panel { Dock = DockStyle.Fill }; content.Controls.Add(workspace); content.Controls.Add(introduction); content.Controls.Add(header);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(16, 5, 12, 5) };
        footer.Paint += (_, e) => { if (!SystemInformation.HighContrast) { using var edge = new Pen(HankiTheme.Border); e.Graphics.DrawLine(edge, 0, 0, footer.Width, 0); } };
        status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.Font = new Font("Segoe UI", 9.25f);
        cancel.Dock = DockStyle.Right; cancel.Width = 110; cancel.Text = "Cancel task"; cancel.Font = new Font("Segoe UI", 9.25f);
        footer.Controls.Add(status); footer.Controls.Add(cancel);
        footer.Visible = false;
        void RefreshNavigation() {
            // Before the handle exists SelectedTab can be null although Home is shown.
            var current = tabs.SelectedTab ?? (tabs.TabCount > 0 ? tabs.TabPages[0] : null);
            var destination = current is null ? null : Navigation.Find(current.Text);
            var productArea = destination?.Area ?? ProductArea.Home;
            foreach (var item in navigation) item.Button.Selected = item.Area == productArea;
            title.Text = destination is null ? current?.Text ?? "" : Navigation.Title(destination);
            bool landing = destination is null || Navigation.IsLanding(destination.Page);
            backTarget = landing ? null : Navigation.Landing(productArea);
            back.Visible = backTarget is not null;
            if (backTarget is not null) { back.Text = "←  " + Navigation.Title(Navigation.Find(backTarget)!); back.AccessibleName = "Back to " + Navigation.Title(Navigation.Find(backTarget)!); }
            // Pages with their own hero don't repeat an introduction.
            introduction.Text = destination is null || destination.Page is "Home" or "System overview" or "Performance overview" ? "" : destination.Introduction;
            introduction.Visible = introduction.Text.Length > 0;
            FitIntroduction();
        }
        tabs.SelectedIndexChanged += (_, _) => RefreshNavigation(); RefreshNavigation();
        Shown += (_, _) => RefreshNavigation();
        // Contacts Polar only when a Technician licence is due for its weekly check; offline, the stored licence keeps working.
        Shown += async (_, _) => { try { await AppLicensing.RefreshAsync(CancellationToken.None); } catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException) { } };
        Controls.Add(content); Controls.Add(sidebarHost); Controls.Add(footer);
        var iconStream = typeof(HankiForm).Assembly.GetManifestResourceStream("IgezziGuard.Brand.hanki.ico");
        if (iconStream is not null) { using (iconStream) { using var branded = new Icon(iconStream); Icon = (Icon)branded.Clone(); } }
        HankiTheme.Apply(this);
        workspace.StatusMessage += message => status.Text = message;
        var taskStatus = new Label { Dock = DockStyle.Right, Width = 360, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true, Font = new Font("Segoe UI", 9.25f), Padding = new Padding(0, 0, 10, 0) };
        footer.Controls.Add(taskStatus); footer.Controls.SetChildIndex(taskStatus, 1);
        cancel.Text = "Cancel tasks"; cancel.Width = 120;
        var taskTimer = new System.Windows.Forms.Timer { Interval = 500 };
        taskTimer.Tick += (_, _) => {
            var active = workspace.ActiveTasks(); cancel.Enabled = cancel.Visible = active.Length > 0;
            taskStatus.Text = active.Length == 0 ? "" : "Running: " + string.Join(", ", active);
            // The status bar appears only while something runs or has a message; idle, the page gets the space.
            footer.Visible = active.Length > 0 || status.Text != IdleStatus;
        };
        HankiTheme.Apply(taskStatus);
        taskTimer.Start(); Disposed += (_, _) => taskTimer.Dispose();
        cancel.Click += (_, _) => workspace.CancelTasks();
        FormClosing += (_, e) => { if (workspace.CancelBeforeClose()) { e.Cancel = true; status.Text = "Cancelling; close again when finished."; } };
    }
    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        if (IsHandleCreated && !Disposing) HankiTheme.Apply(this);
    }
}
