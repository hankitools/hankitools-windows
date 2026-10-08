using System.Security.Cryptography;
using System.Text;

namespace IgezziGuard;

public sealed class ScannerService
{
    private const long MaxFileSize = 512L * 1024 * 1024;
    private const int InspectionBytes = 4 * 1024 * 1024;

    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".scr", ".com", ".pif", ".cpl", ".msi", ".dll"
    };

    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1", ".psm1", ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".hta"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".jpeg", ".png", ".txt", ".rtf"
    };

    private readonly SignatureDatabase _signatures;

    public ScannerService(SignatureDatabase signatures)
    {
        _signatures = signatures;
    }

    public async Task<ScanSummary> ScanAsync(
        string target,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(target) && !Directory.Exists(target)) throw new IOException("Scan target is missing or inaccessible. Choose an existing file or folder.");
        var startedAt = DateTimeOffset.Now;
        var findings = new List<ScanFinding>();
        var state = new MutableScanState();
        var updates = System.Diagnostics.Stopwatch.StartNew();

        foreach (var filePath in EnumerateTargets(target, state, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var (finding, size) = await ScanFileAsync(filePath, cancellationToken).ConfigureAwait(false);
                state.FilesScanned++;
                state.BytesScanned += size;

                if (finding is not null)
                {
                    findings.Add(finding);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ScanSkippedException) { state.Skipped++; }
            catch (IOException)
            {
                state.Errors++;
            }
            catch (UnauthorizedAccessException)
            {
                state.Errors++;
            }
            catch (CryptographicException)
            {
                state.Errors++;
            }

            if (updates.ElapsedMilliseconds >= 200) {
            progress?.Report(new ScanProgress(
                filePath,
                state.FilesScanned,
                state.BytesScanned,
                findings.Count,
                state.Skipped,
                state.Errors)); updates.Restart();
            }
        }
        progress?.Report(new ScanProgress(target, state.FilesScanned, state.BytesScanned, findings.Count, state.Skipped, state.Errors));

        return new ScanSummary(
            target,
            startedAt,
            DateTimeOffset.Now,
            state.FilesScanned,
            state.BytesScanned,
            state.Skipped,
            state.Errors,
            findings);
    }

    private async Task<(ScanFinding? Finding, long Bytes)> ScanFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists) throw new IOException("File disappeared before inspection.");
        if (info.Length > MaxFileSize || IsQuarantinePath(filePath) || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new ScanSkippedException();

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length > MaxFileSize) throw new ScanSkippedException();
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();

        if (_signatures.TryMatch(sha256, out var signature) && signature is not null)
        {
            return (new ScanFinding(
                filePath,
                sha256,
                signature.Severity,
                signature.Name,
                "The SHA-256 hash matches a known signature.",
                100,
                info.Length,
                DateTimeOffset.Now), stream.Length);
        }

        if (info.Length == 0)
        {
            return (null, 0);
        }

        stream.Position = 0;
        var sampleLength = (int)Math.Min(info.Length, InspectionBytes);
        var sample = new byte[sampleLength];
        var bytesRead = 0;
        while (bytesRead < sample.Length)
        {
            var read = await stream.ReadAsync(
                sample.AsMemory(bytesRead, sample.Length - bytesRead),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            bytesRead += read;
        }

        if (bytesRead != sample.Length)
        {
            Array.Resize(ref sample, bytesRead);
        }

        return (AnalyzeHeuristics(filePath, sha256, info, sample), stream.Length);
    }

    private static ScanFinding? AnalyzeHeuristics(
        string filePath,
        string sha256,
        FileInfo info,
        byte[] sample)
    {
        var score = 0;
        var reasons = new List<string>();
        var extension = info.Extension;
        var isExecutable = ExecutableExtensions.Contains(extension);
        var isScript = ScriptExtensions.Contains(extension);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(info.Name);
        var innerExtension = Path.GetExtension(fileNameWithoutExtension);

        if ((isExecutable || isScript) && DocumentExtensions.Contains(innerExtension))
        {
            score += 45;
            reasons.Add($"misleading double extension ({innerExtension}{extension})");
        }

        if (extension.Equals(".scr", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".pif", StringComparison.OrdinalIgnoreCase))
        {
            score += 15;
            reasons.Add("uncommon executable file type");
        }

        var content = Encoding.Latin1.GetString(sample);
        if (isScript)
        {
            AddPattern(content, "-encodedcommand", 35, "encoded PowerShell command", ref score, reasons);
            AddPattern(content, "frombase64string", 20, "Base64 decoding", ref score, reasons);
            AddPattern(content, "downloadstring", 30, "downloads code into memory", ref score, reasons);
            AddPattern(content, "invoke-expression", 20, "dynamic code execution", ref score, reasons);
            AddPattern(content, "iex(", 20, "dynamic code execution", ref score, reasons);
            AddPattern(content, "invoke-mimikatz", 80, "credential theft tooling marker", ref score, reasons);
            AddPattern(content, "add-mppreference", 30, "changes Microsoft Defender preferences", ref score, reasons);
            AddPattern(content, "exclusionpath", 30, "adds an antivirus exclusion", ref score, reasons);
            AddPattern(content, "vssadmin delete shadows", 55, "deletes recovery snapshots", ref score, reasons);
            AddPattern(content, "rundll32 javascript:", 55, "rundll32 script execution", ref score, reasons);
            AddPattern(content, "mshta http", 45, "remote HTA execution", ref score, reasons);
        }

        if (isExecutable && sample.Length >= 2 && sample[0] == (byte)'M' && sample[1] == (byte)'Z')
        {
            AddPattern(content, "WriteProcessMemory", 18, "process-memory modification API", ref score, reasons);
            AddPattern(content, "CreateRemoteThread", 18, "remote-thread API", ref score, reasons);
            AddPattern(content, "VirtualAllocEx", 15, "cross-process memory allocation API", ref score, reasons);

            if (sample.Length >= 64 * 1024 && CalculateEntropy(sample) >= 7.65)
            {
                score += 22;
                reasons.Add("very high entropy (possibly packed or encrypted)");
            }
        }

        if (score < 35)
        {
            return null;
        }

        var severity = score >= 70 ? DetectionSeverity.High : DetectionSeverity.Suspicious;
        var name = severity == DetectionSeverity.High
            ? "Heuristic.HighRisk"
            : "Heuristic.Suspicious";

        return new ScanFinding(
            filePath,
            sha256,
            severity,
            name,
            string.Join("; ", reasons.Distinct(StringComparer.OrdinalIgnoreCase)),
            Math.Min(score, 99),
            info.Length,
            DateTimeOffset.Now);
    }

    private static void AddPattern(
        string content,
        string pattern,
        int weight,
        string reason,
        ref int score,
        List<string> reasons)
    {
        if (!content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        score += weight;
        reasons.Add(reason);
    }

    private static double CalculateEntropy(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        Span<int> frequencies = stackalloc int[256];
        frequencies.Clear();
        foreach (var value in data)
        {
            frequencies[value]++;
        }

        var entropy = 0d;
        foreach (var frequency in frequencies)
        {
            if (frequency == 0)
            {
                continue;
            }

            var probability = (double)frequency / data.Length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }

    private static IEnumerable<string> EnumerateTargets(string target, MutableScanState state, CancellationToken token)
    {
        if (File.Exists(target))
        {
            try
            {
                if (new FileInfo(target).Length > MaxFileSize || (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                {
                    state.Skipped++;
                    yield break;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                state.Errors++;
                yield break;
            }

            if (!IsQuarantinePath(target))
            {
                yield return target;
            }
            yield break;
        }

        if (!Directory.Exists(target))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(target);

        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if (IsQuarantinePath(directory))
            {
                state.Skipped++;
                continue;
            }

            string[] files;
            string[] directories;
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    state.Skipped++;
                    continue;
                }
                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            }
            catch (UnauthorizedAccessException)
            {
                state.Errors++;
                continue;
            }
            catch (IOException)
            {
                state.Errors++;
                continue;
            }

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                FileInfo info;
                try
                {
                    info = new FileInfo(file);
                    if (info.Length > MaxFileSize ||
                        info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        state.Skipped++;
                        continue;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    state.Errors++;
                    continue;
                }

                yield return file;
            }

            foreach (var child in directories)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    {
                        state.Skipped++;
                        continue;
                    }

                    pending.Push(child);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    state.Errors++;
                }
            }
        }
    }

    private static bool IsQuarantinePath(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var quarantine = Path.GetFullPath(SecurityPaths.Quarantine)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(quarantine, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ScanSkippedException : Exception { }

    private sealed class MutableScanState
    {
        public long FilesScanned { get; set; }
        public long BytesScanned { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
    }
}
