using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace IgezziGuard;

/// <summary>What the basic connection check observed. Null means the step did not run or could not complete.</summary>
public sealed record ConnectionFacts(int ActiveAdapters, string? Adapter, bool HasIPv4, bool HasGateway, bool VpnActive,
    bool? DnsWorks, long? DnsMs, bool? IpReachable, long? IpMs, bool? NameReachable, long? NameMs,
    string NameHost = "www.microsoft.com", IReadOnlyList<string>? DnsFailures = null);

public sealed record PingStats(string Target, bool Gateway, int Sent, IReadOnlyList<long> Replies)
{
    public double LossPercent => Sent == 0 ? 100 : 100d * (Sent - Replies.Count) / Sent;
    public double? Average => Replies.Count == 0 ? null : Replies.Average();
}

public static class NetworkDiagnostics
{
    internal static readonly string[] DnsTestNames = ["www.microsoft.com", "cloudflare.com", "example.com"];
    public static async Task<Diagnosis> Check(CancellationToken token)
    {
        var report = new StringBuilder($"Hanki Connect • {DateTimeOffset.Now:g}\r\nRead-only checks; failed probes alone do not establish the cause.\r\n\r\n");
        int active = 0; string? primary = null; bool ipv4 = false, gateway = false, vpn = false;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            token.ThrowIfCancellationRequested();
            if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            report.AppendLine($"ADAPTER: {adapter.Name} — {adapter.Description}\r\nState: {adapter.OperationalStatus}; type: {adapter.NetworkInterfaceType}");
            try {
                var ip = adapter.GetIPProperties();
                report.AppendLine("Addresses: " + string.Join(", ", ip.UnicastAddresses.Select(a => a.Address.ToString())));
                report.AppendLine("Gateways: " + string.Join(", ", ip.GatewayAddresses.Select(a => a.Address.ToString())));
                report.AppendLine("DNS: " + string.Join(", ", ip.DnsAddresses.Select(a => a.ToString())) + "\r\n");
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                active++;
                if (LooksLikeVpn(adapter.Description, adapter.NetworkInterfaceType)) { vpn = true; continue; }
                bool hasGateway = ip.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                bool hasIpv4 = ip.UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal));
                if (primary is null || (hasGateway && !gateway)) { primary = $"{adapter.Name} ({Kind(adapter.NetworkInterfaceType)})"; }
                gateway |= hasGateway; ipv4 |= hasIpv4;
            }
            catch (NetworkInformationException ex) { report.AppendLine("Adapter details unavailable: " + ex.Message + "\r\n"); }
        }
        token.ThrowIfCancellationRequested();
        // Several names: one domain can be filtered by a router or provider while DNS itself works.
        var resolved = new List<string>(); var failures = new List<string>(); long? dnsMs = null;
        var watch = new Stopwatch();
        foreach (var name in DnsTestNames) {
            token.ThrowIfCancellationRequested(); watch.Restart();
            try {
                var addresses = await Dns.GetHostAddressesAsync(name, token).WaitAsync(TimeSpan.FromSeconds(5), token);
                if (addresses.Length == 0) { failures.Add(name); continue; }
                resolved.Add(name); dnsMs ??= watch.ElapsedMilliseconds;
                report.AppendLine($"DNS {name} ({watch.ElapsedMilliseconds} ms, may be cached): " + string.Join(", ", addresses.Select(a => a.ToString())));
            }
            catch (Exception ex) when (ex is SocketException or TimeoutException) { failures.Add(name); report.AppendLine($"DNS {name} failed/inconclusive: " + ex.Message); }
        }
        bool? dns = resolved.Count > 0;
        var (ip443, ipMs) = await Tcp("1.1.1.1", report, token);
        string host = resolved.FirstOrDefault() ?? DnsTestNames[0];
        var (name443, nameMs) = dns == true ? await Tcp(host, report, token) : (null, null);
        report.AppendLine("\r\nFinished. VPNs, proxies and multiple adapters can affect results. No settings changed.");
        return ConnectionVerdict.Evaluate(new(active, primary, ipv4, gateway, vpn, dns, dnsMs, ip443, ipMs, name443, nameMs, host, failures), report.ToString());
    }
    private static async Task<(bool? Ok, long? Ms)> Tcp(string host, StringBuilder report, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var tcp = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var watch = Stopwatch.StartNew();
        try {
            await tcp.ConnectAsync(host, 443, timeout.Token);
            report.AppendLine($"TCP {host}:443 reachable in {watch.ElapsedMilliseconds} ms. Not an HTTPS or general internet health test.");
            return (true, watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { report.AppendLine($"TCP {host}:443 timed out. Firewall, routing or endpoint issues are possible."); return (false, null); }
        catch (SocketException ex) { report.AppendLine($"TCP {host}:443 failed: {ex.SocketErrorCode}"); return (false, null); }
    }
    internal static bool LooksLikeVpn(string description, NetworkInterfaceType type) =>
        type == NetworkInterfaceType.Ppp || Regex.IsMatch(description, @"\b(VPN|TAP|TUN|WireGuard|OpenVPN|Tunnel|AnyConnect|GlobalProtect|Fortinet|NordLynx|Tailscale|ZeroTier)\b", RegexOptions.IgnoreCase);
    private static string Kind(NetworkInterfaceType type) => type switch {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi", NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "Ethernet",
        NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "mobile broadband", _ => "network" };

    /// <summary>English netsh output only; other languages leave these values unknown.</summary>
    internal static (int? Signal, string? Ssid, string? Band) ParseWifi(string netsh)
    {
        string? Field(string name) { var m = Regex.Match(netsh, @"^\s*" + name + @"\s*:\s*(.+?)\s*$", RegexOptions.Multiline); return m.Success ? m.Groups[1].Value : null; }
        int? signal = Field("Signal") is { } text && int.TryParse(text.TrimEnd('%'), out var value) && value is >= 0 and <= 100 ? value : null;
        return (signal, Field("SSID"), Field("Band"));
    }
}

public static class ConnectionVerdict
{
    public static Diagnosis Evaluate(ConnectionFacts f, string report)
    {
        var cards = new List<ResultCard>();
        string vpnNote = f.VpnActive ? "\nA VPN or tunnel adapter is active. It can change routes and DNS, so results may differ without it." : "";
        cards.Add(f.ActiveAdapters == 0 ? new("Network adapter", "Windows reports no connected Wi-Fi or Ethernet adapter.", CardStatus.Problem)
            : !f.HasGateway ? new("Network adapter", $"{f.Adapter ?? "An adapter"} is connected, but has no router (default gateway). Without one, this PC cannot reach the internet." + vpnNote, CardStatus.Problem)
            : !f.HasIPv4 ? new("Network adapter", $"{f.Adapter} has a router but no usable IPv4 address. The router may not have handed out an address (DHCP)." + vpnNote, CardStatus.Review)
            : new("Network adapter", $"{f.Adapter} is connected with an address and a router." + vpnNote, CardStatus.Good));
        var dnsFailures = f.DnsFailures ?? [];
        string filtered = f.DnsWorks == true && dnsFailures.Count > 0
            ? $"\nYour DNS server couldn't find {string.Join(" or ", dnsFailures)}, while other names worked. Some routers and providers filter certain names; if a specific site won't load, try Network tools → Compare DNS." : "";
        cards.Add(f.DnsWorks switch {
            true when f.DnsMs > 500 => new("Name lookups (DNS)", $"Websites can be found by name, but the lookup took {f.DnsMs} ms. Over half a second can make pages feel slow to start." + filtered, CardStatus.Review),
            true => new("Name lookups (DNS)", $"Website names are being translated to addresses ({f.DnsMs} ms; may come from cache)." + filtered, dnsFailures.Count > 0 ? CardStatus.Info : CardStatus.Good),
            false => new("Name lookups (DNS)", "Looking up several well-known website names failed or timed out. Websites will not load by name even if the internet itself works.", CardStatus.Problem),
            null => new("Name lookups (DNS)", "Not tested.", CardStatus.Unknown)
        });
        var reach = new List<string>();
        if (f.IpReachable is { } ip) reach.Add(ip ? $"Cloudflare (1.1.1.1) answered in {f.IpMs} ms." : "Cloudflare (1.1.1.1) did not answer.");
        if (f.NameReachable is { } name) reach.Add(name ? $"{f.NameHost} answered in {f.NameMs} ms." : $"{f.NameHost} did not answer.");
        bool anyOut = f.IpReachable == true || f.NameReachable == true, noneOut = f.IpReachable == false && f.NameReachable != true;
        cards.Add(new("Internet reachability", reach.Count == 0 ? "Not tested." : string.Join("\n", reach) + "\nThis is a connection test to two well-known servers, not a speed test.",
            reach.Count == 0 ? CardStatus.Unknown : anyOut && f.IpReachable != false && f.NameReachable != false ? CardStatus.Good : anyOut ? CardStatus.Review : CardStatus.Problem));

        (string headline, string next) = f switch {
            { ActiveAdapters: 0 } => ("You're not connected to a network",
                "Turn on Wi-Fi or plug in the network cable, and make sure airplane mode is off. Then run this check again."),
            { HasGateway: false } => ("Connected to a network, but not to a router",
                "Reconnect to your Wi-Fi network or unplug and reconnect the cable. If it keeps happening, restart your router."),
            _ when noneOut => ("No internet access right now",
                "Your PC reaches the router, but nothing beyond it. Restart your router or modem. If you're on hotel or café Wi-Fi, open a browser to finish signing in. If it continues, your internet provider may have an outage."),
            { DnsWorks: false } when anyOut => ("Your internet works, but website names aren't resolving",
                "This is a DNS problem. Restart your router first. If it persists, use Network tools → Compare DNS, and consider Use Cloudflare IPv4 DNS (it can be undone in Recovery). A VPN or company network can also cause this."),
            { DnsWorks: true, IpReachable: false, NameReachable: true } => ("You're online",
                "Cloudflare's address was blocked, which is common on school, work or filtered networks. Everything else looks fine."),
            { DnsWorks: true, NameReachable: false } => ("Online, but some servers didn't answer",
                "A firewall, filter or VPN may be blocking some connections. If websites work, you can ignore this; otherwise try Wi-Fi / latency and Network tools."),
            { DnsWorks: true } when f.DnsMs > 500 => ("You're online, but name lookups are slow",
                "Try Network tools → Compare DNS to see if another DNS server answers faster."),
            { DnsWorks: true, IpReachable: true } => ("You're online",
                "Basic connectivity works. If things still feel slow, run Wi-Fi / latency to compare your router with the internet, or the speed test in Network tools."),
            _ => ("The connection check was incomplete", "Run the check again. If it keeps failing, restart your router and check any VPN or firewall software.")
        };
        cards.Add(new("What to do next", next));
        return Diagnosis.From(report, cards, headline);
    }

    /// <summary>Compares the router (local link) with the internet path to say where delay or loss starts.</summary>
    public static Diagnosis Latency(IReadOnlyList<PingStats> probes, (int? Signal, string? Ssid, string? Band) wifi, string report)
    {
        var cards = new List<ResultCard>();
        var router = probes.Where(p => p.Gateway).OrderBy(p => p.LossPercent).FirstOrDefault();
        var internet = probes.FirstOrDefault(p => !p.Gateway);
        if (wifi.Signal is { } signal) {
            string band = wifi.Band is null ? "" : $" on {wifi.Band}";
            cards.Add(signal < 40 ? new("Wi-Fi signal", $"Signal is weak ({signal}%){band}. Weak signal causes drop-outs and slow speeds. Move closer to the router, remove obstacles, or use a cable.", CardStatus.Review)
                : signal < 70 ? new("Wi-Fi signal", $"Signal is fair ({signal}%){band}. Usually fine for browsing and video; getting closer to the router can help for games and calls.", CardStatus.Info)
                : new("Wi-Fi signal", $"Signal is strong ({signal}%){band}.", CardStatus.Good));
        }
        else cards.Add(new("Wi-Fi signal", "Not available. This PC may be on Ethernet, Wi-Fi may be off, or Windows reported it in another language (see technical details).", CardStatus.Info));
        string Describe(PingStats p) => p.Average is { } avg ? $"average {avg:0} ms, {p.LossPercent:0}% no reply" : "no replies";
        bool routerBad = router is not null && router.Replies.Count > 0 && (router.LossPercent >= 20 || router.Average > 50);
        bool routerSilent = router is not null && router.Replies.Count == 0;
        bool internetBad = internet is not null && (internet.LossPercent >= 20 || internet.Average > 150 || internet.Replies.Count == 0);
        cards.Add(router is null ? new("Your router", "No router (gateway) was found to test.", CardStatus.Unknown)
            : routerSilent ? new("Your router", "Your router did not answer test pings. Many routers ignore them, so this alone is not a problem.", CardStatus.Info)
            : new("Your router", $"{Describe(router)}. " + (routerBad ? "That's slow or unstable for a local connection; healthy home links usually answer within a few milliseconds." : "The link between this PC and your router looks healthy."), routerBad ? CardStatus.Review : CardStatus.Good));
        cards.Add(internet is null ? new("The internet", "Not tested.", CardStatus.Unknown)
            : new("The internet", $"Cloudflare (1.1.1.1): {Describe(internet)}. " + (internet.Replies.Count == 0 ? "No answers came back; pings may be blocked on this network." : internetBad ? "Responses beyond your router are slow or dropping." : "Response times beyond your router look normal."),
                internet.Replies.Count == 0 ? CardStatus.Unknown : internetBad ? CardStatus.Review : CardStatus.Good));
        (string headline, string next) =
            routerBad ? ("The slowdown starts between this PC and your router", "This points to Wi-Fi or local network trouble. Move closer to the router, switch to 5 GHz if available, restart the router, or try a network cable to compare.")
            : internetBad && internet!.Replies.Count > 0 ? ("Your router is fine; the delay is further out", "Your local link looks healthy, so the slowdown is more likely at your internet provider or beyond. Test again at a different time of day; if it persists, contact your provider.")
            : wifi.Signal < 40 ? ("Weak Wi-Fi signal", "Latency looks acceptable right now, but a weak signal often causes drop-outs. Moving closer to the router or using a cable usually helps.")
            : internet is { Replies.Count: 0 } ? ("Internet pings were not answered", "This network may block pings. Run the basic connection check instead; if websites load, you can ignore this.")
            : ("Response times look healthy", "Both your router and the internet answered quickly. For bandwidth, run the speed test in Network tools.");
        cards.Add(new("What to do next", next + "\nPings are a small sample and can be deprioritised; they are clues, not proof."));
        return Diagnosis.From(report, cards, headline);
    }

    /// <summary>Plain meaning for a measured download speed in megabits per second.</summary>
    internal static (CardStatus Status, string Meaning) Speed(double mbps) => mbps switch {
        < 5 => (CardStatus.Review, "Enough for web browsing and email; video may buffer."),
        < 25 => (CardStatus.Info, "Enough for HD video on one or two devices."),
        < 100 => (CardStatus.Good, "Enough for several HD streams, video calls and most downloads."),
        _ => (CardStatus.Good, "Fast — 4K streaming and large downloads on several devices.")
    };
}
