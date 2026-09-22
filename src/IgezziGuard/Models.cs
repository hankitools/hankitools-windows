namespace IgezziGuard;

public enum DetectionSeverity
{
    Suspicious = 1,
    High = 2,
    Malware = 3
}

public sealed record ScanFinding(
    string FilePath,
    string Sha256,
    DetectionSeverity Severity,
    string DetectionName,
    string Details,
    int Score,
    long FileSize,
    DateTimeOffset DetectedAt);

public sealed record ScanProgress(
    string CurrentPath,
    long FilesScanned,
    long BytesScanned,
    int Detections,
    int Skipped,
    int Errors);

public sealed record ScanSummary(
    string Target,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    long FilesScanned,
    long BytesScanned,
    int Skipped,
    int Errors,
    IReadOnlyList<ScanFinding> Findings);

public sealed record SignatureEntry(
    string Sha256,
    string Name,
    DetectionSeverity Severity);

public sealed record QuarantineItem(
    string Id,
    string OriginalPath,
    string DataFileName,
    string Sha256,
    string DetectionName,
    DetectionSeverity Severity,
    long OriginalSize,
    DateTimeOffset QuarantinedAt,
    string NonceBase64,
    string TagBase64);

public sealed record HistoryEntry(
    string Target,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    long FilesScanned,
    long BytesScanned,
    int DetectionCount,
    int Skipped,
    int Errors);
