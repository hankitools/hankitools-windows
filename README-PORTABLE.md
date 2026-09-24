# Hanki Tools • Portable guide

**+ A little sisu for your PC.**

Extract the entire ZIP to a local folder and open HankiTools.exe. Keep the Data folder next to the executable. This is a Windows x64 release that is not code-signed yet, so Windows SmartScreen warns before the first run; Windows 11 x64 is the acceptance-test target. ARM64, Windows Server and older Windows releases are not certified by this release.

Start on Home or press Ctrl+K to find a tool. Each module explains its purpose. Blue buttons start the main workflow; quieter links offer supporting actions. Defender and memory snapshots show summary cards first; technical details retain the full report.

Run normally as a standard user. Some Windows settings and Defender commands require administrator approval. Hanki does not automatically elevate the entire app. Read action previews before applying changes.

“Cancel tasks” cancels active Hanki collection/tasks and stops local monitoring. It does not roll back completed changes or stop a scan running independently in Defender. Use Shield's Defender cancel action or Windows Security for that. Use Recovery for supported setting changes, Maintain's startup undo for registry entries, and Windows Recycle Bin for recycled files. Files deleted permanently can't be restored. A removed leftover app entry can be put back by double-clicking its .reg backup (in the app-entry-backups folder next to Hanki's saved data).

## Your data

Local history, app-observation records, error logs, recovery journals, startup-file backups and optional symbol cache live in `%LOCALAPPDATA%\IgezziGuard`. Saved monitoring sessions and reviewed exports live where you choose. API keys and AI chat are kept in memory for the session and are not intentionally written to an application file.

To upgrade, close Hanki and extract the new package to a new folder. Local history and recovery data remain available. Keep the previous package until the new one has been tested. Do not remove the local data directory while you still need to restore a disabled startup file or undo settings.

To remove the portable app, close it and delete its extracted application folder. Removing the application does not undo settings or delete local data. Restore desired changes first. If you later remove the local data directory, you also remove its history and recovery backups; this is not necessary for an ordinary upgrade.

## Limitations

- Defender can remediate threats under Windows policy. Hanki does not undo that remediation.
- The separate file scanner is experimental: a test signature and simple heuristics, no maintained malware feed, archive unpacking or real-time protection. Files over 512 MiB and reparse points are excluded.
- Duplicate sizes are logical file sizes, not guaranteed disk-space savings. Review every cleanup; recycling does not immediately free space.
- App observation can be incomplete and does not prove an app is unused.
- Pagefile advice does not establish a custom size or validate crash-dump readiness. Pagefile settings are changed through Windows.
- A crash marker or debugger's guess does not establish root cause. Driver/WMI/permissions can make counters unavailable.
- Optional AI requires your own OpenAI API access and billing. Review the entire request before sending. Cancellation cannot guarantee provider processing or charges stop.

If reporting a problem, include Hanki version, Windows version, module, exact steps and redacted error text. Do not include API keys or private logs/dumps. About & privacy shows the version and local data location.
