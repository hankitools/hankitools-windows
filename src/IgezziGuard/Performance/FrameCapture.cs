using System.Diagnostics;
using System.Runtime.InteropServices;
namespace IgezziGuard;

/// <summary>
/// Frame timing for DirectX 10/11/12 games from the Microsoft-Windows-DXGI event provider, the same source PresentMon
/// uses. Each Present call of the target process is one frame. Needs administrator rights (or membership of the
/// Performance Log Users group) to start a real-time trace; without them Hanki measures everything except frames.
/// Vulkan and OpenGL games that don't present through DXGI aren't counted, which the results say.
/// </summary>
internal sealed class FrameCapture : IDisposable
{
    private static readonly Guid DxgiProvider = new("ca11c036-0102-4a2d-a6ad-f03cfed5d3c9");
    private const string SessionName = "Hanki Tools frame capture";
    private const ushort PresentStart = 42, PresentMultiplaneStart = 55;
    private readonly int pid;
    private readonly List<double> presents = [];
    private readonly object gate = new();
    private readonly EventRecordCallback callback;
    private IntPtr properties, loggerName;
    private ulong session, trace;
    private Thread? thread;

    /// <summary>Why frames can't be captured, or null when capture is running.</summary>
    internal string? Unavailable { get; private set; }

    internal FrameCapture(int processId)
    {
        pid = processId; callback = OnEvent;
        try { Start(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { Unavailable = "Frame capture isn't supported on this version of Windows."; }
    }

    private void Start()
    {
        const int PropertiesSize = 120, NameBytes = 1024;
        properties = Marshal.AllocHGlobal(PropertiesSize + NameBytes);
        for (int i = 0; i < PropertiesSize + NameBytes; i += 4) Marshal.WriteInt32(properties, i, 0);
        Marshal.WriteInt32(properties, 0, PropertiesSize + NameBytes);   // Wnode.BufferSize
        Marshal.WriteInt32(properties, 40, 1);                          // Wnode.ClientContext: QueryPerformanceCounter timestamps
        Marshal.WriteInt32(properties, 44, 0x00020000);                 // Wnode.Flags: WNODE_FLAG_TRACED_GUID
        Marshal.WriteInt32(properties, 64, 0x00000100);                 // LogFileMode: EVENT_TRACE_REAL_TIME_MODE
        Marshal.WriteInt32(properties, 116, PropertiesSize);            // LoggerNameOffset
        uint status = StartTraceW(out session, SessionName, properties);
        if (status == 183) { // ERROR_ALREADY_EXISTS: a previous Hanki capture was left running.
            ControlTraceW(0, SessionName, properties, 1);
            status = StartTraceW(out session, SessionName, properties);
        }
        if (status == 5) { Unavailable = "Frame rates need Hanki to run as administrator. Everything else was measured."; return; }
        if (status != 0) { Unavailable = $"Frame capture couldn't start (error {status})."; return; }
        var provider = DxgiProvider;
        status = EnableTraceEx2(session, ref provider, 1, 4, 0, 0, 0, IntPtr.Zero);
        if (status != 0) { Stop(); Unavailable = $"Frame capture couldn't start (error {status})."; return; }

        // EVENT_TRACE_LOGFILEW (448 bytes on 64-bit Windows): logger name, real-time + event-record mode, callback.
        var logfile = Marshal.AllocHGlobal(448);
        for (int i = 0; i < 448; i += 4) Marshal.WriteInt32(logfile, i, 0);
        loggerName = Marshal.StringToHGlobalUni(SessionName);
        Marshal.WriteIntPtr(logfile, 8, loggerName);
        Marshal.WriteInt32(logfile, 28, 0x00000100 | 0x10000000);       // PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD
        Marshal.WriteIntPtr(logfile, 424, Marshal.GetFunctionPointerForDelegate(callback));
        trace = OpenTraceW(logfile);
        Marshal.FreeHGlobal(logfile);
        if (trace == ulong.MaxValue) { Stop(); Unavailable = "Frame capture couldn't open its trace."; return; }
        thread = new Thread(() => ProcessTrace([trace], 1, IntPtr.Zero, IntPtr.Zero)) { IsBackground = true, Name = "Hanki frame capture" };
        thread.Start();
    }

    private void OnEvent(IntPtr record)
    {
        // EVENT_RECORD: EventHeader.ProcessId at 12, TimeStamp at 16, ProviderId at 24, EventDescriptor.Id at 40.
        if ((uint)Marshal.ReadInt32(record, 12) != pid) return;
        ushort id = (ushort)Marshal.ReadInt16(record, 40);
        if (id is not (PresentStart or PresentMultiplaneStart)) return;
        if (Marshal.PtrToStructure<Guid>(record + 24) != DxgiProvider) return;
        double seconds = Marshal.ReadInt64(record, 16) / (double)Stopwatch.Frequency;
        lock (gate) if (presents.Count < 1_000_000) presents.Add(seconds);
    }

    /// <summary>Present timestamps (seconds) collected so far.</summary>
    internal IReadOnlyList<double> Presents() { lock (gate) return presents.ToArray(); }

    private void Stop()
    {
        if (trace != 0 && trace != ulong.MaxValue) { CloseTrace(trace); trace = 0; }
        if (session != 0) { ControlTraceW(session, null, properties, 1); session = 0; }
    }
    public void Dispose()
    {
        Stop();
        thread?.Join(2000);
        if (properties != IntPtr.Zero) { Marshal.FreeHGlobal(properties); properties = IntPtr.Zero; }
        if (loggerName != IntPtr.Zero) { Marshal.FreeHGlobal(loggerName); loggerName = IntPtr.Zero; }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void EventRecordCallback(IntPtr record);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint StartTraceW(out ulong handle, string name, IntPtr properties);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint ControlTraceW(ulong handle, string? name, IntPtr properties, uint code);
    [DllImport("advapi32.dll")] private static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level, ulong any, ulong all, uint timeout, IntPtr parameters);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern ulong OpenTraceW(IntPtr logfile);
    [DllImport("advapi32.dll")] private static extern uint ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
    [DllImport("advapi32.dll")] private static extern uint CloseTrace(ulong handle);
}
