# Hanki Tools • Data and network behavior

This describes the shipped application's behavior, not a guarantee about third-party services or operating-system records. No app analytics, crash-upload service, background updater or automatic diagnostic-report upload is implemented.

## Local data

The app uses `%LOCALAPPDATA%\IgezziGuard` for up to 100 scan summaries, startup/recovery journals, disabled-startup file backups, optional app-observation mappings/coverage, error logs and an optional debugger symbol cache. Paths, application names, command strings, network configuration and dates can be sensitive. Recovery/observation/error records are not automatically removed when the portable app folder is deleted. Keep recovery backups until changes are restored.

The Windows Update and Battery & startup checks read Windows' own local records (update history, startup events, battery report); on laptops Windows writes its battery report to a temporary file, which Hanki deletes straight after reading. Neither check contacts the network.

Hanki Pro and Technician (paid, optional): a licence key is kept in Windows Credential Manager for your Windows account, with the edition, the activation id and when it was last checked; no name or email from the purchase is kept. A scheduled check (Pro) is a Windows Task Scheduler task for your account that runs the same read-only scan without network probes or repairs and adds it to local history. Technician customer reports are saved where you choose; a minimized record of each report (labels, scan titles and results, repair outcomes) and your business name and contact are kept under `%LOCALAPPDATA%\IgezziGuard\technician`.

Hanki Performance reads graphics, display, processor, memory and storage information from Windows and, on NVIDIA PCs, the NVIDIA driver's own settings. Nothing about it leaves the PC. It keeps, under `%LOCALAPPDATA%\IgezziGuard`: your game list (`games.json`: names and program paths found in Steam, Epic, GOG and other launchers' local records, or added by you), the latest Performance check (`performance-checks.json`) and Performance sessions (`performance-sessions.json`: measurements and the settings changed). Measurements in Performance Lab include the names of the busiest programs; saving one to a file is your choice. Frame-rate capture reads Windows' DirectX timing events for the chosen game only, on this PC, while you measure. On AMD Radeon PCs it reads Radeon settings through AMD Software's own interface (ADLX). The gaming check and Tune my PC take a one-second sample of running programs (names and CPU and memory use) to point out overlays, recorders and busy programs, and read RivaTuner's global frame limit from its profile file. The programs it points out are saved with the latest Performance check; the rest of the sample isn't kept. Your own NVIDIA presets are kept in `nvidia-presets.json`. Launch and measure saves its measurements as Performance sessions. Settings Hanki Performance changes (display mode, per-app GPU choice, processor maximum, NVIDIA per-game and global settings, AMD Radeon settings, Windows gaming settings and mouse acceleration) are recorded in Recovery before they change.

File inventories, current reports and checklists are normally session data. Explicit exports and saved monitoring runs are written where you choose; monitoring runs include the machine name. Clipboard content copied through Assistant can be available to other local applications or Windows clipboard history/sync. Redaction helpers only mask common patterns and are not comprehensive.

API key, AI draft and chat are held in process memory until cleared or exit; they are not intentionally persisted by Hanki. This does not rule out operating-system paging, crash dumps, screen capture or other software observing memory. Error logs are local and may contain paths and exception details.

## Connections you initiate

| Feature | Information disclosed |
| --- | --- |
| Basic network checks | DNS requests for www.microsoft.com, cloudflare.com and example.com, a TCP probe to 1.1.1.1:443 and one to the first of those names that resolves; destinations/resolvers can observe source IP and requests. |
| Full System Scan network probes | Only when you tick Include network probes: ICMP to your default gateway, DNS requests for www.microsoft.com, cloudflare.com and example.com, and a TCP connection to port 443 of the first name that resolves. The DNS-cache repair (Hanki Pro) repeats them to verify. Destinations/resolvers can observe source IP and requests. |
| Wi-Fi/ICMP/traceroute | ICMP probes to disclosed gateways, public target or selected host; local reports can contain SSID, BSSID, MAC and IP addresses. |
| DNS comparisons / DNS repair | Queries go to configured resolvers, Cloudflare 1.1.1.1 and Google 8.8.8.8. Changing system DNS affects future queries outside Hanki too. |
| Transfer test | Requests to speed.cloudflare.com; up to 25 MiB download and 10 MiB upload payload, plus overhead. Metered charges may apply. |
| Defender controls | Defender communicates under its own Windows configuration, sample-submission and protection policies. Hanki does not control those provider records. |
| Debugger symbols | Only when enabled: module/symbol identifiers and source IP go to Microsoft's symbol service. Hanki does not upload the dump. |
| OpenAI API chat | The exact reviewed request, including prior chat turns, goes to api.openai.com/v1/responses. The API key travels separately as an authorization header. store=false is requested; it is not a zero-retention guarantee. |
| Quick Assist | Hanki only opens Microsoft's Quick Assist app (or its Microsoft Store page if it isn't installed). The remote session runs through Microsoft's service under Microsoft's terms; the person you connect with can see your screen, and control it if you allow. Hanki doesn't join, record or relay the session. |
| Licence check (Pro and Technician only) | When you activate, check or remove a key, the key, Hanki's organization id at Polar and an activation id go to Polar's licence API (api.polar.sh), with a label such as "Windows PC, activated 2026-09-23"; Polar sees your IP address. A Pro key is checked once. A Technician licence is checked about once a week when Hanki starts, and works offline for up to 30 days. No scan results, files, PC names or user names are sent. Nothing is sent without a licence. |
| Open ChatGPT | Opens chatgpt.com in your browser without putting the report in the URL. Pasting/uploading there is a separate user action. |

The source build scripts additionally contact Microsoft's runtime metadata and .NET package sources. Signing uses the configured timestamp service. Those are publisher/build operations, not background behavior of the installed app.

Support and privacy contact: hello@hanki.tools. The hanki.tools website, GitHub issues and Discord have their own logging and analytics, separate from the app.
