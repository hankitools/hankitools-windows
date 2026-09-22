using System.Text.Json.Serialization;

namespace IgezziGuard;

[JsonConverter(typeof(JsonStringEnumConverter<CollectionOutcome>))]
public enum CollectionOutcome { Completed, Partial, Unavailable, Failed, Cancelled }
[JsonConverter(typeof(JsonStringEnumConverter<FindingSeverity>))]
public enum FindingSeverity { Unknown, Healthy, Informational, Warning, Critical }
[JsonConverter(typeof(JsonStringEnumConverter<FindingConfidence>))]
public enum FindingConfidence { Unknown, Possible, Likely, Confirmed }
[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticCategory>))]
public enum DiagnosticCategory { Windows, Storage, Network, Devices, Security, Performance }

/// <summary>Evidence, not executable instructions. Outcome and machine health are independent.</summary>
public sealed record DiagnosticResult
{
    public string ModuleId { get; }
    public string FindingId { get; }
    public DiagnosticCategory Category { get; }
    public CollectionOutcome Outcome { get; }
    public FindingSeverity Severity { get; }
    public string Title { get; }
    public string Explanation { get; }
    public string Evidence { get; }
    public string Coverage { get; }
    public DateTimeOffset Started { get; }
    public DateTimeOffset Ended { get; }
    public FindingConfidence Confidence { get; }
    public string? Recommendation { get; }
    public string? RepairActionId { get; }
    public bool AutomatedRepairAvailable => RepairActionId is not null;
    public IReadOnlyDictionary<string, string> Metadata { get; }

    [JsonConstructor]
    public DiagnosticResult(string moduleId, string findingId, DiagnosticCategory category,
        CollectionOutcome outcome, FindingSeverity severity, string title, string explanation,
        DateTimeOffset started, DateTimeOffset ended, string evidence = "", string coverage = "",
        FindingConfidence confidence = FindingConfidence.Unknown, string? recommendation = null,
        string? repairActionId = null, IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(findingId) || string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Stable identity and title are required.");
        if (!Enum.IsDefined(category) || !Enum.IsDefined(outcome) || !Enum.IsDefined(severity) || !Enum.IsDefined(confidence))
            throw new ArgumentException("Unsupported diagnostic state.");
        if (started == default || ended < started) throw new ArgumentException("Invalid collection times.");
        if (outcome != CollectionOutcome.Completed && severity == FindingSeverity.Healthy)
            throw new ArgumentException("Incomplete collection cannot establish health.");
        if (outcome is CollectionOutcome.Unavailable or CollectionOutcome.Failed or CollectionOutcome.Cancelled && severity != FindingSeverity.Unknown)
            throw new ArgumentException("No machine-health conclusion is available for this outcome.");
        ModuleId = moduleId; FindingId = findingId; Category = category; Outcome = outcome; Severity = severity;
        Title = title; Explanation = explanation; Started = started; Ended = ended; Evidence = evidence;
        Coverage = coverage; Confidence = confidence; Recommendation = recommendation;
        RepairActionId = string.IsNullOrWhiteSpace(repairActionId) ? null : repairActionId;
        Metadata = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            metadata is null ? new Dictionary<string, string>() : new Dictionary<string, string>(metadata));
    }
}
