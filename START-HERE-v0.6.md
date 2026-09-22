# Hanki Tools v0.6

## Build and open

On Windows with the .NET 8 SDK installed, extract the source and run `BUILD-WINDOWS.cmd`. Open `HankiTools.exe` from the timestamped directory printed by the build. Build outputs do not overwrite previous timestamped builds.

## New tools

**Diagnose:** collect the newest 50 critical/error/warning events in each of the System and Application logs over seven days. Messages are capped at 500 characters. These are selected events, not a full log export or crash-dump analysis. Event 41 records an unclean shutdown; it does not establish the cause. Access failures and no matching events are reported.

**Shield / Defender audit:** inspect selected Defender status and preferences, including exclusions. No configuration is changed. Organisation policy, another antivirus and insufficient permissions affect results; unavailable or empty fields are not proof of protection or absence of exclusions.

**Connect / Wi-Fi & latency:** collect native `netsh wlan show interfaces` output, then send ten ICMP probes per target to 1.1.1.1 and up to four distinct active IPv4 gateways. Targets run concurrently, with 1.5-second probe timeouts and 250 ms gaps. This generates network traffic. Reports show reply counts, no-reply rate and RTT statistics. ICMP filtering, route selection and the small sample limit interpretation; no-reply rate is not necessarily application packet loss. Wi-Fi details may require Windows location permission. No speed test, traceroute or permission changes are performed.

**Performance / Sampling:** collect 30 samples about one second apart. Show CPU average/peak, commit percentage average/peak and minimum available RAM. The first completed session becomes the in-memory baseline; subsequent sessions compare with it. Reset clears the baseline for the next successful run. Cancelled/failed sessions are discarded. Repeat the same representative workload: differences do not prove a setting helped. Disk/GPU counters are absent; on systems with more than 64 logical processors, CPU results may represent only the calling processor group.

## Reports and Assistant

Each new panel supports editable review before saving a local text report, and preparation in the existing Assistant tab. Reports may contain user/computer names, network identifiers, paths, exclusions and application data. Review/redact before sharing. The Assistant's limited automatic masking is not a guarantee that sensitive content is removed. Preparing a report does not upload it. Integrated AI chat and automatic fixes are not included.

Collection runs as the current user without automatic elevation. Fixed internal PowerShell commands use no user-provided command text; commands have a 45-second timeout. Cancellation stops collection. Close during active work cancels it; close again after it finishes.

## Remaining work

Windows acceptance testing is outstanding. Repairs, pagefile modification, automatic tuning, quarantine and restore remain disabled. Retained functionality includes file inventory/reviewed recycling, installed-app inventory with unknown usage clearly labelled, and the earlier diagnostics and pagefile explainer. Native recycling and UAC shortcuts also require Windows validation before release.
