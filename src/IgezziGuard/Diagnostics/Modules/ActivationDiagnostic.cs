using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace IgezziGuard;

internal sealed record ActivationProduct(string? Id,string? Name,string? Description,int? LicenseStatus,uint? LicenseStatusReason,
    uint? GracePeriodRemaining,string? KeyManagementServiceMachine,uint? KeyManagementServicePort);
internal sealed record ActivationSnapshot(string? Edition,string? ServiceState,string? ServiceStartMode,uint? Rearms,
    ActivationProduct[]? Products,string[]? RecentCodes,string? Coverage);
public sealed record ActivationError(string Code,string Category,string Explanation,string NextStep,string ActionOwner);
internal static partial class ActivationRules
{
    internal static ActivationError Explain(uint code)
    {
        var hex=$"0x{code:X8}";
        (string Category,string Explanation,string Next,string Owner) value=code switch {
            0xC004F050 => ("Key / edition","Windows reports that the installed product key is invalid. This alone does not prove an edition mismatch.","Check that your legitimate license matches the installed Windows edition in Activation settings; contact Microsoft if it should match.","User / Microsoft"),
            0xC004F210 or 0xC004F212 => ("Edition mismatch","Windows reports a license/edition mismatch.","Compare the installed edition with your legitimate license and use Microsoft's Activation troubleshooter.","User / Microsoft"),
            0xC004F211 => ("Hardware change","Windows reports that this device's hardware has changed.","Use the Activation troubleshooter and the Microsoft account associated with your digital license, if applicable.","User / Microsoft"),
            0x803F7001 => ("License not found","Windows could not find a valid license for this installation.","Review Activation settings and your existing digital license. A hardware or edition change may need Microsoft's troubleshooter.","User / Microsoft"),
            0xC004C003 or 0xC004C060 => ("Blocked key","Windows reports a blocked activation key.","Contact Microsoft or your organization's licensing administrator with proof of your legitimate license. Do not use replacement keys from untrusted sources.","Microsoft / organization"),
            0xC004C008 => ("Activation limit","Windows reports the key has reached its permitted activation use.","Contact Microsoft or your organization's licensing administrator to review the license and allowed devices.","Microsoft / organization"),
            0xC004F074 => ("KMS reachability","Windows could not complete contact with its configured KMS service.","Connect to the organization's approved network/VPN, review time and DNS, and contact its administrator. No public KMS host is recommended.","Organization administrator"),
            0x8007232B => ("KMS DNS","Windows reports a DNS name lookup failure during activation.","For an organization-managed KMS client, review internal DNS/VPN and the approved KMS service with your administrator.","Organization administrator"),
            0xC004F038 => ("KMS activation count","The organization's KMS service reports an insufficient activation count.","Ask the organization's licensing administrator to review the legitimate KMS deployment; client resets do not establish entitlement.","Organization administrator"),
            0xC004F06C => ("Time / KMS validation","KMS activation validation failed; a time difference can be relevant.","Review Windows date, time zone and synchronization against your organization's trusted time source. Hanki does not adjust the clock.","User / organization administrator"),
            0x80072F8F => ("Time / secure connection","Windows reports a secure-connection validation problem during activation.","Check date/time and trusted network connectivity; use Microsoft's Activation troubleshooter.","User / administrator"),
            0x80072EE7 or 0x80072EFD => ("Network","Windows reports a name-resolution or connection problem.","Review Connect DNS/proxy/VPN results and retry through Windows Activation settings on a trusted connection.","User / administrator"),
            _ => ("Unknown","This activation error is not mapped by Hanki.","Retain the exact code and consult Windows Activation settings or Microsoft/organization support; no cause is inferred.","Microsoft / organization")
        };
        return new(hex,value.Category,value.Explanation,value.Next,value.Owner);
    }
    internal static string Channel(string? description) {
        var text=description?.ToUpperInvariant()??"";
        if(text.Contains("VOLUME_KMSCLIENT"))return "KMS client";
        if(text.Contains("VOLUME_MAK"))return "MAK / volume";
        if(text.Contains("OEM"))return "OEM";
        if(text.Contains("RETAIL"))return "Retail";
        return "Unknown";
    }
    // Also mask unexpected key-shaped strings from provider text; no full-key property is queried.
    [GeneratedRegex(@"(?i)\b[A-Z0-9]{5}(?:-[A-Z0-9]{5}){4}\b")]
    private static partial Regex ProductKey();
    internal static string Safe(string? value)=>ProductKey().Replace(value??"Unknown","[product key omitted]");
    internal static bool Host(string value)=>value.Length is >0 and <=253 && value.TrimEnd('.').Split('.').All(label=>label.Length is >0 and <=63&&label.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-')&&label[0]!='-'&&label[^1]!='-');
    internal static IReadOnlyList<DiagnosticResult> Map(ActivationSnapshot snapshot,DateTimeOffset start,DateTimeOffset end)
    {
        var results=new List<DiagnosticResult>();
        DiagnosticResult Make(string id,FindingSeverity severity,string title,string explanation,string evidence,CollectionOutcome outcome=CollectionOutcome.Completed,string? next=null)=>
            new("activation",id,DiagnosticCategory.Windows,outcome,severity,title,Safe(explanation),start,end,Safe(evidence),
                "Read-only Windows licensing evidence. No keys are collected or changed. "+Safe(snapshot.Coverage),recommendation:next);
        var products=snapshot.Products??[];
        if(products.Length==0)results.Add(Make("status",FindingSeverity.Unknown,"Windows activation","No installed Windows licensing product was returned; activation state is unknown.","Edition: "+Safe(snapshot.Edition),CollectionOutcome.Unavailable));
        foreach(var p in products.Take(20)) {
            var status=p.LicenseStatus switch {0=>"Unlicensed",1=>"Licensed",2=>"Initial grace period",3=>"Hardware-change / out-of-tolerance grace",4=>"Non-genuine grace",5=>"Notification",6=>"Extended grace",_=>"Unknown"};
            var severity=p.LicenseStatus==1?FindingSeverity.Informational:p.LicenseStatus is >=0 and <=6?FindingSeverity.Warning:FindingSeverity.Unknown;
            string explanation=p.LicenseStatus==1?"Windows reports this installed license as activated.":"Windows reports "+status+". Review Activation settings before making licensing changes.";
            var evidence=$"Installed edition: {Safe(snapshot.Edition)}\r\nLicense product: {Safe(p.Name)}\r\nStatus: {status}\r\nChannel: {Channel(p.Description)}\r\nGrace remaining (minutes): {p.GracePeriodRemaining?.ToString()??"Unknown"}\r\nRemaining Windows rearms: {snapshot.Rearms?.ToString()??"Unknown"}";
            string? next="Open Windows Activation settings to review the installed edition and your legitimate license.";
            if(p.LicenseStatusReason is uint code && code!=0) {var error=Explain(code);explanation+=" "+error.Explanation;evidence+=$"\r\nError: {error.Code} ({error.Category})\r\nAction owner: {error.ActionOwner}";next=error.NextStep;}
            if(Channel(p.Description)=="KMS client")evidence+="\r\nConfigured KMS host: "+Safe(p.KeyManagementServiceMachine)+". Organization activation only; no KMS host is invented or substituted.";
            // IDs come from Windows GUIDs. Unexpected identifiers are not exposed.
            var id=Guid.TryParse(p.Id,out var guid)?guid.ToString():"product-"+results.Count;
            results.Add(Make(id,severity,"Windows activation",explanation,evidence,next:next));
        }
        var service=snapshot.ServiceState;
        var state=service is null?FindingSeverity.Unknown:snapshot.ServiceStartMode=="Disabled"?FindingSeverity.Warning:FindingSeverity.Informational;
        results.Add(Make("service",state,"Software Protection service",service is null?"The licensing service could not be inspected.":"Software Protection state: "+service+". A stopped trigger-start service alone does not prove activation failure.","sppsvc: "+Safe(service)+" / "+Safe(snapshot.ServiceStartMode),service is null?CollectionOutcome.Unavailable:CollectionOutcome.Completed));
        foreach(var codeText in (snapshot.RecentCodes??[]).Distinct(StringComparer.OrdinalIgnoreCase).Take(10)) {
            if(!uint.TryParse(codeText.Replace("0x","",StringComparison.OrdinalIgnoreCase),System.Globalization.NumberStyles.HexNumber,null,out var code)||code==0)continue;
            var e=Explain(code);results.Add(Make("event-"+e.Code,FindingSeverity.Informational,"Recent activation error",e.Explanation,"Historical event code: "+e.Code+"; may no longer describe the current state. Category: "+e.Category,next:e.NextStep));
        }
        results.Add(Make("time",FindingSeverity.Informational,"Activation time context","Incorrect date/time can affect secure activation. Hanki has not compared the clock with a trusted time server.","Current local time: "+DateTimeOffset.Now.ToString("O")+"; time zone: "+TimeZoneInfo.Local.Id,next:"If activation reports a time/secure-connection error, inspect Date & time in Windows Settings."));
        return results;
    }
}

internal sealed class ActivationDiagnostic(IDiagnosticProbe? source=null) : IDiagnosticModule
{
    private readonly IDiagnosticProbe source=source??new WindowsDiagnosticProbe();
    public string Id=>"activation";
    public string DisplayName=>"Windows activation";
    public DiagnosticCategory Category=>DiagnosticCategory.Windows;
    public DiagnosticRequirements Requirements=>new(Disclosure:"KMS discovery/reachability occurs only for an installed KMS client and only with network consent.");
    internal const string Script="""
        $notes=@();$edition=$null;$products=@();$service=$null;$rearms=$null;$codes=@();
        try{$edition=(Get-CimInstance -Query 'SELECT Caption FROM Win32_OperatingSystem').Caption}catch{$notes+='Edition unavailable'}
        try{$products=@(Get-CimInstance -Query "SELECT ID,Name,Description,LicenseStatus,LicenseStatusReason,GracePeriodRemaining,KeyManagementServiceMachine,KeyManagementServicePort FROM SoftwareLicensingProduct WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL" | Select-Object ID,Name,Description,LicenseStatus,LicenseStatusReason,GracePeriodRemaining,KeyManagementServiceMachine,KeyManagementServicePort)}catch{$notes+='Licensing products unavailable'}
        try{$service=Get-CimInstance -Query "SELECT State,StartMode FROM Win32_Service WHERE Name='sppsvc'"}catch{$notes+='Service unavailable'}
        try{$rearms=(Get-CimInstance -Query 'SELECT RemainingWindowsReArmCount FROM SoftwareLicensingService').RemainingWindowsReArmCount}catch{$notes+='Rearm information unavailable'}
        try{$events=@(Get-WinEvent -FilterHashtable @{LogName='Application';ProviderName='Microsoft-Windows-Security-SPP';Level=2,3;StartTime=(Get-Date).AddDays(-7)} -MaxEvents 20 -ErrorAction Stop);
            $codes=@($events|ForEach-Object{[regex]::Matches($_.ToXml(),'0x[0-9a-fA-F]{8}')|ForEach-Object{$_.Value}}|Select-Object -Unique -First 10)
        }catch{$notes+='No matching recent licensing errors, or event query unavailable'}
        [pscustomobject]@{Edition=$edition;ServiceState=$service.State;ServiceStartMode=$service.StartMode;Rearms=$rearms;Products=$products;RecentCodes=$codes;Coverage=($notes -join '; ')}|ConvertTo-Json -Depth 5
        """;
    public async Task<IReadOnlyList<DiagnosticResult>> CollectAsync(DiagnosticContext context,IProgress<DiagnosticProgress>? progress,CancellationToken token)
    {
        var start=DateTimeOffset.UtcNow;progress?.Report(new(Id,"Reading local licensing state"));
        var snapshot=JsonSerializer.Deserialize<ActivationSnapshot>(await source.ReadAsync(Script,60,token),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new IOException("Activation data unavailable.");
        var results=ActivationRules.Map(snapshot,start,DateTimeOffset.UtcNow).ToList();
        var kms=snapshot.Products?.FirstOrDefault(p=>ActivationRules.Channel(p.Description)=="KMS client");
        if(kms is not null) {
            string explanation;CollectionOutcome outcome;FindingSeverity severity=FindingSeverity.Unknown;
            if(!context.AllowExternalContact){explanation="KMS network checks were not approved. Connect to your organization's approved network/VPN and use the dedicated activation check if needed.";outcome=CollectionOutcome.Unavailable;}
            else {
                try {
                    string? host=kms.KeyManagementServiceMachine;int port=(int)(kms.KeyManagementServicePort??0);if(port==0)port=1688;
                    if(string.IsNullOrWhiteSpace(host)) {
                        var domain=IPGlobalProperties.GetIPGlobalProperties().DomainName;
                        if(!ActivationRules.Host(domain))throw new IOException("Organization DNS suffix unavailable.");
                        var json=await source.ReadAsync("$r=@(Resolve-DnsName -Type SRV -DnsOnly -QuickTimeout -Name "+WindowsCommand.Quote("_vlmcs._tcp."+domain)+"); ConvertTo-Json -InputObject @($r|Select-Object NameTarget,Port) -Depth 3",15,token);
                        using var doc=JsonDocument.Parse(json);var srv=doc.RootElement.EnumerateArray().FirstOrDefault();
                        if(srv.ValueKind!=JsonValueKind.Object)throw new IOException("KMS DNS SRV record unavailable.");
                        host=srv.GetProperty("NameTarget").GetString();port=srv.GetProperty("Port").GetInt32();
                    }
                    if(host is null||!ActivationRules.Host(host)||port is <1 or >65535)throw new IOException("KMS endpoint invalid.");
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(5));using var tcp=new TcpClient();
                    await tcp.ConnectAsync(host,port,timeout.Token);
                    explanation="The Windows-configured or organization-DNS KMS endpoint accepted a TCP connection. This does not verify activation entitlement, KMS count or protocol success.";outcome=CollectionOutcome.Completed;severity=FindingSeverity.Informational;
                }catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                catch{explanation="KMS discovery or reachability could not be confirmed. Review internal DNS, VPN, firewall and the organization's approved KMS service; no alternate host was used.";outcome=CollectionOutcome.Partial;}
            }
            results.Add(new(Id,"kms-network",Category,outcome,severity,"Organization KMS connectivity",explanation,start,DateTimeOffset.UtcNow,coverage:"Bounded DNS/TCP probes only; no activation request or configuration change."));
        }
        return results;
    }
}
