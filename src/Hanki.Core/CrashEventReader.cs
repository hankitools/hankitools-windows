using System.Diagnostics.Eventing.Reader;
using System.Globalization;

namespace IgezziGuard;

internal static class CrashEventReader
{
    public static (List<CrashEvent> Events, string Coverage) Read(DateTimeOffset from, DateTimeOffset to, bool markersOnly, CancellationToken token)
    {
        var events = new List<CrashEvent>(); var notes = new List<string>();
        string Stamp(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var filter = markersOnly ? "(EventID=41 or EventID=6008 or EventID=1001)" : "(Level=1 or Level=2 or Level=3 or EventID=41 or EventID=6008 or EventID=1001 or EventID=1074 or EventID=6005 or EventID=6006)";
        foreach (var log in markersOnly ? new[] { "System" } : new[] { "System", "Application" }) {
            token.ThrowIfCancellationRequested();
            try {
                var query = new EventLogQuery(log, PathType.LogName, $"*[System[TimeCreated[@SystemTime >= '{Stamp(from)}' and @SystemTime <= '{Stamp(to)}'] and {filter}]]") { ReverseDirection = true };
                using var reader = new EventLogReader(query);
                using var registration = token.Register(() => { try { reader.CancelReading(); } catch (ObjectDisposedException) { } catch (EventLogException) { } });
                int count = 0, missingTime = 0;
                while (count < 500) {
                    token.ThrowIfCancellationRequested();
                    using var record = reader.ReadEvent(TimeSpan.FromSeconds(3));
                    if (record is null) break;
                    count++;
                    if (record.TimeCreated is not { } time) { missingTime++; continue; }
                    string message;
                    try { message = record.FormatDescription() ?? "Description unavailable."; }
                    catch (EventLogException) { message = "Description unavailable; use provider, ID and record number in Event Viewer."; }
                    if (message.Length > 1200) message = message[..1200] + " [message truncated]";
                    var e = new CrashEvent(new DateTimeOffset(time), log, record.ProviderName ?? "Unknown", record.Id, record.RecordId,
                        record.Level switch { 1 => "Critical", 2 => "Error", 3 => "Warning", 4 => "Information", _ => "Other" }, message);
                    if (!markersOnly || CrashTimeline.IsMarker(e)) events.Add(e);
                }
                notes.Add($"{log}: {count} queried records" + (count == 500 ? "; cap reached, older events may be omitted" : "") + (missingTime > 0 ? $"; {missingTime} records without time omitted" : "") + ".");
            }
            catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or System.Security.SecurityException) {
                token.ThrowIfCancellationRequested(); notes.Add($"{log}: unavailable/incomplete ({ex.Message}).");
            }
        }
        token.ThrowIfCancellationRequested();
        return (events, string.Join(" ", notes) + " Newest 500 queried records per log maximum; messages capped at 1,200 characters. Warning/error/critical and selected restart-related IDs only; not a complete log export.");
    }
}
