using System.Text.Json;
namespace IgezziGuard;

public sealed record DiagnosticHistoryDocument(int Version, IReadOnlyList<DiagnosticScan> Scans);
public sealed record RepairAuditEntry(Guid ScanId, RepairAttempt Attempt);
public sealed record RepairAuditDocument(int Version, IReadOnlyList<RepairAuditEntry> Entries);

internal static class LocalJson
{
    internal static T? Read<T>(string path)
    {
        if (!File.Exists(path)) return default;
        if (new FileInfo(path).Length > 10_000_000) throw new IOException("History exceeds the safe read limit. Original file preserved.");
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new IOException("Empty history. Original file preserved."); }
        catch (JsonException ex) { throw new IOException("History is damaged or unsupported. Original file preserved.", ex); }
    }
    internal static void Write<T>(string path, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 10_000_000) throw new IOException("History limit reached. Existing data retained.");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using(var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); } File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static FileStream Lock(string path) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); return new(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
}

public sealed class DiagnosticHistory(string path)
{
    public IReadOnlyList<DiagnosticScan> Read()
    {
        var doc = LocalJson.Read<DiagnosticHistoryDocument>(path);
        if (doc is null) return [];
        if (doc.Version != 1 || doc.Scans is null || doc.Scans.Count > 30 || doc.Scans.Any(s => s is null || s.Results is null || s.Results.Any(r => r is null) || s.Ended < s.Started || s.CompletedModules > s.PlannedModules))
            throw new IOException("Unsupported history format. File preserved.");
        return doc.Scans;
    }
    public void Add(DiagnosticScan scan)
    {
        using var gate = LocalJson.Lock(path);
        var rows = Read().Where(s => s.Id != scan.Id && s.Ended >= DateTimeOffset.UtcNow.AddDays(-90)).Append(DiagnosticPrivacy.Minimize(scan))
            .OrderByDescending(s => s.Ended).Take(30).ToArray();
        LocalJson.Write(path, new DiagnosticHistoryDocument(1, rows));
    }
    public void Clear() { using var gate = LocalJson.Lock(path); LocalJson.Write(path, new DiagnosticHistoryDocument(1, [])); }
    public static IReadOnlyList<string> Compare(DiagnosticScan before, DiagnosticScan after)
    {
        var old = before.Results.ToDictionary(r => (r.ModuleId, r.FindingId));
        var current = after.Results.ToDictionary(r => (r.ModuleId, r.FindingId));
        return old.Keys.Union(current.Keys).OrderBy(k => k.ModuleId, StringComparer.Ordinal).ThenBy(k => k.FindingId, StringComparer.Ordinal).Select(k => {
            old.TryGetValue(k, out var b); current.TryGetValue(k, out var a);
            string state = b is null ? "New" : a is null ? "Not observed (not proof of resolution)" : a.Outcome != CollectionOutcome.Completed || a.Severity == FindingSeverity.Unknown ? "Unknown / incomplete" :
                a.Severity == FindingSeverity.Healthy && b.Severity is FindingSeverity.Warning or FindingSeverity.Critical ? "Resolved" :
                b.Outcome != CollectionOutcome.Completed || b.Severity == FindingSeverity.Unknown ? "New evidence" :
                DiagnosticOrchestrator.Priority(a.Severity) < DiagnosticOrchestrator.Priority(b.Severity) ? "Improved" :
                DiagnosticOrchestrator.Priority(a.Severity) > DiagnosticOrchestrator.Priority(b.Severity) ? "Worsened" : "Unchanged";
            return $"{a?.Title ?? b!.Title}: {state}";
        }).ToArray();
    }
}

public sealed class RepairAudit(string path) : IRepairAudit
{
    public IReadOnlyList<RepairAuditEntry> Read()
    {
        var doc = LocalJson.Read<RepairAuditDocument>(path);
        if (doc is null) return [];
        if (doc.Version != 1 || doc.Entries is null || doc.Entries.Count > 1000 || doc.Entries.Any(e => e?.Attempt is null)) throw new IOException("Unsupported repair audit. File preserved.");
        return doc.Entries;
    }
    public Task RecordAsync(Guid scanId, RepairAttempt attempt, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var gate = LocalJson.Lock(path);
        var rows = Read().Where(e => !(e.ScanId == scanId && e.Attempt.ActionId == attempt.ActionId && e.Attempt.Started == attempt.Started)).ToList();
        if (rows.Count >= 1000) throw new IOException("Repair audit full. Export/review before further repairs; pending records must be preserved.");
        rows.Add(new(scanId, DiagnosticPrivacy.Minimize(attempt)));
        LocalJson.Write(path, new RepairAuditDocument(1, rows));
        return Task.CompletedTask;
    }
}

public static class RepairReportText
{
    public static string Format(RepairReport report) => "Repair results — no command exit code alone proves a fix\r\n\r\n" + string.Join("\r\n\r\n", report.Attempts.Select(a =>
        $"{a.ActionId}: {a.State}\r\nVerification: {a.Verification}\r\n{DiagnosticPrivacy.Redact(a.Explanation)}\r\nRestore protection: {a.Protection.State} — {a.Protection.Explanation}\r\nBefore: {string.Join(", ", a.Before.Select(r => r.Severity))}\r\nAfter: {string.Join(", ", a.After.Select(r => r.Severity))}"));
}
