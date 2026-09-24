using System.Runtime.InteropServices;
namespace IgezziGuard;

/// <summary>
/// Measures the PC once a second through Windows performance counters (PDH, English counter names so it works in
/// any Windows language) and the display kernel's adapter data for GPU temperature and clock. Read-only; nothing is
/// changed and nothing leaves the PC. Values Windows doesn't provide stay null.
/// </summary>
internal sealed class SystemMonitor : IDisposable
{
    private IntPtr query;
    private readonly Dictionary<string, IntPtr> counters = new();
    private readonly List<string> notes = [];
    private readonly int logicalProcessors = Environment.ProcessorCount;
    private uint gpuHandle;

    internal IReadOnlyList<string> Notes => notes;

    internal SystemMonitor(long? gpuLuid)
    {
        Check(PdhOpenQueryW(null, IntPtr.Zero, out query), "Performance counters are unavailable");
        foreach (var (name, path) in new[] {
            ("cores", @"\Processor Information(*)\% Processor Time"), ("performance", @"\Processor Information(_Total)\% Processor Performance"),
            ("frequency", @"\Processor Information(_Total)\Processor Frequency"), ("available", @"\Memory\Available MBytes"),
            ("commit", @"\Memory\% Committed Bytes In Use"), ("faults", @"\Memory\Pages Input/sec"), ("idle", @"\PhysicalDisk(*)\% Idle Time"),
            ("latency", @"\PhysicalDisk(*)\Avg. Disk sec/Transfer"), ("read", @"\PhysicalDisk(_Total)\Disk Read Bytes/sec"), ("write", @"\PhysicalDisk(_Total)\Disk Write Bytes/sec"),
            ("gpu", @"\GPU Engine(*)\Utilization Percentage"), ("vram", @"\GPU Adapter Memory(*)\Dedicated Usage"),
            ("pcpu", @"\Process V2(*)\% Processor Time"), ("pio", @"\Process V2(*)\IO Data Bytes/sec"), ("pmem", @"\Process V2(*)\Private Bytes") }) {
            // "Process V2" names each instance "name:pid", so two processes with the same name stay apart.
            if (PdhAddEnglishCounterW(query, path, IntPtr.Zero, out var counter) == 0) counters[name] = counter;
            else if (name.StartsWith('p')) { if (!notes.Contains("Per-program activity isn't reported on this version of Windows.")) notes.Add("Per-program activity isn't reported on this version of Windows."); }
            else if (name is "gpu" or "vram") { if (!notes.Contains("GPU load isn't reported on this PC.")) notes.Add("GPU load isn't reported on this PC."); }
            else notes.Add($"The {name} counter isn't available.");
        }
        PdhCollectQueryData(query); // Rate counters need a first reading.
        if (gpuLuid is { } luid) {
            var open = new OpenFromLuid { LuidLow = (uint)(luid & 0xFFFFFFFF), LuidHigh = (int)(luid >> 32) };
            if (D3DKMTOpenAdapterFromLuid(ref open) == 0) gpuHandle = open.Adapter;
        }
    }

    /// <summary>Takes one sample; call about once a second. The first call after construction covers the time since it.</summary>
    internal MonitorSample Sample(int? targetPid)
    {
        PdhCollectQueryData(query);
        var cores = Instances("cores").Where(i => i.Name.Contains(',') && !i.Name.Contains("_Total")).Select(i => Math.Clamp(i.Value, 0, 100)).ToArray();
        double total = Instances("cores").Where(i => i.Name == "_Total").Select(i => i.Value).DefaultIfEmpty(cores.Length > 0 ? cores.Average() : 0).First();
        double? performance = Single("performance"), frequency = Single("frequency");
        var disks = Instances("idle").Where(i => i.Name != "_Total").ToArray();
        double diskActive = disks.Length == 0 ? 0 : disks.Max(i => Math.Clamp(100 - i.Value, 0, 100));
        var latencies = Instances("latency").Where(i => i.Name != "_Total").Select(i => i.Value * 1000).ToArray();

        // GPU engines: sum each engine across processes, then take the busiest engine, as Task Manager does.
        double? gpuBusy = null, targetGpu = null;
        if (counters.ContainsKey("gpu")) {
            var engines = Instances("gpu").Select(i => (Pid: Pid(i.Name), Engine: Engine(i.Name), i.Value)).Where(e => e.Engine is not null).ToArray();
            if (engines.Length > 0) {
                gpuBusy = Math.Min(100, engines.GroupBy(e => e.Engine).Max(g => g.Sum(e => e.Value)));
                if (targetPid is { } pid) targetGpu = Math.Min(100, engines.Where(e => e.Pid == pid).GroupBy(e => e.Engine).Select(g => g.Sum(e => e.Value)).DefaultIfEmpty(0).Max());
            }
        }
        double? vram = counters.ContainsKey("vram") ? Instances("vram").Select(i => i.Value).DefaultIfEmpty(-1).Max() is var v && v >= 0 ? v / (1024 * 1024) : null : null;
        var (temperature, clock) = GpuSensors();
        return new MonitorSample(DateTimeOffset.UtcNow, Math.Clamp(total, 0, 100), cores, performance, performance is { } p && frequency is { } f ? f * p / 100 : null,
            Single("available") ?? 0, Single("commit") ?? 0, Single("faults") ?? 0, diskActive, latencies.Length > 0 ? latencies.Max() : null,
            (Single("read") ?? 0) / (1024 * 1024), (Single("write") ?? 0) / (1024 * 1024), gpuBusy, vram, temperature, clock, targetGpu, Processes());
    }

    private IReadOnlyList<ProcessActivity> Processes()
    {
        if (!counters.ContainsKey("pcpu")) return [];
        static Dictionary<string, double> ByName(IEnumerable<(string Name, double Value)> items) =>
            items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
        var io = ByName(Instances("pio")); var memory = ByName(Instances("pmem"));
        return Instances("pcpu").Select(i => (Instance: i, Split: i.Name.LastIndexOf(':')))
            .Where(x => x.Split > 0 && int.TryParse(x.Instance.Name[(x.Split + 1)..], out var pid) && pid > 0)
            .Select(x => new ProcessActivity(int.Parse(x.Instance.Name[(x.Split + 1)..]), x.Instance.Name[..x.Split], x.Instance.Value / logicalProcessors,
                io.GetValueOrDefault(x.Instance.Name) / (1024 * 1024), memory.GetValueOrDefault(x.Instance.Name) / (1024 * 1024)))
            .OrderByDescending(p => p.CpuPercent + p.IoMBps / 5).Take(6).ToArray();
    }
    private static int? Pid(string instance) => instance.StartsWith("pid_", StringComparison.Ordinal) && int.TryParse(instance[4..instance.IndexOf('_', 4)], out var pid) ? pid : null;
    private static string? Engine(string instance) { int i = instance.IndexOf("_luid_", StringComparison.Ordinal); return i < 0 ? null : instance[(i + 1)..]; }

    // ---- GPU temperature and clock from the display kernel (WDDM 2.4 and later) ------------------------------
    private (double? Temperature, double? ClockMhz) GpuSensors()
    {
        if (gpuHandle == 0) return (null, null);
        double? temperature = null, clock = null;
        var perf = new AdapterPerf();
        if (Query(62, ref perf) && perf.Temperature is > 0 and < 1500) temperature = perf.Temperature / 10.0;
        var node = new NodePerf { NodeOrdinal = 0 };
        if (Query(61, ref node) && node.Frequency > 0) clock = node.Frequency / 1_000_000.0;
        return (temperature, clock);
    }
    internal double? GpuMaxClockMhz()
    {
        var node = new NodePerf { NodeOrdinal = 0 };
        return gpuHandle != 0 && Query(61, ref node) && node.MaxFrequency > 0 ? node.MaxFrequency / 1_000_000.0 : null;
    }
    private bool Query<T>(int type, ref T data) where T : struct
    {
        int size = Marshal.SizeOf<T>(); var buffer = Marshal.AllocHGlobal(size);
        try {
            Marshal.StructureToPtr(data, buffer, false);
            var info = new QueryInfo { Adapter = gpuHandle, Type = type, Data = buffer, Size = (uint)size };
            if (D3DKMTQueryAdapterInfo(ref info) != 0) return false;
            data = Marshal.PtrToStructure<T>(buffer); return true;
        } finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        if (query != IntPtr.Zero) { PdhCloseQuery(query); query = IntPtr.Zero; }
        if (gpuHandle != 0) { var close = new CloseAdapter { Adapter = gpuHandle }; D3DKMTCloseAdapter(ref close); gpuHandle = 0; }
    }

    // ---- PDH -------------------------------------------------------------------------------------------------
    private double? Single(string name)
    {
        if (!counters.TryGetValue(name, out var counter)) return null;
        return PdhGetFormattedCounterValue(counter, Double | NoCap, out _, out var value) == 0 && value.Status is 0 or 1 ? value.Value : null;
    }
    private IReadOnlyList<(string Name, double Value)> Instances(string name)
    {
        if (!counters.TryGetValue(name, out var counter)) return [];
        uint size = 0, count;
        uint status = PdhGetFormattedCounterArrayW(counter, Double | NoCap, ref size, out count, IntPtr.Zero);
        if (status != MoreData || size == 0) return [];
        var buffer = Marshal.AllocHGlobal((int)size);
        try {
            if (PdhGetFormattedCounterArrayW(counter, Double | NoCap, ref size, out count, buffer) != 0) return [];
            var result = new List<(string, double)>((int)count);
            int stride = IntPtr.Size + 16;
            for (int i = 0; i < count; i++) {
                var item = buffer + i * stride;
                if (Marshal.ReadInt32(item, IntPtr.Size) is not (0 or 1)) continue;
                result.Add((Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "", BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, IntPtr.Size + 8))));
            }
            return result;
        } finally { Marshal.FreeHGlobal(buffer); }
    }
    private static void Check(uint status, string message) { if (status != 0) throw new IOException($"{message} (PDH 0x{status:X8})."); }

    private const uint Double = 0x00000200, NoCap = 0x00008000, MoreData = 0x800007D2;
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue { public uint Status; public double Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);

    [StructLayout(LayoutKind.Sequential)] private struct OpenFromLuid { public uint LuidLow; public int LuidHigh; public uint Adapter; }
    [StructLayout(LayoutKind.Sequential)] private struct QueryInfo { public uint Adapter; public int Type; public IntPtr Data; public uint Size; }
    [StructLayout(LayoutKind.Sequential)] private struct CloseAdapter { public uint Adapter; }
    [StructLayout(LayoutKind.Sequential)] private struct AdapterPerf { public uint PhysicalAdapterIndex; public ulong MemoryFrequency, MaxMemoryFrequency, MaxMemoryFrequencyOC, MemoryBandwidth, PcieBandwidth; public uint FanRpm, Power, Temperature; public byte PowerStateOverride; }
    [StructLayout(LayoutKind.Sequential)] private struct NodePerf { public uint NodeOrdinal, PhysicalAdapterIndex; public ulong Frequency, MaxFrequency, MaxFrequencyOC; public uint Voltage, VoltageMax, VoltageMaxOC; public ulong MaxTransitionLatency; }
    [DllImport("gdi32.dll")] private static extern int D3DKMTOpenAdapterFromLuid(ref OpenFromLuid data);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref QueryInfo info);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref CloseAdapter data);
}
