using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IgezziGuard;

public static class NetworkDiagnostics
{
    public static async Task Run(IProgress<string> output, CancellationToken token)
    {
        output.Report($"Hanki Connect • {DateTimeOffset.Now:g}\nRead-only checks; failed probes alone do not establish the cause.\n");
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            token.ThrowIfCancellationRequested();
            output.Report($"ADAPTER: {adapter.Name} — {adapter.Description}\nState: {adapter.OperationalStatus}; type: {adapter.NetworkInterfaceType}");
            try {
                var ip = adapter.GetIPProperties();
                output.Report("Addresses: " + string.Join(", ", ip.UnicastAddresses.Select(a => a.Address.ToString())));
                output.Report("Gateways: " + string.Join(", ", ip.GatewayAddresses.Select(a => a.Address.ToString())));
                output.Report("DNS: " + string.Join(", ", ip.DnsAddresses.Select(a => a.ToString())));
            }
            catch (NetworkInformationException ex) { output.Report("Adapter details unavailable: " + ex.Message); }
        }
        token.ThrowIfCancellationRequested();
        try {
            var addresses = await Dns.GetHostAddressesAsync("example.com", token).WaitAsync(TimeSpan.FromSeconds(5), token);
            output.Report("DNS example.com: " + string.Join(", ", addresses.Select(a => a.ToString())));
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException) { output.Report("DNS failed/inconclusive: " + ex.Message); }
        foreach (var host in new[] { "1.1.1.1", "example.com" })
        {
            token.ThrowIfCancellationRequested();
            using var tcp = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try {
                await tcp.ConnectAsync(host, 443, timeout.Token);
                output.Report($"TCP {host}:443 reachable. Not an HTTPS or general internet health test.");
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { output.Report($"TCP {host}:443 timed out. Firewall, routing or endpoint issues are possible."); }
            catch (SocketException ex) { output.Report($"TCP {host}:443 failed: {ex.SocketErrorCode}"); }
        }
        token.ThrowIfCancellationRequested();
        output.Report("\nFinished. VPNs, proxies and multiple adapters can affect results. No settings changed.");
    }
}
