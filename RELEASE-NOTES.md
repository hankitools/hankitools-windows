# Hanki Tools 0.17.0-rc.5 — roadmap candidate

RC5 adds new read-only checks and remote help:

- Diagnose → Windows Update: when the last Windows update installed (Defender's daily definition updates are ignored), failed updates with plain-language error codes, a pending restart, paused updates and a disabled update service. Driver and app update hiccups are shown separately as information.
- Performance → Battery & startup: how much of its original capacity a laptop battery still holds, how long since Windows last fully restarted (Fast Startup means Shut down doesn't restart Windows), the last startup, and startup time where Windows records it.
- Both checks are also part of the Full System Scan.
- Help & community → Get help from someone you trust: opens Windows' own Quick Assist, with a scam warning first. Hanki doesn't join or record the session.
- New guided checks: "Windows Update fails or is stuck" and "My laptop battery runs out quickly"; "My PC is slow" suggests a real restart.
- The Full System Scan's network probe now looks up several names, so a router that blocks one name is no longer reported as a DNS failure.
- The sidebar carries the Hanki brand line: + A little sisu for your PC.

RC4 fixes issues found in native pre-release testing of RC3 (see VALIDATION-v0.17.md):

- Dump analysis inspects real Windows Error Reporting minidumps, which contain reserved "unused" stream entries that were rejected as duplicates.
- Full System Scan, history comparison, file-scan history, Recovery and technician reports show local time instead of UTC.
- Startup folders no longer list desktop.ini or other hidden system files, which are folder settings rather than startup programs.
- A setting change Windows refuses before anything changes (for example, DNS without administrator rights) is recorded as "Not applied (unchanged)" instead of staying pending in Recovery.
- Cancelling a read-only tool says nothing was changed; screen readers get text for the last-scan card and scan counts.

RC3 explains results in plain language. Diagnose, Performance, Maintain, Connect and Shield now lead with a headline and color-coded cards (Looks OK / Worth reviewing / Needs attention / Not available) with next steps; the full technical report remains under View technical details. Missing or failed evidence is never shown as healthy.

- Diagnose: Guided checks start from a symptom and open the right tool for each step. Event logs explain common Windows events, separate harmless noise, and name crashed apps and failed services. The crash timeline opens with a short summary.
- Connect: the basic check says whether the adapter, router, DNS or internet is the problem. It tests several names, so a router that filters one domain is noted rather than reported as a DNS failure. Wi-Fi / latency compares your router with the internet and reads signal strength.
- Performance: clear verdicts for memory, drive space, the 30-second sample and monitoring, with plain comparisons against your baseline.
- Maintain: space-by-type chips after a file scan, advice for each startup entry, reclaimable space for duplicates, and app size totals.
- Shield: summaries for Defender protection, definitions, recent scans and detections. Microsoft Defender comes first; the experimental file scanner has its own page.

RC2 fixes Full System Scan modules being reported as Failed on Windows PowerShell 5.1. Harmless progress records ("Preparing modules for first use.") reached collector stderr and were treated as tool errors. Progress output is now suppressed so stderr carries only genuine errors, and a native check guards against regressions.

RC2 also refreshes the interface: Home leads with Full System Scan and a last-scan summary; scan results show colored evidence states; the sidebar is grouped with distinct icons; sub-views use underline tabs; reports sit in rounded cards with inline search; and the title bar, scrollbars, list headers and inputs follow the dark theme. It fixes "&" disappearing from labels, missing startup selection highlights and fully-selected report text. High Contrast keeps system rendering.

- Added free Full System Scan with structured results, visible unknown/partial states, cancellation and deterministic guidance.
- Added Windows Activation troubleshooting with safe licensing-state/error explanations and optional organization KMS checks. No full product keys or activation changes.
- Added minimized local diagnostic history and comparisons alongside existing scanner history and recovery tools.
- Added development foundations for explicitly reviewed repairs, verification/audits, additive Pro/Technician capabilities, scheduling, secure sessions and minimized provider consent.
- Release builds remain Community; no production licensing, identity, cloud report or new AI explanation provider is connected. Existing manual tools remain available without an account.
- Native Windows acceptance and exact signed-executable release checks remain required. This is not a public-release approval. Cloud report sharing still requires an approved backend and server-side validation.

## App polish and community support

- Added Help & community (F1) with hanki.tools, Discord, user guide, privacy information and GitHub links.
- Added an editable bug-report draft and copyable app details without account names, file paths or automatic log collection.
- Added Task Manager, Event Viewer and Windows Settings to quick access and Ctrl+K search.
- Search now understands common terms such as Wi-Fi, BSOD, RAM, cleanup, undo and support.
- Updates remain manual; opening a support link does not upload a report.

# Hanki Tools 0.16.0-rc.3

RC3 replaces Compress-Archive with verified staging and .NET ZIP creation. Read-sharing supports loaded/read-open executables; sharing violations receive bounded retries. Partial ZIPs are removed on failure, existing ZIPs are never overwritten, and both candidate/final packaging use the same helper. REPACKAGE-WINDOWS.ps1 can package a successful RC2 candidate without rebuilding after verifying its recorded hashes and smoke result.

RC3 also brings the app closer to the supplied website candidate: cool charcoal surfaces, blue accents, a compact Hanki Tools header, sidebar line icons and denser bordered tool cards. Actual module names, release-candidate version and feature limitations remain authoritative; website placeholder version/support claims are not adopted.

RC2 fixes two backup-failure tests to accept both Windows access-denied and Linux I/O exceptions. The tests still require the backup to fail and confirm that the backend setting was not changed. Production backup/undo code and the build gate are unchanged.

Release-readiness candidate following the 0.15 UI update. This is not an assertion that native Windows acceptance has passed.

- Preserves damaged scan history and reports the problem instead of silently resetting it. History writes remain bounded and use an atomic replacement.
- Missing scan targets now fail clearly. Files skipped during inspection no longer count as successfully scanned. Scan progress updates are throttled to keep the UI responsive.
- Separates Windows-command timeouts from user cancellation and keeps recovery advice visible for uncertain changes.
- Validates loaded monitoring sessions: required fields, sample limits, dates, finite/ranged values and peak/mean consistency.
- Adds a global running-task indicator and cancellation across active modules. Defender monitoring can be cancelled when closing; independent Defender scans retain their separate controls.
- Adds elapsed time to tool cancellation, About & privacy, runtime/build identity, DPI-aware button sizing and theme refresh on system-color changes.
- Uses assembly version metadata for app branding and network user agent, reducing stale release labels.
- Adds opt-in structural Windows UI smoke checks, current-runtime candidate builds, optional timestamped signing, checksums and a final release-package gate tied to the tested executable hash.
- Tightens the installed debugger signature check to match the Microsoft organization field exactly.
- Removes excluded prototype UI/quarantine code from the source tree. No existing user recovery data is deleted.

The charcoal workspaces, concise module intros, modern navigation, result cards and contextual file actions from previous versions remain.
