using System.Text.Json;

namespace IgezziGuard;

public sealed class ExtendedPerformancePanel : ToolPage
{
    private readonly ComboBox duration = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private SavedSession? latest, baseline;
    public ExtendedPerformancePanel() : base("Long monitoring: CPU/memory about every second, physical-disk throughput and busiest GPU engine about every five seconds. WMI/driver support required; missing counters stay unknown. Save sessions to compare later. GPU reports the busiest engine, not summed utilization across GPUs. Collectors add some overhead.") {
        duration.Items.AddRange(["30 seconds", "1 minute", "5 minutes", "15 minutes"]); duration.SelectedIndex = 1; Bar.Controls.Add(duration);
        Button("Start monitoring", async () => {
            int seconds = new[] { 30, 60, 300, 900 }[duration.SelectedIndex]; SavedSession? complete = null;
            var progress = new Progress<string>(text => { if (IsBusy) Output.Text = text; });
            await Run(async token => {
                var cpu = PerformanceSession.Sample(token, seconds, progress); var devices = Devices(seconds, token);
                await Task.WhenAll(cpu, devices); complete = new(1, Environment.MachineName, await cpu, await devices);
                return Describe(complete) + (baseline is null ? "\r\nNo baseline selected. Save this run or use it as baseline." : Compare(baseline, complete));
            }); latest = complete;
        });
        Button("Use last run as baseline", () => { if (latest is not null) { baseline = latest; Output.Text = "Baseline selected.\r\n" + Describe(baseline); } });
        Button("Save last run", () => { if (latest is null) return; using var picker = new SaveFileDialog { Filter = "Hanki session|*.json", FileName = "Hanki-session-" + latest.Cpu.Started.ToString("yyyyMMdd-HHmmss") + ".json", OverwritePrompt = true }; if (picker.ShowDialog(this) == DialogResult.OK) try { File.WriteAllText(picker.FileName, JsonSerializer.Serialize(latest, new JsonSerializerOptions { WriteIndented = true })); } catch (Exception ex) { Output.Text = ex.Message; } });
        Button("Load saved baseline", () => { using var picker = new OpenFileDialog { Filter = "Hanki session|*.json" }; if (picker.ShowDialog(this) != DialogResult.OK) return; try {
            if (new FileInfo(picker.FileName).Length > 5_000_000) throw new IOException("Session file too large.");
            var saved = JsonSerializer.Deserialize<SavedSession>(File.ReadAllText(picker.FileName)) ?? throw new IOException("Invalid session.");
            SessionValidation.Validate(saved);
            baseline = saved; Output.Text = "Loaded baseline\r\n" + Describe(saved) + (latest is null ? "" : Compare(saved, latest));
        } catch (Exception ex) { Output.Text = "Could not load: " + ex.Message; } });
    }
    private static async Task<List<DeviceSample>> Devices(int seconds, CancellationToken token)
    {
        var script = "$rows=@(for($i=0;$i -lt " + (seconds / 5) + ";$i++){ Start-Sleep -Seconds 5; $r=$null;$w=$null;$g=$null;$notes=@();" +
            "try{$d=Get-CimInstance Win32_PerfFormattedData_PerfDisk_PhysicalDisk | Where-Object Name -eq '_Total';if($null -eq $d){throw 'Disk total unavailable'};$r=[double]$d.DiskReadBytesPersec;$w=[double]$d.DiskWriteBytesPersec}catch{$notes+='Disk counter unavailable'};" +
            "try{$engines=@(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine);if($engines.Count -gt 0){$values=@($engines | Group-Object { $_.Name -replace '^pid_\\d+_','' } | ForEach-Object { [Math]::Min(100,($_.Group | Measure-Object UtilizationPercentage -Sum).Sum) });$g=[double](($values | Measure-Object -Maximum).Maximum)}else{$notes+='GPU counters unavailable'}}catch{$notes+='GPU counter unavailable'};" +
            "[pscustomobject]@{At=[DateTimeOffset]::Now.ToString('o');DiskRead=$r;DiskWrite=$w;GpuBusy=$g;Notes=($notes -join '; ')} }); ConvertTo-Json -InputObject @($rows) -Depth 3";
        try { return JsonSerializer.Deserialize<List<DeviceSample>>(await WindowsCommand.PowerShell(script, token, seconds + 180)) ?? []; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return [new(DateTimeOffset.Now, null, null, null, "Device collection timed out. CPU/memory sampling retained; disk/GPU values unknown.")]; }
        catch (Exception ex) when (ex is IOException or JsonException or System.ComponentModel.Win32Exception) { return [new(DateTimeOffset.Now, null, null, null, "Device collection unavailable: " + ex.Message)]; }
    }
    private static string Metric(IEnumerable<double?> values, string unit) { var data = values.Where(x => x.HasValue).Select(x => x!.Value).ToArray(); return data.Length == 0 ? "Unknown" : $"mean {data.Average():0.00}, peak {data.Max():0.00} {unit} ({data.Length} valid samples)"; }
    private static string Describe(SavedSession s) => $"Machine: {s.Machine}\r\n" + PerformanceSession.Describe(s.Cpu) +
        $"Disk read: {Metric(s.Devices.Select(d => d.DiskRead / 1048576), "MiB/s")}\r\nDisk write: {Metric(s.Devices.Select(d => d.DiskWrite / 1048576), "MiB/s")}\r\nBusiest GPU engine: {Metric(s.Devices.Select(d => d.GpuBusy), "%")}\r\n" +
        "GPU samples aggregate process contributions per named engine and take the busiest engine across adapters; they are not an FPS measure. Device and CPU collection windows can differ. CPU may cover only the calling processor group on >64 logical processors.\r\n" +
        string.Join("\r\n", s.Devices.Where(d => !string.IsNullOrWhiteSpace(d.Notes)).Select(d => d.Notes).Distinct());
    private static string Compare(SavedSession before, SavedSession after) =>
        $"\r\nCOMPARISON with {before.Cpu.Started:O}\r\n" + (before.Machine != after.Machine ? "Different machine names: comparison is not like-for-like.\r\n" : "") +
        $"Average CPU change: {after.Cpu.AverageCpu - before.Cpu.AverageCpu:+0.0;-0.0;0.0} percentage points\r\nAverage commit change: {after.Cpu.AverageCommitPercent - before.Cpu.AverageCommitPercent:+0.0;-0.0;0.0} percentage points\r\n" +
        "Repeat the same workload and duration. Differences do not prove a tweak helped.\r\n\r\nBASELINE\r\n" + Describe(before);
}
