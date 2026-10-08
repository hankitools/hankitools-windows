using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace IgezziGuard;

internal static class PerformanceSnapshot
{
    public static string Collect(CancellationToken token)
    {
        var report = new StringBuilder($"HANKI PERFORMANCE — snapshot {DateTimeOffset.Now:g}\r\nRead-only; values change as workloads run. Refresh for another sample.\r\n\r\n");
        var p = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        if (GetPerformanceInfo(out p, (uint)Marshal.SizeOf<PerformanceInformation>()))
        {
            ulong Bytes(UIntPtr pages) => checked(pages.ToUInt64() * p.PageSize.ToUInt64());
            report.AppendLine($"Usable physical RAM: {Format(Bytes(p.PhysicalTotal))}");
            report.AppendLine($"Available RAM: {Format(Bytes(p.PhysicalAvailable))}");
            report.AppendLine($"Committed memory: {Format(Bytes(p.CommitTotal))} / limit {Format(Bytes(p.CommitLimit))}");
            report.AppendLine($"Peak system commit since boot: {Format(Bytes(p.CommitPeak))}");
            report.AppendLine(ReviewParsing.CommitGuidance(Bytes(p.CommitTotal), Bytes(p.CommitLimit)));
            report.AppendLine("Commit is memory Windows has promised to back; it is NOT pagefile disk usage. Available RAM includes reusable memory.");
            report.AppendLine("\r\n" + PagefileAdvisor.Explain(Bytes(p.CommitTotal), Bytes(p.CommitLimit), Bytes(p.CommitPeak), Bytes(p.PhysicalAvailable), Bytes(p.PhysicalTotal)));
        }
        else report.AppendLine("Memory counters unavailable: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        token.ThrowIfCancellationRequested();
        report.AppendLine("\r\nACTIVE PAGEFILES — allocation and usage reported by Windows");
        int pagefiles = 0; string? callbackError = null;
        PagefileCallback callback = (IntPtr context, ref PagefileInformation data, string path) => {
            try {
                var page = (ulong)Environment.SystemPageSize;
                report.AppendLine($"{path}: allocated {Format(checked(data.Total.ToUInt64() * page))}, in use {Format(checked(data.Used.ToUInt64() * page))}, peak in use {Format(checked(data.Peak.ToUInt64() * page))}");
                pagefiles++; return true;
            }
            catch (Exception ex) { callbackError = ex.Message; return false; }
        };
        if (!EnumPageFiles(callback, IntPtr.Zero)) report.AppendLine("Pagefile enumeration unavailable/incomplete: " + (callbackError ?? new Win32Exception(Marshal.GetLastWin32Error()).Message));
        else if (pagefiles == 0) report.AppendLine("Windows reported no active pagefiles. This alone does not establish crash-dump readiness.");
        GC.KeepAlive(callback);
        token.ThrowIfCancellationRequested();
        report.AppendLine("\r\nCONFIGURED PAGEFILE ENTRIES — registry read, not necessarily active yet");
        try {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
            using var memory = machine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", false);
            if (memory?.GetValue("PagingFiles", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string[] configured && configured.Length > 0)
                foreach (var raw in configured) report.AppendLine(ReviewParsing.PagefileSetting(raw));
            else report.AppendLine("No readable configured entries; do not infer enabled/disabled from this alone.");
            report.AppendLine("A '0 0' entry indicates system-managed sizing for that entry, not confirmation of the global automatic-management checkbox. Pending changes may require restart.");
            using var crash = machine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl", false);
            report.AppendLine("CrashDumpEnabled setting: " + (crash?.GetValue("CrashDumpEnabled") is int mode ? mode switch {
                0 => "None", 1 => "Complete (additional settings may alter filtering)", 2 => "Kernel", 3 => "Small", 7 => "Automatic", _ => "Other: " + mode
            } : "Unknown"));
            report.AppendLine("Dump file location/size and dedicated dump configuration have NOT been validated.");
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { report.AppendLine("Settings unavailable: " + ex.Message); }
        token.ThrowIfCancellationRequested();
        report.AppendLine("\r\nLOCAL FIXED-DRIVE FREE SPACE — available to this user");
        foreach (var drive in DriveInfo.GetDrives())
        {
            token.ThrowIfCancellationRequested();
            try { if (drive.DriveType == DriveType.Fixed && drive.IsReady) report.AppendLine($"{drive.Name}: {Format((ulong)drive.AvailableFreeSpace)} free / {Format((ulong)drive.TotalSize)} total"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { report.AppendLine($"{drive.Name}: unavailable ({ex.Message})"); }
        }
        report.AppendLine("\r\nLARGEST PROCESS WORKING SETS — one sample, not CPU or sustained-load analysis");
        var entries = new List<(string Name, int Id, long Bytes)>(); int skipped = 0;
        var processes = Process.GetProcesses();
        try {
            foreach (var process in processes) {
                token.ThrowIfCancellationRequested();
                try { entries.Add((process.ProcessName, process.Id, process.WorkingSet64)); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { skipped++; }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        foreach (var item in entries.OrderByDescending(i => i.Bytes).Take(20)) report.AppendLine($"{item.Name} (PID {item.Id}): {Format((ulong)Math.Max(0, item.Bytes))}");
        report.AppendLine($"{skipped} processes unavailable. Shared pages can appear in multiple working sets; do not add them to estimate system usage. No process termination is offered.");
        return report.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    private static string Format(ulong bytes) => $"{bytes / (1024d * 1024 * 1024):0.##} GiB";
    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation {
        public uint Size;
        public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PagefileInformation { public uint Size, Reserved; public UIntPtr Total, Used, Peak; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool PagefileCallback(IntPtr context, ref PagefileInformation info, [MarshalAs(UnmanagedType.LPWStr)] string path);
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(out PerformanceInformation info, uint size);
    [DllImport("psapi.dll", EntryPoint = "EnumPageFilesW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumPageFiles(PagefileCallback callback, IntPtr context);
}
