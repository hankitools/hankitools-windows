using System.Globalization;
using System.Text;
using System.Text.Json;

namespace IgezziGuard;

internal static class DefenderAuditSummary
{
    public static string Format(string json, string? toolNotes = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var text = new StringBuilder("DEFENDER • PROTECTION REVIEW\r\n\r\n");
        var status = Get(root, "Status"); var preferences = Get(root, "Preferences");
        foreach (var (key, label) in new[] { ("AntivirusEnabled", "Antivirus"), ("RealTimeProtectionEnabled", "Real-time protection"), ("BehaviorMonitorEnabled", "Behavior monitoring"), ("IoavProtectionEnabled", "Downloaded-file protection"), ("NISEnabled", "Network inspection"), ("IsTamperProtected", "Tamper protection") })
            text.AppendLine($"{label}: {State(Get(status, key))}");
        text.AppendLine($"\r\nRunning mode: {Value(Get(status, "AMRunningMode"))}");
        text.AppendLine($"Signature version: {Value(Get(status, "AntivirusSignatureVersion"))}");
        text.AppendLine($"Signatures updated: {Date(Value(Get(status, "AntivirusSignatureLastUpdated")))}");
        if (Get(root, "StatusError").ValueKind == JsonValueKind.String) text.AppendLine("Status could not be read: " + Get(root, "StatusError").GetString());
        text.AppendLine("\r\nCONFIGURED PREFERENCES");
        foreach (var (key, label) in new[] { ("DisableRealtimeMonitoring", "Real-time monitoring"), ("DisableBehaviorMonitoring", "Behavior monitoring"), ("DisableIOAVProtection", "Downloaded-file scanning"), ("DisableScriptScanning", "Script scanning") })
            text.AppendLine($"{label}: {State(Get(preferences, key), inverted: true)}");
        text.AppendLine("Preferences describe configuration; the current status above describes reported protection.");
        text.AppendLine("\r\nEXCLUSIONS • REVIEW ACCESS");
        foreach (var key in new[] { "ExclusionPath", "ExclusionProcess", "ExclusionExtension", "ExclusionIpAddress" }) {
            var field = Get(preferences, key);
            var values = field.ValueKind == JsonValueKind.Array ? field.EnumerateArray().Select(Value).ToArray() : new[] { Value(field) };
            bool restricted = values.Any(v => v.Contains("administrator", StringComparison.OrdinalIgnoreCase) || v.StartsWith("N/A", StringComparison.OrdinalIgnoreCase));
            text.AppendLine($"{key}: " + (restricted ? "Administrator access required — exclusions are unknown." : field.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || values.Length == 0 ? "No entries returned — absence is not verified." : string.Join(", ", values)));
        }
        if (Get(root, "PreferencesError").ValueKind == JsonValueKind.String) text.AppendLine("Preferences could not be read: " + Get(root, "PreferencesError").GetString());
        text.AppendLine("\r\nIf access is restricted, reopen Hanki as administrator and run the audit again to review exclusions. No settings were changed. Enabled protection does not prove the PC is free of threats.");
        text.AppendLine("\r\nRAW EVIDENCE • REVIEW BEFORE SHARING\r\n" + json);
        if (!string.IsNullOrWhiteSpace(toolNotes)) text.AppendLine("\r\nTool notes (stderr):\r\n" + toolNotes);
        return text.ToString();
    }
    private static JsonElement Get(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var result) ? result : default;
    private static string Value(JsonElement value) => value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? "Not available" : value.ToString();
    private static string State(JsonElement value, bool inverted = false) => value.ValueKind is JsonValueKind.True or JsonValueKind.False ? (value.GetBoolean() != inverted ? "Enabled" : "Disabled — review recommended") : "Unknown — not reported";
    internal static string Date(string value)
    {
        if (value.StartsWith("/Date(", StringComparison.Ordinal) && value.EndsWith(")/", StringComparison.Ordinal)) {
            var match = System.Text.RegularExpressions.Regex.Match(value, @"^/Date\((-?\d+)(?:[+-]\d{4})?\)/$");
            if (match.Success && long.TryParse(match.Groups[1].Value, out long milliseconds)) {
                try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToLocalTime().ToString("f", CultureInfo.CurrentCulture); }
                catch (ArgumentOutOfRangeException) { }
            }
        }
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date.ToLocalTime().ToString("f", CultureInfo.CurrentCulture);
        return "Not available";
    }
}
