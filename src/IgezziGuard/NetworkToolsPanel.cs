using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IgezziGuard;

public sealed class NetworkToolsPanel : ToolPage
{
    private readonly TextBox host = new() { Text = "example.com", Width = 230 };
    private readonly ComboBox adapters = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
    private List<NetworkInterface> interfaces = [];
    public NetworkToolsPanel() : base("Trace routes, compare DNS resolvers, test bounded transfer throughput, and repair IPv4 DNS with undo. Targets/resolvers see your source IP and requested hostname. Speed tests use Cloudflare and transfer up to 25 MiB down plus 10 MiB up. VPNs, filtering, server load and route asymmetry affect results. No network reset or firewall changes.") {
        Bar.Controls.Add(host);
        Button("Trace route", async () => { try { var name = ValidateHost(host.Text); if (Review($"Trace {name} using up to 20 ICMP TTL probes? The destination/network can observe probes; local DNS resolves the hostname.")) await Run(t => Trace(name, t)); } catch (Exception ex) { Output.Text = ex.Message; } });
        Button("Compare DNS", async () => { try { var name = ValidateHost(host.Text); if (IPAddress.TryParse(name, out _)) throw new ArgumentException("Enter a DNS hostname for DNS comparisons."); if (Review($"Query A records for {name} three times each using configured DNS, Cloudflare 1.1.1.1 and Google 8.8.8.8? Public resolvers receive this hostname. Cached results can affect timings.")) await Run(t => CompareDns(name, t)); } catch (Exception ex) { Output.Text = ex.Message; } });
        Button("Speed test (35 MiB max)", async () => { if (Review("Send a bounded speed test to speed.cloudflare.com?\nUp to 25 MiB download and 10 MiB upload plus protocol overhead. This uses bandwidth and may incur metered-data charges. Cloudflare sees your source IP. Each direction has a 25-second timeout; no automatic retry. Results are approximate single-request throughput, not line capacity.")) await Run(Speed); });
        Bar.Controls.Add(adapters);
        Button("Refresh adapters", () => { try { interfaces = NetworkInterface.GetAllNetworkInterfaces().Where(a => Guid.TryParse(a.Id, out _) && a.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToList(); adapters.Items.Clear(); foreach (var a in interfaces) adapters.Items.Add(a.Name + " / " + a.OperationalStatus); } catch (Exception ex) { Output.Text = ex.Message; } });
        Button("Restore automatic IPv4 DNS", () => ChangeDns(""));
        Button("Use Cloudflare IPv4 DNS", () => ChangeDns("1.1.1.1,1.0.0.1"));
    }
    internal static string ValidateHost(string value) { value = value.Trim(); if (value.Length > 253 || Uri.CheckHostName(value) is UriHostNameType.Unknown or UriHostNameType.IPv6) throw new ArgumentException("Enter a plain DNS hostname or IPv4 address, without scheme, port, spaces or path."); return value; }
    private async void ChangeDns(string after) {
        if (adapters.SelectedIndex < 0) return; var adapter = interfaces[adapters.SelectedIndex]; var target = Guid.Parse(adapter.Id).ToString();
        try {
            var before = await new WindowsSettings().Read("IPv4 DNS", target, CancellationToken.None);
            if (!Review($"Change IPv4 DNS for {adapter.Name}?\nBefore: {(before.Length == 0 ? "Automatic" : before)}\nAfter: {(after.Length == 0 ? "Automatic" : after)}\n\nMay interrupt name resolution or break private/corporate names. Public DNS receives future queries. IPv6 DNS is not changed. Policy/VPN settings can take precedence. Recovery stores the original configuration. Administrator rights may be required.")) return;
            await Run(async t => { await WindowsSettings.Journal().Apply("IPv4 DNS", target, before, after, t); return "IPv4 DNS setting applied and verified. Re-test connectivity. Undo in Recovery. This does not override VPN/NRPT or prove the network is fixed."; });
        } catch (Exception ex) { Output.Text = ex.Message; }
    }
    private static async Task<string> Trace(string host, CancellationToken token) {
        var addresses = await Dns.GetHostAddressesAsync(host, token); var destination = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? throw new IOException("No IPv4 destination.");
        var lines = new List<string> { $"Traceroute {host} → {destination}, {DateTimeOffset.Now:O}", "One probe per hop, 20-hop maximum. Missing ICMP replies do not prove a broken route; return paths can differ." };
        using var ping = new Ping();
        for (int ttl = 1; ttl <= 20; ttl++) { token.ThrowIfCancellationRequested(); try {
            var reply = await ping.SendPingAsync(destination, 1000, new byte[32], new PingOptions(ttl, true)).WaitAsync(token);
            lines.Add($"{ttl,2}  {reply.Address}  {reply.Status}" + (reply.Status == IPStatus.Success ? $"  {reply.RoundtripTime} ms" : ""));
            if (reply.Status == IPStatus.Success) break;
        } catch (PingException ex) { lines.Add($"{ttl}: probe unavailable ({ex.InnerException?.Message ?? ex.Message})"); } }
        return string.Join("\r\n", lines);
    }
    private static Task<string> CompareDns(string name, CancellationToken token) => WindowsCommand.PowerShell(
        "$name=" + WindowsCommand.Quote(name) + "; $results=@(foreach($server in @('Configured','1.1.1.1','8.8.8.8')){ for($i=1;$i -le 3;$i++){ $watch=[Diagnostics.Stopwatch]::StartNew(); try{$params=@{Name=$name;Type='A';DnsOnly=$true;NoHostsFile=$true;QuickTimeout=$true};if($server -ne 'Configured'){$params.Server=$server};$answer=Resolve-DnsName @params;$watch.Stop();[pscustomobject]@{Resolver=$server;Attempt=$i;Milliseconds=$watch.ElapsedMilliseconds;Addresses=(@($answer | Where-Object Type -eq 'A' | Select-Object -ExpandProperty IPAddress) -join ',');Error=$null}}catch{$watch.Stop();[pscustomobject]@{Resolver=$server;Attempt=$i;Milliseconds=$watch.ElapsedMilliseconds;Addresses=$null;Error=$_.Exception.Message}}} }); $results | Format-Table -AutoSize | Out-String -Width 220", token, 120);
    private static async Task<string> Speed(CancellationToken token) {
        var lines = new List<string> { $"Cloudflare bounded transfer test {DateTimeOffset.Now:O}", "Single HTTP request per direction. Includes request/connection overhead; not a sustained multi-connection speed benchmark." };
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }; client.DefaultRequestHeaders.UserAgent.ParseAdd("HankiTools/" + AppInfo.Version);
        using (var down = CancellationTokenSource.CreateLinkedTokenSource(token)) {
            down.CancelAfter(TimeSpan.FromSeconds(25)); var watch = Stopwatch.StartNew(); long bytes = 0;
            try {
                using var response = await client.GetAsync("https://speed.cloudflare.com/__down?bytes=26214400", HttpCompletionOption.ResponseHeadersRead, down.Token); response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(down.Token); var buffer = new byte[65536]; int n;
                while (bytes < 26214400 && (n = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, 26214400 - bytes)), down.Token)) > 0) bytes += n;
                watch.Stop(); lines.Add($"Download: {bytes:N0} bytes in {watch.Elapsed.TotalSeconds:0.00}s = {bytes * 8 / Math.Max(.001, watch.Elapsed.TotalSeconds) / 1e6:0.00} Mbps");
            } catch (OperationCanceledException) when (!token.IsCancellationRequested) { lines.Add($"Download timed out after {bytes:N0} received bytes; no complete measurement."); }
            catch (HttpRequestException ex) { lines.Add("Download unavailable: " + ex.Message); }
        }
        token.ThrowIfCancellationRequested();
        using (var up = CancellationTokenSource.CreateLinkedTokenSource(token)) {
            up.CancelAfter(TimeSpan.FromSeconds(25)); var data = new byte[10485760]; System.Security.Cryptography.RandomNumberGenerator.Fill(data); using var content = new ByteArrayContent(data); var watch = Stopwatch.StartNew();
            try { using var request = new HttpRequestMessage(HttpMethod.Post, "https://speed.cloudflare.com/__up") { Content = content }; using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, up.Token); response.EnsureSuccessStatusCode(); watch.Stop(); lines.Add($"Upload request/response: {data.Length:N0} payload bytes in {watch.Elapsed.TotalSeconds:0.00}s = {data.Length * 8 / Math.Max(.001, watch.Elapsed.TotalSeconds) / 1e6:0.00} Mbps"); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { lines.Add("Upload timed out; transferred bytes unknown, no complete measurement."); }
            catch (HttpRequestException ex) { lines.Add("Upload unavailable: " + ex.Message); }
        }
        return string.Join("\r\n", lines);
    }
}
