# Hanki Tools • Data and network behavior

This describes the shipped application's behavior, not a guarantee about third-party services or operating-system records. No app analytics, crash-upload service, background updater or automatic diagnostic-report upload is implemented.

## Local data

The app uses `%LOCALAPPDATA%\IgezziGuard` for up to 100 scan summaries, startup/recovery journals, disabled-startup file backups, optional app-observation mappings/coverage, error logs and an optional debugger symbol cache. Paths, application names, command strings, network configuration and dates can be sensitive. Recovery/observation/error records are not automatically removed when the portable app folder is deleted. Keep recovery backups until changes are restored.

The Windows Update and Battery & startup checks read Windows' own local records (update history, startup events, battery report); on laptops Windows writes its battery report to a temporary file, which Hanki deletes straight after reading. Neither check contacts the network.

File inventories, current reports and checklists are normally session data. Explicit exports and saved monitoring runs are written where you choose; monitoring runs include the machine name. Clipboard content copied through Assistant can be available to other local applications or Windows clipboard history/sync. Redaction helpers only mask common patterns and are not comprehensive.

API key, AI draft and chat are held in process memory until cleared or exit; they are not intentionally persisted by Hanki. This does not rule out operating-system paging, crash dumps, screen capture or other software observing memory. Error logs are local and may contain paths and exception details.

## Connections you initiate

| Feature | Information disclosed |
| --- | --- |
| Basic network checks | DNS requests for www.microsoft.com, cloudflare.com and example.com, a TCP probe to 1.1.1.1:443 and one to the first of those names that resolves; destinations/resolvers can observe source IP and requests. |
| Full System Scan network probes | Only when you tick Include network probes: ICMP to your default gateway, DNS requests for www.microsoft.com, cloudflare.com and example.com, and a TCP connection to port 443 of the first name that resolves. The DNS-cache repair (not available in this release) repeats them to verify. Destinations/resolvers can observe source IP and requests. |
| Wi-Fi/ICMP/traceroute | ICMP probes to disclosed gateways, public target or selected host; local reports can contain SSID, BSSID, MAC and IP addresses. |
| DNS comparisons / DNS repair | Queries go to configured resolvers, Cloudflare 1.1.1.1 and Google 8.8.8.8. Changing system DNS affects future queries outside Hanki too. |
| Transfer test | Requests to speed.cloudflare.com; up to 25 MiB download and 10 MiB upload payload, plus overhead. Metered charges may apply. |
| Defender controls | Defender communicates under its own Windows configuration, sample-submission and protection policies. Hanki does not control those provider records. |
| Debugger symbols | Only when enabled: module/symbol identifiers and source IP go to Microsoft's symbol service. Hanki does not upload the dump. |
| OpenAI API chat | The exact reviewed request, including prior chat turns, goes to api.openai.com/v1/responses. The API key travels separately as an authorization header. store=false is requested; it is not a zero-retention guarantee. |
| Open ChatGPT | Opens chatgpt.com in your browser without putting the report in the URL. Pasting/uploading there is a separate user action. |

The source build scripts additionally contact Microsoft's runtime metadata and .NET package sources. Signing uses the configured timestamp service. Those are publisher/build operations, not background behavior of the installed app.

Support and privacy contact: hello@hanki.tools. The hanki.tools website, GitHub issues and Discord have their own logging and analytics, separate from the app.
