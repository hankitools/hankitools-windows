using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace IgezziGuard;

public static partial class DiagnosticPrivacy
{
    // Opt-in exports still require human review. Pattern masking is not proof that arbitrary logs are anonymous.
    public static string Redact(string text) => Secret().Replace(Authorization().Replace(AssistantPrompt.MaskCommon(text), "$1[redacted]"), "$1[redacted]");
    [GeneratedRegex(@"(?im)(authorization\s*[:=]\s*)(?:bearer\s+)?[^\r\n,]+") ]
    private static partial Regex Authorization();
    [GeneratedRegex(@"(?i)(\b(?:authorization|api[_-]?key|password|token|secret)\s*[:=]\s*)([^\s,;]+)")]
    private static partial Regex Secret();
    public static DiagnosticResult Minimize(DiagnosticResult r) => new(r.ModuleId,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(r.FindingId))), r.Category, r.Outcome, r.Severity,
        r.Title, "Local result retained without raw evidence. Re-run the individual check for current details.", r.Started, r.Ended,
        coverage: "Raw paths, network identifiers, device identifiers and report text omitted from saved history.", confidence: r.Confidence);
    public static DiagnosticScan Minimize(DiagnosticScan scan) => scan with { Results = scan.Results.Select(Minimize).ToArray() };
    public static RepairAttempt Minimize(RepairAttempt a) => a with {
        Before = a.Before.Select(Minimize).ToArray(), After = a.After.Select(Minimize).ToArray(), Explanation = Redact(a.Explanation)
    };
}
