using System.Text.Json;

namespace IgezziGuard;

public static class DefenderReview
{
    public static string Alerts(JsonElement status)
    {
        var issues = new List<string>();
        foreach (var name in new[] { "AMServiceEnabled", "AntivirusEnabled", "RealTimeProtectionEnabled", "BehaviorMonitorEnabled", "IsTamperProtected" }) {
            if (!status.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) issues.Add(name + ": unknown");
            else if (!value.GetBoolean()) issues.Add(name + ": OFF — review Windows Security / administrator policy");
        }
        if (status.TryGetProperty("AntivirusSignatureAge", out var age) && age.TryGetInt32(out int days) && days > 7) issues.Add($"Definitions reported {days} days old — review updates");
        return issues.Count == 0 ? "Selected protection flags are on. This is not proof the PC is malware-free." : string.Join("\r\n", issues);
    }
}
