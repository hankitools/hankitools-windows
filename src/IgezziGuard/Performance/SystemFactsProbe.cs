using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
namespace IgezziGuard;

/// <summary>Reads processor, memory, pagefile and storage facts (read-only). Drive temperature and wear need administrator rights.</summary>
internal static class SystemFactsProbe
{
    private const string Script = """
        $ErrorActionPreference='SilentlyContinue'
        $p=Get-CimInstance Win32_Processor | Select-Object -First 1
        $mods=@(Get-CimInstance Win32_PhysicalMemory | ForEach-Object { [pscustomobject]@{Slot=[string]$_.DeviceLocator;Bytes=[uint64]$_.Capacity;Rated=[int]$_.Speed;Configured=[int]$_.ConfiguredClockSpeed;Manufacturer=([string]$_.Manufacturer).Trim();Part=([string]$_.PartNumber).Trim();Type=[int]$_.SMBIOSMemoryType} })
        $slots=[int](@(Get-CimInstance Win32_PhysicalMemoryArray) | Measure-Object MemoryDevices -Sum).Sum
        $cs=Get-CimInstance Win32_ComputerSystem
        $pfs=@(Get-CimInstance Win32_PageFileSetting | ForEach-Object { [pscustomobject]@{Path=[string]$_.Name;Initial=[int]$_.InitialSize;Maximum=[int]$_.MaximumSize} })
        $pfu=@(Get-CimInstance Win32_PageFileUsage | ForEach-Object { [pscustomobject]@{Path=[string]$_.Name;Allocated=[int]$_.AllocatedBaseSize;Peak=[int]$_.PeakUsage} })
        $disks=@(Get-PhysicalDisk | ForEach-Object { $r=$null; try { $r=$_ | Get-StorageReliabilityCounter -ErrorAction Stop } catch {}
            [pscustomobject]@{Number=[int]$_.DeviceId;Name=[string]$_.FriendlyName;Media=[string]$_.MediaType;Bus=[string]$_.BusType;Size=[uint64]$_.Size;Health=[string]$_.HealthStatus;
              Temp=$(if($r -and $r.Temperature){[double]$r.Temperature}else{$null});Wear=$(if($r -and $null -ne $r.Wear){[int]$r.Wear}else{$null})} })
        $vols=@(Get-Partition | Where-Object DriveLetter | ForEach-Object { $v=Get-Volume -DriveLetter $_.DriveLetter
            [pscustomobject]@{Letter=[string]$_.DriveLetter;Disk=[int]$_.DiskNumber;FileSystem=[string]$v.FileSystem;Size=[uint64]$v.Size;Free=[uint64]$v.SizeRemaining} })
        $task=Get-ScheduledTask -TaskPath '\Microsoft\Windows\Defrag\' -TaskName 'ScheduledDefrag'; $info=$null; if($task){$info=$task | Get-ScheduledTaskInfo}
        [pscustomobject]@{CpuName=[string]$p.Name;Cores=[int]$p.NumberOfCores;Threads=[int]$p.NumberOfLogicalProcessors;BaseMhz=[int]$p.MaxClockSpeed;Modules=$mods;Slots=$slots;
          Automatic=[bool]$cs.AutomaticManagedPagefile;Settings=$pfs;Usage=$pfu;Disks=$disks;Volumes=$vols;TaskState=$(if($task){[string]$task.State}else{$null});
          LastRun=$(if($info -and $info.LastRunTime -and $info.LastRunTime.Year -ge 2000){$info.LastRunTime.ToUniversalTime().ToString('o')}else{$null})} | ConvertTo-Json -Depth 4 -Compress
        """;
    private sealed record ModuleDto(string? Slot, ulong Bytes, int Rated, int Configured, string? Manufacturer, string? Part, int Type);
    private sealed record PageDto(string? Path, int Initial, int Maximum, int Allocated, int Peak);
    private sealed record DiskDto(int Number, string? Name, string? Media, string? Bus, ulong Size, string? Health, double? Temp, int? Wear);
    private sealed record VolumeDto(string? Letter, int Disk, string? FileSystem, ulong Size, ulong Free);
    private sealed record Dto(string? CpuName, int Cores, int Threads, int BaseMhz, ModuleDto[]? Modules, int Slots, bool Automatic, PageDto[]? Settings, PageDto[]? Usage,
        DiskDto[]? Disks, VolumeDto[]? Volumes, string? TaskState, DateTimeOffset? LastRun);

    internal static async Task<(CpuFacts Cpu, MemoryFacts Memory, StorageFacts Storage)> Collect(CancellationToken token)
    {
        var output = await WindowsCommand.PowerShellCapture(Script, token, 90);
        var dto = JsonSerializer.Deserialize<Dto>(output.StandardOutput, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new IOException("System information unavailable.");
        var cpu = new CpuFacts(dto.CpuName?.Trim() ?? "Processor", dto.Cores, dto.Threads > 0 ? dto.Threads : Environment.ProcessorCount, dto.BaseMhz);

        var info = new PerformanceInfo { Size = (uint)Marshal.SizeOf<PerformanceInfo>() };
        GetPerformanceInfo(out info, info.Size);
        ulong page = info.PageSize.ToUInt64();
        int crash = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl")?.GetValue("CrashDumpEnabled") is int c ? c : 7;
        var pagefile = new PagefileFacts(dto.Automatic, (dto.Settings ?? []).Select(s => (s.Path ?? "", s.Initial, s.Maximum)).ToArray(),
            (dto.Usage ?? []).Select(u => (u.Path ?? "", u.Allocated, u.Peak)).ToArray(), crash);
        var top = new List<(string, ulong)>();
        foreach (var process in Process.GetProcesses()) {
            using (process) { try { top.Add((process.ProcessName, (ulong)process.PrivateMemorySize64)); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
        }
        GetPhysicallyInstalledSystemMemory(out var installedKb);
        var memory = new MemoryFacts(installedKb * 1024, dto.Slots, (dto.Modules ?? []).Select(m => new MemoryModule(m.Slot ?? "", m.Bytes, m.Rated, m.Configured, m.Manufacturer ?? "", m.Part ?? "", m.Type)).ToArray(),
            info.CommitTotal.ToUInt64() * page, info.CommitLimit.ToUInt64() * page, info.CommitPeak.ToUInt64() * page, info.PhysicalAvailable.ToUInt64() * page, pagefile,
            top.GroupBy(t => t.Item1).Select(g => (g.Key, (ulong)g.Sum(x => (double)x.Item2))).OrderByDescending(t => t.Item2).Take(6).ToArray());

        bool? trimDisabled = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem")?.GetValue("DisableDeleteNotification") is int t ? t != 0 : null;
        var notes = new List<string>();
        var disks = (dto.Disks ?? []).Select(d => new DiskFacts(d.Number, d.Name?.Trim() ?? "Disk", d.Media ?? "", d.Bus ?? "", d.Size, d.Health ?? "", d.Temp, d.Wear)).ToArray();
        if (disks.Length > 0 && disks.All(d => d.TemperatureC is null)) notes.Add("Drive temperature and wear are shown when Hanki runs as administrator.");
        var storage = new StorageFacts(disks, (dto.Volumes ?? []).Where(v => v.Letter is { Length: 1 }).Select(v => new VolumeFacts(v.Letter![0], v.Disk, v.FileSystem ?? "", v.Size, v.Free)).ToArray(),
            trimDisabled, dto.TaskState, dto.LastRun, Environment.OSVersion.Version.Build, DirectX12(), notes);
        return (cpu, memory, storage);
    }

    /// <summary>Whether the default GPU can create a DirectX 12 device at feature level 12_0 (a test call; no device is kept).</summary>
    private static bool? DirectX12()
    {
        try { var id = new Guid("189819f1-1db6-4b57-be54-1821339b85f7"); return D3D12CreateDevice(IntPtr.Zero, 0xC000, ref id, IntPtr.Zero) is 0 or 1; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct PerformanceInfo {
        public uint Size; public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint Handles, Processes, Threads;
    }
    [DllImport("psapi.dll")] private static extern bool GetPerformanceInfo(out PerformanceInfo info, uint size);
    [DllImport("kernel32.dll")] private static extern bool GetPhysicallyInstalledSystemMemory(out ulong kilobytes);
    [DllImport("d3d12.dll")] private static extern int D3D12CreateDevice(IntPtr adapter, int minimumFeatureLevel, ref Guid riid, IntPtr device);
}
