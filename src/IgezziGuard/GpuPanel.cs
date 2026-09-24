using System.Text;
namespace IgezziGuard;

/// <summary>Performance → GPU: graphics hardware, drivers, displays and the vendor interfaces Hanki can use (HANKI-GPU-101/102/103). Read-only.</summary>
public sealed class GpuPanel : ToolPage
{
    protected override bool ReadOnlyTool => true;
    public GpuPanel() : base("Your graphics hardware, driver versions, video memory and displays, and which vendor interfaces Hanki can use on this PC. Reading this changes nothing.")
    {
        Button("Show graphics details", async () => await Run(async token => await Task.Run(Details, token)));
        Button("NVIDIA settings (technical)", async () => await Run(async token => await Task.Run(() => {
            try { return Nvidia.TechnicalReport([]); } catch (NvidiaException ex) { return ex.Message; }
        }, token)));
        Button("Windows Graphics settings", () => HealthSettings.Open(this, "ms-settings:display-advancedgraphics", "Settings → System → Display → Graphics"));
    }

    private static Diagnosis Details()
    {
        var graphics = GraphicsProbe.Collect();
        var cards = new List<ResultCard>();
        foreach (var a in graphics.Adapters.OrderBy(a => a.PreferenceRank))
            cards.Add(new(a.Name, $"{GraphicsFacts.VendorName(a.Vendor)} · {(a.LikelyIntegrated ? "integrated graphics" : "dedicated graphics")} · {GraphicsFacts.Memory(a.DedicatedMemory)} video memory\r\n" +
                $"Driver {GraphicsFacts.DriverVersion(a.Vendor, a.DriverVersion)}{(a.DriverDate is { } d ? $", released {d:d}" : "")}" +
                (a.Bus is { } bus && !a.LikelyIntegrated && GpuBus.Describe(bus) is { Length: > 0 } link ? "\r\nConnection: " + link : "") +
                (graphics.Adapters.Count > 1 && a.PreferenceRank == 0 ? "\r\nWindows gives demanding apps this GPU first." : ""), CardStatus.Info));
        foreach (var d in graphics.Displays) {
            double best = GraphicsFacts.MaxRefreshAtCurrentResolution(d);
            var faster = GraphicsFacts.FasterRefresh(d);
            cards.Add(new(d.Name, $"{GraphicsFacts.Describe(d.Current)} on {graphics.AdapterFor(d)?.Name ?? "an unknown GPU"}, via {d.Connection}\r\n" +
                $"Fastest at this resolution: {best:0} Hz{(d.HdrSupported == true ? $" · HDR {(d.HdrEnabled == true ? "on" : "available, off")}" : "")}" +
                (faster is not null ? "\r\nIt's running below its fastest refresh rate; Gaming can switch it." : ""), faster is null ? CardStatus.Good : CardStatus.Review));
        }
        cards.Add(graphics.NvapiAvailable
            ? new("NVIDIA driver interface", "Available. Hanki reads NVIDIA settings and can change them for one game (Gaming → Games) or for all games with presets and single settings (Gaming → NVIDIA), after you review each change.", CardStatus.Good)
            : new("NVIDIA driver interface", graphics.Adapters.Any(a => a.Vendor == GpuVendor.Nvidia) ? "Not available: the NVIDIA driver may be missing or damaged." : "No NVIDIA graphics on this PC.", CardStatus.Info));
        if (graphics.Adapters.Any(a => a.Vendor == GpuVendor.Amd))
            cards.Add(new("AMD driver interface", graphics.AdlxPresent ? "The Radeon driver interface (ADLX) is installed. Hanki reads and changes Radeon settings in Gaming → AMD Radeon and in Tune my PC, after you review each change. Not yet tested on AMD hardware; please report problems." :
                "Not found; install the AMD Software driver package for Radeon settings.", CardStatus.Info));
        if (graphics.Adapters.Count == 0) cards.Add(new("Graphics hardware", "Windows didn't report any graphics adapters. " + string.Join(" ", graphics.Notes), CardStatus.Unknown));

        var report = new StringBuilder("Graphics hardware • " + DateTimeOffset.Now.ToString("g") + "\r\nRead-only; nothing was changed.\r\n\r\n");
        foreach (var a in graphics.Adapters)
            report.AppendLine($"{a.Name}\r\n  Vendor {GraphicsFacts.VendorName(a.Vendor)} (PCI {a.VendorId:X4}:{a.DeviceId:X4}), {(a.LikelyIntegrated ? "integrated" : "dedicated")}\r\n  Dedicated memory {GraphicsFacts.Memory(a.DedicatedMemory)}, shared {GraphicsFacts.Memory(a.SharedMemory)}\r\n  Driver {GraphicsFacts.DriverVersion(a.Vendor, a.DriverVersion)} {a.DriverDate:d}{(a.Bus is { } linkFacts ? "\r\n  Connection: " + GpuBus.Describe(linkFacts) : "")}\r\n  Windows high-performance order: {a.PreferenceRank + 1}");
        foreach (var d in graphics.Displays)
            report.AppendLine($"{d.Name} ({d.Device})\r\n  {GraphicsFacts.Describe(d.Current)} ({d.Current.RefreshHz:0.###} Hz), {d.Connection}, {(d.Primary ? "main display" : "extra display")}\r\n  Refresh rates at this resolution: " +
                string.Join(", ", d.Supported.Where(m => m.Width == d.Current.Width && m.Height == d.Current.Height).Select(m => $"{m.RefreshHz:0}").Distinct()) + " Hz");
        report.AppendLine($"PC type: {(graphics.Portable == true ? "laptop or tablet" : graphics.Portable == false ? "desktop" : "unknown")}; {(graphics.OnAcPower ? "plugged in" : "on battery")}");
        report.AppendLine("Adaptive sync (G-SYNC, FreeSync): Windows doesn't report it reliably, so Hanki doesn't claim it either way.");
        foreach (var note in graphics.Notes) report.AppendLine("Note: " + note);
        var headline = graphics.Adapters.OrderBy(a => a.PreferenceRank).FirstOrDefault() is { } gpu ? gpu.Name : "Graphics hardware";
        return Diagnosis.From(report.ToString(), cards, headline);
    }
}
