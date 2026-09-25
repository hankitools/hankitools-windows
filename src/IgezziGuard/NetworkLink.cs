using System.Text.Json;
namespace IgezziGuard;

/// <param name="Media">Physical medium as Windows reports it: "802.3" for Ethernet, "Native 802.11" for Wi-Fi.</param>
/// <param name="Bps">Negotiated receive link speed in bits per second.</param>
public sealed record LinkAdapter(string Name, string Description, string Media, double Bps, bool? FullDuplex);
internal sealed record NetworkLinkFacts(LinkAdapter[]? Adapters, string? Notes);

/// <summary>
/// The speed each connected network adapter negotiated (HANKI-FIX-121). A wired link at 100 Mbps or in half duplex
/// usually means a damaged cable, a loose plug or an old port. Read-only, standard user, no traffic sent.
/// </summary>
internal static class NetworkLink
{
    internal const string Script = """
        $notes=@();$adapters=@()
        try{$adapters=@(Get-NetAdapter -Physical -ErrorAction Stop|Where-Object {$_.Status -eq 'Up'}|ForEach-Object{[pscustomobject]@{Name=[string]$_.Name;Description=[string]$_.InterfaceDescription;Media=[string]$_.PhysicalMediaType;Bps=[double]$_.ReceiveLinkSpeed;FullDuplex=$_.FullDuplex}})}catch{$notes+='adapters'}
        [pscustomobject]@{Adapters=$adapters;Notes=($notes -join ', ')}|ConvertTo-Json -Depth 4 -Compress
        """;
    internal static NetworkLinkFacts Parse(string json) =>
        JsonSerializer.Deserialize<NetworkLinkFacts>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new IOException("Network adapter data unavailable.");
    internal static bool Wired(LinkAdapter a) => a.Media.Equals("802.3", StringComparison.OrdinalIgnoreCase);
    internal static bool Wireless(LinkAdapter a) => a.Media.Contains("802.11", StringComparison.OrdinalIgnoreCase);
    internal static string Rate(double bps) => bps >= 1e9 ? $"{bps / 1e9:0.#} Gbps" : $"{bps / 1e6:0} Mbps";

    internal static IReadOnlyList<HealthItem> Evaluate(NetworkLinkFacts facts)
    {
        if ((facts.Notes ?? "").Contains("adapters", StringComparison.Ordinal))
            return [new("link", "Network connection speed", CardStatus.Unknown, "Windows didn't report its network adapters.", "Get-NetAdapter unavailable")];
        var up = (facts.Adapters ?? []).Where(a => a.Bps > 0 && (Wired(a) || Wireless(a))).ToArray();
        if (up.Length == 0)
            return [new("link", "Network connection speed", CardStatus.Info, "No wired or Wi-Fi adapter is connected right now.", "No connected physical adapters")];
        var items = new List<HealthItem>();
        foreach (var a in up) {
            string id = "link:" + a.Name.ToLowerInvariant(), rate = Rate(a.Bps), evidence = $"{a.Description}: {a.Media}, {rate}, full duplex {a.FullDuplex?.ToString() ?? "unknown"}";
            if (Wired(a)) {
                if (a.Bps <= 10e6)
                    items.Add(new(id, $"{a.Name} runs at only {rate}", CardStatus.Problem,
                        $"The wired connection negotiated just {rate}, far below the 1 Gbps that current network cards and routers use. A damaged cable or a failing port is the usual reason: try another cable and another port on the router.", evidence));
                else if (a.Bps <= 100e6)
                    items.Add(new(id, $"{a.Name} runs at {rate}", CardStatus.Review,
                        $"The wired connection negotiated {rate}. Current network cards, routers and switches run at 1 Gbps or more, so this usually means a damaged or old cable (a broken wire, or Cat 5), a loose plug, or a 100 Mbps port on the router or switch. If your internet plan is faster than {rate}, you aren't getting all of it. Try another cable and another port.", evidence));
                else if (a.FullDuplex == false)
                    items.Add(new(id, $"{a.Name} runs in half duplex", CardStatus.Review,
                        $"The wired connection runs at {rate} but can't send and receive at the same time (half duplex), which slows it down and causes errors. That usually comes from a cable or a port that negotiated badly: try another cable and port.", evidence));
                else
                    items.Add(new(id, $"{a.Name} speed", CardStatus.Good, $"The wired connection runs at {rate}, full duplex.", evidence));
            } else if (a.Bps < 50e6)
                items.Add(new(id, $"{a.Name} link is slow", CardStatus.Review,
                    $"Wi-Fi is connected at a link rate of only {rate}. Real speeds are lower still. Distance, walls and interference are the usual reasons: move closer to the router, or compare the router with the internet in Connect → Wi-Fi / latency.", evidence));
            else
                items.Add(new(id, $"{a.Name} speed", CardStatus.Info,
                    $"Wi-Fi is connected at a link rate of {rate}. That's the most the Wi-Fi link can carry right now; real speeds are lower and drop with distance and walls.", evidence));
        }
        return items;
    }
}

/// <summary>Full scan module: the negotiated speed of connected network adapters.</summary>
internal sealed class NetworkLinkDiagnostic(IDiagnosticProbe? source = null) : IDiagnosticModule
{
    private readonly IDiagnosticProbe source = source ?? new WindowsDiagnosticProbe();
    public string Id => "network-link";
    public string DisplayName => "Network connection speed";
    public DiagnosticCategory Category => DiagnosticCategory.Network;
    public DiagnosticRequirements Requirements => new();
    public async Task<IReadOnlyList<DiagnosticResult>> CollectAsync(DiagnosticContext context, IProgress<DiagnosticProgress>? progress, CancellationToken token)
    {
        var start = DateTimeOffset.UtcNow; progress?.Report(new(Id, "Reading network adapter speeds"));
        var facts = NetworkLink.Parse(await source.ReadAsync(NetworkLink.Script, 60, token));
        return HealthItems.Findings(Id, Category, NetworkLink.Evaluate(facts), start, DateTimeOffset.UtcNow);
    }
}
