namespace IgezziGuard;

internal sealed record InternetNextStep(string Headline, string Instruction, CardStatus Status, string FollowUpRoute);

/// <summary>One next step from evidence. Never treats a configured gateway as a responding router.</summary>
internal static class InternetJourney
{
    internal static InternetNextStep Evaluate(ConnectionFacts f)
    {
        const string basic = "Connect  /  Wi-Fi / latency", advanced = "Connect  /  Advanced / DNS repair";
        if (f.ActiveAdapters == 0)
            return new("No network connection was reported", "Connect to Wi-Fi or plug in the network cable. Then choose Check again.", CardStatus.Review, basic);
        // A VPN or IPv6-only network can work without a physical IPv4 gateway.
        bool reached = f.IpReachable == true || f.NameReachable == true;
        if (!reached && (!f.HasGateway || !f.HasIPv4) && !f.VpnActive)
            return new("The local connection needs a closer look", "Reconnect to your network, then choose Check again. These checks focus on IPv4; an IPv6-only network may need a different investigation.", CardStatus.Review, basic);
        if (f.DnsWorks == false && reached)
            return new("A server answered, but website name lookups failed", "Try opening the website again. If it still fails, use More help to compare DNS. On a work or VPN connection, ask your administrator before changing DNS.", CardStatus.Review, advanced);
        if (f.DnsWorks is null || f.IpReachable is null || (f.DnsWorks == true && f.NameReachable is null))
            return new("The connection check is incomplete", "Choose Check again. Missing evidence does not tell us whether the connection works.", CardStatus.Unknown, basic);
        if (!reached)
            return new("The test servers did not answer", "Try the same website on another device using this network. If both fail, check your router or provider. A firewall or VPN may also block these tests.", CardStatus.Review, basic);
        if (f.DnsWorks == true && f.NameReachable == true)
            return new("Basic connection checks passed", "Try the app or website that was failing. If it works, choose It works now. If it is still slow or disconnecting, choose More help.", CardStatus.Info, basic);
        return new("Some connection tests did not pass", "Try the affected website in your browser. A successful server test does not prove every app works. Use More help if the problem continues.", CardStatus.Review, advanced);
    }
}
