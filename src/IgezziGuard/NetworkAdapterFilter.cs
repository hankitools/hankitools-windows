using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace IgezziGuard;

internal static partial class NetworkAdapterFilter
{
    /// <summary>Windows lists every filter driver (WFP, QoS, Native WiFi) bound to an adapter as its own "adapter"; they end in -0000, -0001… and are not something to change DNS on.</summary>
    internal static bool IsFilterBinding(string? description) => description is not null && FilterSuffix().IsMatch(description);

    /// <summary>True for entries a person could sensibly change DNS on: not filter bindings, WAN miniports, tunnels, PPP or the kernel-debug adapter.</summary>
    internal static bool IsUserAdapter(string? description, NetworkInterfaceType type) =>
        type is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp)
        && !IsFilterBinding(description)
        && description is not null && !description.StartsWith("WAN Miniport", StringComparison.OrdinalIgnoreCase) && !description.StartsWith("Microsoft Kernel Debug", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"-\d{4}$")]
    private static partial Regex FilterSuffix();
}
