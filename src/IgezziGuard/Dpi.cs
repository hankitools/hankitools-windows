using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace IgezziGuard;

/// <summary>
/// Display scaling. Hanki is system-DPI aware, and every size in the code is written for 100% (96 DPI). Windows already
/// draws text at the display's scale; <see cref="Scale"/> scales the layout to match, once per control: the main window
/// when it's built, each dialog when it's themed, and anything added later to a scaled window when it's added.
/// </summary>
internal static class Dpi
{
    private static readonly ConditionalWeakTable<Control, object> Scaled = new();
    private static readonly ConditionalWeakTable<Font, object> Zoomed = new();

    /// <summary>Windows' display scale when Hanki started: 1.5 at 150%. Windows stretches the window on displays set differently.</summary>
    private static readonly float Display = ReadDisplay();
    /// <summary>
    /// Extra scale for the UI check's screenshots, which run at 100% (HANKI_UI_SCALE): it enlarges text as well, as
    /// Windows does at that scale. 1 otherwise.
    /// </summary>
    internal static float TextZoom { get; } = ReadZoom();
    /// <summary>Device pixels per 100% pixel.</summary>
    internal static float Factor { get; } = Display * TextZoom;

    private static float ReadDisplay()
    {
        try { using var g = Graphics.FromHwnd(IntPtr.Zero); return Math.Clamp(g.DpiX / 96f, 1f, 4f); }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or ArgumentException) { return 1f; }
    }
    private static float ReadZoom() =>
        float.TryParse(Environment.GetEnvironmentVariable("HANKI_UI_SCALE"), NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom) && zoom is >= 1 and <= 3 ? zoom : 1f;

    /// <summary>A 100% size in device pixels.</summary>
    internal static int Px(int logical) => (int)Math.Round(logical * Factor);

    /// <summary>A font for custom painting. Point sizes follow the display by themselves; this adds the UI check's zoom.</summary>
    internal static Font PaintFont(string family, float points, FontStyle style = FontStyle.Regular)
    {
        var font = new Font(family, points * TextZoom, style);
        if (TextZoom != 1f) Zoomed.Add(font, true);
        return font;
    }

    /// <summary>
    /// Scales what hasn't been scaled yet: a whole top-level window, or new controls inside a scaled one. Controls that
    /// aren't in a scaled window yet are left for the window's own pass.
    /// </summary>
    internal static void Scale(Control root)
    {
        if (Factor == 1f) return;
        if (Scaled.TryGetValue(root, out _)) { foreach (Control child in root.Controls) ScaleNew(child); return; }
        if (root is Form { TopLevel: true } || InScaledWindow(root)) Apply(root);
    }
    private static bool InScaledWindow(Control control)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
            if (Scaled.TryGetValue(parent, out _)) return true;
        return false;
    }
    private static void ScaleNew(Control control)
    {
        if (!Scaled.TryGetValue(control, out _)) { Apply(control); return; }
        foreach (Control child in control.Controls) ScaleNew(child);
    }
    private static void Apply(Control root)
    {
        root.Scale(new SizeF(Factor, Factor));
        // List columns keep their widths through Scale.
        foreach (var list in Descendants(root).OfType<ListView>())
            foreach (ColumnHeader column in list.Columns) column.Width = (int)Math.Round(column.Width * Factor);
        if (TextZoom != 1f) ZoomFonts(root);
        foreach (var control in Descendants(root)) {
            if (Scaled.TryGetValue(control, out _)) continue;
            Scaled.Add(control, true);
            // Content built later (result cards, search results, tabs) is scaled as it's added.
            control.ControlAdded += (_, e) => { if (e.Control is { } added && !Scaled.TryGetValue(added, out _)) Apply(added); };
        }
    }
    /// <summary>Fonts set on a control get the UI check's zoom once; inherited fonts follow their parent.</summary>
    private static void ZoomFonts(Control root)
    {
        foreach (var control in Descendants(root)) {
            var font = control.Font;
            if (Zoomed.TryGetValue(font, out _) || control.Parent is { } parent && ReferenceEquals(font, parent.Font)) continue;
            var zoomed = new Font(font.FontFamily, font.Size * TextZoom, font.Style, font.Unit);
            Zoomed.Add(zoomed, true);
            control.Font = zoomed;
        }
    }
    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (var d in Descendants(child)) yield return d;
    }
}
