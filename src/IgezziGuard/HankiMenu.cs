namespace IgezziGuard;

/// <summary>Dark drop-down menus for "More" buttons: overflowing views and actions, and report options.</summary>
internal static class HankiMenu
{
    internal static ContextMenuStrip Create(bool checks = false)
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = checks, Font = new Font("Segoe UI", 10.5f), Padding = new Padding(4) };
        if (!SystemInformation.HighContrast) { menu.Renderer = new Renderer(); menu.BackColor = HankiTheme.Surface; menu.ForeColor = HankiTheme.Text; }
        return menu;
    }
    internal static ToolStripMenuItem Item(string text, Action click, bool enabled = true)
    {
        var item = new ToolStripMenuItem(text) { Enabled = enabled, Padding = new Padding(4, 6, 12, 6) };
        item.Click += (_, _) => click();
        return item;
    }
    /// <summary>Opens the menu under a button, aligned to its left edge.</summary>
    internal static void ShowBelow(Control anchor, ContextMenuStrip menu) => menu.Show(anchor, new Point(0, anchor.Height + (int)(4 * anchor.DeviceDpi / 96f)));

    private sealed class Renderer() : ToolStripProfessionalRenderer(new Palette())
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? HankiTheme.Text : Color.FromArgb(110, 120, 134);
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = HankiTheme.Muted; base.OnRenderArrow(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = e.ImageRectangle; float s = e.Item.Owner?.DeviceDpi / 96f ?? 1;
            using var pen = new Pen(HankiTheme.Accent, 2 * s) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            g.DrawLines(pen, [new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.55f), new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.75f), new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.3f)]);
        }
    }
    private sealed class Palette : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => HankiTheme.Surface;
        public override Color MenuBorder => HankiTheme.Border;
        public override Color MenuItemBorder => HankiTheme.Raised;
        public override Color MenuItemSelected => HankiTheme.Raised;
        public override Color MenuItemSelectedGradientBegin => HankiTheme.Raised;
        public override Color MenuItemSelectedGradientEnd => HankiTheme.Raised;
        public override Color MenuItemPressedGradientBegin => HankiTheme.Raised;
        public override Color MenuItemPressedGradientEnd => HankiTheme.Raised;
        public override Color ImageMarginGradientBegin => HankiTheme.Surface;
        public override Color ImageMarginGradientMiddle => HankiTheme.Surface;
        public override Color ImageMarginGradientEnd => HankiTheme.Surface;
        public override Color CheckBackground => HankiTheme.Surface;
        public override Color CheckSelectedBackground => HankiTheme.Raised;
        public override Color CheckPressedBackground => HankiTheme.Raised;
        public override Color SeparatorDark => HankiTheme.Border;
        public override Color SeparatorLight => HankiTheme.Surface;
    }
}

/// <summary>
/// Keeps a row of views or actions on one line: what doesn't fit moves into a "More" menu, in order. Fixed items
/// (the primary action, drop-downs, labels) and the selected view always stay visible.
/// </summary>
internal static class Overflow
{
    internal static HankiButton Attach(FlowLayoutPanel row, Func<HankiButton, bool> foldable, Func<HankiButton, bool>? pinned = null, string label = "More",
        HankiButtonStyle style = HankiButtonStyle.Tab, Font? font = null)
    {
        var more = new HankiButton { Text = label + "  ▾", AutoSize = true, Appearance = style, AccessibleName = label, Folded = true, Margin = new Padding(0, 0, 8, 0) };
        if (font is not null) more.Font = font;
        more.Click += (_, _) => {
            var menu = HankiMenu.Create();
            foreach (var hidden in row.Controls.OfType<HankiButton>().Where(b => b != more && b.Wanted && b.Folded))
                menu.Items.Add(HankiMenu.Item(hidden.Text, hidden.Invoke, hidden.Enabled));
            menu.Closed += (_, _) => row.BeginInvoke(menu.Dispose);
            HankiMenu.ShowBelow(more, menu);
        };
        row.Controls.Add(more);
        bool fitting = false;
        void Fit() {
            if (fitting || row.IsDisposed) return;
            fitting = true;
            try {
                int Width(Control c) => c.GetPreferredSize(Size.Empty).Width + c.Margin.Horizontal;
                var items = row.Controls.Cast<Control>().Where(c => c != more && (c is not HankiButton b || b.Wanted)).ToList();
                var candidates = items.OfType<HankiButton>().Where(foldable).ToList();
                foreach (var b in candidates) b.Folded = false;
                int available = row.ClientSize.Width - row.Padding.Horizontal;
                if (available <= 0 || items.Sum(Width) <= available) { more.Folded = true; return; }
                int room = available - Width(more) - items.Except(candidates).Sum(Width);
                var keep = candidates.Where(b => pinned?.Invoke(b) == true).ToHashSet();
                room -= keep.Sum(Width);
                foreach (var b in candidates) {
                    if (keep.Contains(b)) continue;
                    if (Width(b) > room) break;
                    keep.Add(b); room -= Width(b);
                }
                foreach (var b in candidates) b.Folded = !keep.Contains(b);
                more.Folded = candidates.All(keep.Contains);
            } finally { fitting = false; }
        }
        row.SizeChanged += (_, _) => Fit();
        row.ControlAdded += (_, e) => { if (e.Control != more) { row.Controls.SetChildIndex(more, row.Controls.Count - 1); Fit(); } };
        row.VisibleChanged += (_, _) => { if (row.Visible) Fit(); };
        more.Tag = (Action)Fit;
        return more;
    }
    /// <summary>Re-fits a row after its items changed (text, visibility or selection).</summary>
    internal static void Refit(HankiButton more) => (more.Tag as Action)?.Invoke();
}
