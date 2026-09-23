using System.Text;
namespace IgezziGuard;

public sealed record TechnicianSession(Guid Id, string CustomerLabel, string DeviceLabel, DateTimeOffset Started,
    Guid ScanId, DiagnosticScan Scan, RepairReport? Repairs);
public sealed record TechnicianBusiness(string DisplayName, string Contact);
public sealed record TechnicianSeat(string Subject, string Organization, string SeatId, int MaximumRegisteredWorkstations,
    DateTimeOffset Expires, DateTimeOffset OfflineGraceUntil);
public sealed record TechnicianWorkstation(string RegistrationId, string SeatId, bool Revoked);
public sealed record CustomerDeviceSession(Guid SessionId, string Label); // Never a paid workstation registration.
public static class TechnicianSeatPolicy
{
    public static bool CanWorkOffline(TechnicianSeat seat,TechnicianWorkstation workstation,DateTimeOffset now)=>
        !workstation.Revoked && workstation.SeatId==seat.SeatId && !string.IsNullOrWhiteSpace(workstation.RegistrationId) &&
        seat.MaximumRegisteredWorkstations>0 && now<seat.OfflineGraceUntil && seat.OfflineGraceUntil>=seat.Expires;
    public static bool CanRegister(TechnicianSeat seat,IEnumerable<TechnicianWorkstation> workstations,DateTimeOffset now)=>
        now<seat.Expires && workstations.Where(w=>w.SeatId==seat.SeatId&&!w.Revoked).Select(w=>w.RegistrationId).Distinct(StringComparer.Ordinal).Count()<seat.MaximumRegisteredWorkstations;
}
public sealed class TechnicianSessions(IEntitlements entitlements)
{
    public TechnicianSession Begin(string customerLabel,string deviceLabel,DiagnosticScan scan,RepairReport? repairs=null)
    {
        if(!entitlements.Allows(HankiCapability.TechnicianSessions))throw new InvalidOperationException("Technician capability unavailable.");
        if(repairs is not null && repairs.ScanId!=scan.Id)throw new ArgumentException("Repair report belongs to another scan.");
        return new(Guid.NewGuid(),Label(customerLabel),Label(deviceLabel),DateTimeOffset.UtcNow,scan.Id,scan,repairs);
    }
    internal static string Label(string text)
    {
        var value=text.Trim();
        if(value.Length>100||value.Any(char.IsControl))throw new ArgumentException("Use a short single-line label (100 characters maximum).");
        return value.Length==0?"Unlabelled":value;
    }
    public string Export(TechnicianSession session,TechnicianBusiness business,bool includeTechnical)
    {
        if(!entitlements.Allows(HankiCapability.CustomerReports))throw new InvalidOperationException("Customer-report capability unavailable.");
        if(session.ScanId!=session.Scan.Id||session.Repairs is not null&&session.Repairs.ScanId!=session.ScanId)throw new ArgumentException("Report evidence does not match this session.");
        var report=new StringBuilder($"{Label(business.DisplayName)} — PC service report\r\n{Label(business.Contact)}\r\n\r\nJob: {Label(session.CustomerLabel)} · Device: {Label(session.DeviceLabel)}\r\nChecked: {session.Scan.Ended:g}\r\n\r\n");
        report.AppendLine("Observations and next steps");
        foreach(var r in session.Scan.Results){report.AppendLine($"{r.Title}: {r.Severity} ({r.Outcome})");report.AppendLine(FindingAnalysis.Recommend(r)?.ManualAction??"Review incomplete evidence or the individual check as needed.");}
        report.AppendLine("\r\nRepairs and verification");
        report.AppendLine(session.Repairs is null?"No Hanki repair attempts recorded for this session.":RepairReportText.Format(session.Repairs));
        report.AppendLine("\r\nOnly a Fixed verification result indicates that the matched diagnostic condition cleared. Unknown, unchanged, blocked or failed results still need review. Findings do not guarantee overall hardware/software health.");
        if(includeTechnical){report.AppendLine("\r\nOptional technical details — identifiers minimized; review before giving to a customer");foreach(var r in session.Scan.Results)report.AppendLine($"{r.ModuleId}: {DiagnosticPrivacy.Redact(r.Evidence)}\r\n{r.Coverage}");}
        return report.ToString();
    }
    public void SaveLocal(string path,TechnicianSession session)
    {
        if(!entitlements.Allows(HankiCapability.TechnicianSessions))throw new InvalidOperationException("Technician capability unavailable.");
        using var gate=LocalJson.Lock(path);
        var minimized=session with {Scan=DiagnosticPrivacy.Minimize(session.Scan),Repairs=session.Repairs is null?null:session.Repairs with {Attempts=session.Repairs.Attempts.Select(DiagnosticPrivacy.Minimize).ToArray()}};
        LocalJson.Write(path,minimized);
    }
}
