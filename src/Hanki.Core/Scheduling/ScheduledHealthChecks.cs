using System.Xml.Linq;
namespace IgezziGuard;

public enum HealthCheckFrequency { Daily, Weekly }
internal static class ScheduledHealthChecks
{
    internal const string TaskName = "Hanki Tools - local health check";
    internal static string TaskXml(string executable, string user, HealthCheckFrequency frequency)
    {
        if (!Path.IsPathFullyQualified(executable) || !Enum.IsDefined(frequency) || string.IsNullOrWhiteSpace(user)) throw new ArgumentException("Invalid schedule configuration.");
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var calendar = frequency == HealthCheckFrequency.Daily
            ? new XElement(ns+"ScheduleByDay",new XElement(ns+"DaysInterval",1))
            : new XElement(ns+"ScheduleByWeek",new XElement(ns+"WeeksInterval",1),new XElement(ns+"DaysOfWeek",new XElement(ns+"Sunday")));
        return new XDocument(new XElement(ns+"Task",new XAttribute("version","1.2"),
            new XElement(ns+"RegistrationInfo",new XElement(ns+"Description","Opt-in local read-only health check. No repairs or network probes. Remove from Hanki to disable.")),
            new XElement(ns+"Triggers",new XElement(ns+"CalendarTrigger",new XElement(ns+"StartBoundary",DateTime.Today.AddDays(1).AddHours(19).ToString("yyyy-MM-ddTHH:mm:ss")),new XElement(ns+"Enabled",true),calendar)),
            new XElement(ns+"Principals",new XElement(ns+"Principal",new XAttribute("id","Author"),new XElement(ns+"UserId",user),new XElement(ns+"LogonType","InteractiveToken"),new XElement(ns+"RunLevel","LeastPrivilege"))),
            new XElement(ns+"Settings",new XElement(ns+"MultipleInstancesPolicy","IgnoreNew"),new XElement(ns+"DisallowStartIfOnBatteries",true),new XElement(ns+"StopIfGoingOnBatteries",true),new XElement(ns+"StartWhenAvailable",true),new XElement(ns+"ExecutionTimeLimit","PT45M")),
            new XElement(ns+"Actions",new XAttribute("Context","Author"),new XElement(ns+"Exec",new XElement(ns+"Command",executable),new XElement(ns+"Arguments","--scheduled-health-check"))))).ToString();
    }
    internal static async Task InstallAsync(HealthCheckFrequency frequency, IEntitlements entitlements, CancellationToken token)
    {
        if(!entitlements.Allows(HankiCapability.ScheduledChecks))throw new InvalidOperationException("Scheduled checks are unavailable for this edition.");
        using var identity=System.Security.Principal.WindowsIdentity.GetCurrent();
        var exe=Environment.ProcessPath ?? throw new IOException("Executable path unavailable.");
        if(!File.Exists(exe))throw new IOException("Executable not found.");
        if(Path.GetFileNameWithoutExtension(exe).Equals("dotnet",StringComparison.OrdinalIgnoreCase))throw new IOException("Launch the Hanki executable directly before creating a schedule.");
        var directory=Path.Combine(SecurityPaths.Root,"scheduling");Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".xml");
        try { await File.WriteAllTextAsync(path,TaskXml(exe,identity.User?.Value ?? throw new IOException("Current-user identity unavailable."),frequency),token);
            await WindowsCommand.RunCaptured(Path.Combine(Environment.SystemDirectory,"schtasks.exe"),["/Create","/TN",TaskName,"/XML",path,"/F"],token);
        }finally{if(File.Exists(path))File.Delete(path);}
    }
    internal static async Task RemoveAsync(CancellationToken token) =>
        await WindowsCommand.RunCaptured(Path.Combine(Environment.SystemDirectory,"schtasks.exe"),["/Delete","/TN",TaskName,"/F"],token);
    internal sealed record ScheduleStatus(bool Exists, string? State, DateTimeOffset? LastRun, long? LastResult, DateTimeOffset? NextRun);
    // Read through the ScheduledTasks module: its values don't depend on the Windows display language.
    private const string StatusScript = """
        $t=Get-ScheduledTask -TaskName 'Hanki Tools - local health check' -ErrorAction SilentlyContinue
        if(-not $t){[pscustomobject]@{Exists=$false}|ConvertTo-Json -Compress;return}
        $i=$t|Get-ScheduledTaskInfo
        $last=if($i.LastRunTime -and $i.LastRunTime.Year -ge 2000){$i.LastRunTime.ToUniversalTime().ToString('o')}else{$null}
        $next=if($i.NextRunTime -and $i.NextRunTime.Year -ge 2000){$i.NextRunTime.ToUniversalTime().ToString('o')}else{$null}
        [pscustomobject]@{Exists=$true;State=[string]$t.State;LastRun=$last;LastResult=[long]$i.LastTaskResult;NextRun=$next}|ConvertTo-Json -Compress
        """;
    internal static async Task<ScheduleStatus> StatusAsync(CancellationToken token)
    {
        var output = await WindowsCommand.PowerShellCapture(StatusScript, token, 30);
        return System.Text.Json.JsonSerializer.Deserialize<ScheduleStatus>(output.StandardOutput, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new IOException("Schedule status unavailable.");
    }
    /// <summary>One plain line for the saved-scans page (System actions).</summary>
    internal static string Describe(ScheduleStatus status)
    {
        if (!status.Exists) return "No scheduled check.";
        string next = status.NextRun is { } n ? $"next {n.ToLocalTime():g}" : "no next run planned";
        if (string.Equals(status.State, "Disabled", StringComparison.OrdinalIgnoreCase)) return "Scheduled check is turned off in Task Scheduler.";
        if (status.LastRun is not { } last) return $"Scheduled check is on ({next}); it hasn't run yet.";
        string result = status.LastResult switch {
            0 => "finished",
            1 => "finished with gaps (some checks were unavailable)",
            0x41301 => "is running",
            0x41303 => "hasn't run yet",
            _ =>$"didn't finish (code 0x{unchecked((uint)(status.LastResult ?? 0)):X})"
        };
        return $"Scheduled check is on ({next}). Last run {last.ToLocalTime():g} {result}.";
    }
    internal static async Task<int> RunAsync()
    {
        // No edition check here: the run is the same read-only scan as the free Full System Scan.
        // Scheduling it is the Pro convenience, checked when the task is created (InstallAsync).
        try {
            SecurityPaths.EnsureCreated();
            using var gate=LocalJson.Lock(Path.Combine(SecurityPaths.Root,"scheduled-run"));
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(40));
            // Explicitly exclude admin/servicing and external probes from unattended runs.
            var modules=WindowsDiagnosticCatalog.Create().Where(m=>!m.Requirements.Administrator&&!m.Requirements.ExternalContact);
            var result=await new DiagnosticOrchestrator(modules).ScanAsync(new(true,false,false),null,timeout.Token);
            new DiagnosticHistory(Path.Combine(SecurityPaths.Root,"diagnostic-history.json")).Add(result);
            return result.Complete?0:1;
        }catch{return 1;}
    }
}
