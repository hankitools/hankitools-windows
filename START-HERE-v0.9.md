# Hanki Tools v0.9

Close Hanki, extract this source package and run BUILD-WINDOWS.cmd with the .NET 8 SDK on Windows. Launch HankiTools.exe from the new timestamped dist folder. The title/footer show v0.9. Existing data is retained; new journals use separate files. Do not discard backups while their actions remain applied.

## What's available

| Area | New workflows |
| --- | --- |
| Maintain | Duplicate contents, additional age/type/size review filters, machine-wide Run entries in both registry views, current-user/all-user Startup folder files |
| Performance | 30-second, 1-minute, 5-minute and 15-minute monitoring; CPU/memory, physical disk throughput and GPU engine counters; save/load sessions and comparisons; power-plan tuning with undo |
| Diagnose | Local minidump structure inspection, Microsoft CDB/KD debugger integration, symptom-based investigation checklists |
| Connect | Bounded traceroute, DNS resolver comparisons, bounded download/upload tests, automatic/public IPv4 DNS changes with undo |
| Shield | Defender quick/full scan requests, cancellation requests, definition updates, opt-in protection polling and recent threat details |
| Recovery | A separate recovery page for power plan, IPv4 DNS and startup-folder moves; saved before/after states, interrupted-action handling and conflict checks |

## Maintain

Duplicates: select a folder. Files are grouped by size and SHA-256; no removal is preselected. Select a group and one copy to recycle. Before recycling, Hanki rechecks metadata/hashes and byte-compares against a retained copy, keeping that copy open during recycling. Only the original personal-folder recycling policy applies: system/application paths, reparse points and other protected paths remain blocked. Restore through Windows Recycle Bin. Hanki does not claim duplicate cleanup is part of its settings undo. Hard links can appear as duplicates; logical duplicate bytes are not a guaranteed disk-space saving. No permanent-delete fallback is used.

Duplicate scan limits: 100,000 inventory entries, 2 GiB per hashed file, 20 GiB total hash-read budget; empty files excluded. Errors, skips and limits appear in the report. Changed files require rescanning.

Files & storage adds review filters for files not modified for 90 days, installer/archive extensions and files at least 250 MiB. These are not proofs a file is unused or safe to remove. Filters select no files automatically. The existing preview/recycle workflow remains required; age filters do not use last-access time.

Startup / undo now has current-user, all-users 64-bit and all-users 32-bit registry scopes. Select scope, refresh and review a value before disabling. Each scope has its own journal and restore list. Machine-wide changes generally require administrator rights. Hanki does not change Windows Startup approval flags, so presence in Run is not an enabled-state verdict.

Startup folders lists top-level files from both Windows Startup folders. Disabling moves a file (up to 4 MiB) into Hanki's local disabled-startup directory, checks its hash and journals the move. Undo is on Recovery. Existing/conflicting destinations are never overwritten. No shortcut is executed. Scheduled tasks/services are not managed in this release.

## Performance

Long monitoring / saved runs samples CPU and commit about once per second. Disk/GPU collectors run separately about every five seconds, with WMI query overhead. Physical-disk rates are _Total read/write bytes per second; GPU values aggregate per-process contributions per named engine and report the busiest engine across adapters. This is not total utilization summed across engines/GPUs or an FPS/temperature measure. Missing driver/WMI counters remain unknown. CPU on systems with over 64 logical processors may cover only the calling processor group. Collection itself adds overhead; device and CPU windows may differ.

Use last run as baseline, save the completed run as JSON, or load a saved baseline. Reports show CPU/commit changes and device summaries for both runs. Compare the same machine, workload and duration; differences are not causal proof. Incomplete/cancelled runs are not saved as new results. Saved reports contain machine name, timestamps and metrics.

Power tuning offers installed Balanced and High performance plans. The current plan is saved before applying a new one. Unsupported/missing plans report an error rather than being created. Heat, fan noise and battery use can increase. Undo through Recovery before applying another change to the same setting. Pagefile sizing remains under Windows settings; Hanki provides advice and a shortcut, not automatic resizing.

## Diagnose

Choose a local dump. Built-in MDMP inspection extracts bounded header/exception/module evidence and rejects malformed offsets/counts. It does not symbolize stacks or determine the culprit. PAGE kernel dumps are recognized but require the debugger path for substantive analysis.

For symbolized analysis, install Microsoft's Debugging Tools for Windows separately, then select cdb.exe for user-mode dumps or kd.exe for kernel dumps. Hanki verifies a valid Microsoft executable signature and runs fixed !analyze -v; k; q commands against the selected file. No dump-provided commands are executed. Output is bounded and the process times out after 180 seconds. CDB/KD and extension behavior remain Microsoft's responsibility; this is not a debugger sandbox.

Microsoft symbol downloads are off by default. Enabling them sends symbol/module requests and your IP to Microsoft's symbol server; Hanki does not upload the dump. Offline analysis may lack symbols. Clear inherited debugger paths and fixed command arguments reduce accidental configuration leakage. The dump and debugger output may contain sensitive information. A module, bucket or 'probably caused by' line is a hypothesis requiring corroboration.

Guided checks provides local checklists for restarts, freezes, memory pressure, network problems and security concerns. They reference the relevant Hanki tools and Windows evidence. Ticks are session-only and do not execute fixes or certify a diagnosis.

## Connect

Trace route resolves a plain hostname to IPv4 and sends one ICMP probe per hop, at most 20 hops with 1-second probe timeouts. Missing hop replies are not proof of a broken path. DNS comparison sends three A-record queries each to configured DNS, 1.1.1.1 and 8.8.8.8; resolver timings can include cache effects and answers can legitimately differ.

The speed test sends one request per direction to speed.cloudflare.com: up to 25 MiB download and 10 MiB upload, plus protocol overhead, with a 25-second timeout per direction. This is an approximate bounded transfer measurement, not a multi-connection line-capacity benchmark. Metered charges, VPN/proxy routing, server load and TCP setup can affect results. Review the network/data disclosure before running.

DNS repairs target a selected adapter by stable GUID, capture its IPv4 static/automatic configuration and offer either automatic addresses or 1.1.1.1/1.0.0.1. The IPv4 CIM object is explicitly targeted; IPv6 is left unchanged. Public DNS receives future queries and may break private/corporate names. VPN/NRPT/policy can override behavior. Settings changes may require running Hanki as administrator; no automatic full-app elevation occurs. No Winsock reset, firewall change, adapter reset or forced reboot is performed.

## Shield

Defender quick/full scans, scan cancellation and definition updates launch the standard Microsoft tool with Windows UAC approval. Scan launch is not confirmation the service accepted/completed it; check Windows Security and refresh start/end timestamps. Defender may remediate or quarantine according to policy. Hanki does not undo Defender actions. Scans can continue after Hanki closes; general Hanki Cancel does not stop them. The explicit cancellation command may affect a scan started outside Hanki.

Protection alerts are opt-in and check selected Defender status flags every minute while Hanki is open. Unknown/unavailable fields stay unknown. Review alerts appear in Shield and the shared status footer; they are not background-service monitoring or Windows toast notifications. Another antivirus/passive mode or policy may explain disabled flags. Recent findings combine up to 30 detection records with threat-name mapping and action-status/resource fields. Empty findings do not establish a malware-free PC.

## Recovery and local data

Recovery.json (lowercase recovery.json on disk) stores exact before/after values for power plans, IPv4 DNS and startup-folder state. It writes/flushed backups before mutations, rechecks previews and verifies results. Interrupted actions remain Pending / inspect. Undo restores only when current state matches either saved state; unknown external changes block restoration. This is not an OS-wide transaction and does not prevent changes by another application between checks.

Files under %LOCALAPPDATA%\IgezziGuard:
- recovery.json — general setting journal.
- disabled-startup — startup-folder files needed for restoration.
- startup-actions.json — original current-user registry journal.
- startup-machine-1.json / startup-machine-2.json — machine registry journals.
- symbols — optional debugger symbol cache.

Keep backups until restored. Journals contain paths/commands and are not encrypted. Per-user Windows permissions apply. If administrator credentials belong to another account, that account has a different data folder; use the same Windows user for a change and its recovery.

## Verification status

Release build: zero errors/warnings. All 90 non-destructive automated checks pass, including duplicate content/changes, malformed dump offsets, interrupted setting changes and conflict-aware undo. Tests use temporary files and fake setting/HTTP backends; no Windows machine settings, external speed tests or Defender scans were run in the Linux build environment.

Windows UI, WMI counters, registry/CIM mutations, UAC behavior, debugger output and external network services still require native acceptance tests. See VALIDATION-v0.9.md. This remains an unsigned source preview, not a production-certified maintenance suite.
