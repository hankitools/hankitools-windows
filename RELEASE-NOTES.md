# Next version (not released yet)

- **Home search.** "What do you need help with?" finds guided fixes and tools as you type, with everyday words (laggy, bsod, wifi, fps, undo). Enter opens the top result.
- **Your PC at a glance and recent activity on Home.** Windows version, time since restart, system drive space, memory in use, graphics card and driver, and protection as Windows Security reports it; each tile opens its page. Below: your latest scan, Performance check and the changes Hanki made, each with a link to review or undo.
- **Delete files.** In Maintain → Files, select one or many files and choose Delete… (or press Delete, or right-click). Choose Move to Recycle Bin (the default, restorable) or Delete permanently (after confirming it can't be undone). Files on any local drive can be deleted, except Windows, program and system folders, Store app files, app data, other people's profiles and Hanki itself.
- **Uninstall apps.** In Maintain → Apps, select an app and choose Uninstall…: Hanki runs the app's own uninstaller, the same one Windows Settings uses, and checks afterwards that it's gone.
- **Leftover app entries.** Apps whose files are already gone but still show as installed are marked "Leftover entry". Remove leftover entry… saves the entry to a .reg backup, then removes it (Windows asks for administrator permission for entries that apply to all users).
- Deletions, uninstalls and removed entries appear in History → System actions.

# Hanki Tools 0.18.0-rc.4 — cleaner, more modern layout

An unsigned preview for testing. It changes how pages look and are laid out; the checks, settings and Recovery behave as in 0.18.0-rc.3.

- **Cleaner pages.** Views sit on one row of pill-shaped tabs, with "More views" for the rest. Actions stay on one or two lines, with "More" for the rest. The row of report links under every tool is gone: Review / share, Prepare for Assistant and the previous report are in a ⋯ menu, next to "Technical details".
- **Modern controls.** Drop-downs are rounded fields with a dark list, text boxes have a quiet frame, lists lose their white border, the report search is a rounded field, and a thin line moves while a task runs. Cards and tiles have softer edges.
- **Quieter window.** Quick access, About and the version sit at the bottom of the sidebar, and the status bar appears only while something runs.

# Hanki Tools 0.18.0-rc.3 — fewer Tune my PC steps

An unsigned preview for testing. The new power mode, G-SYNC and per-game changes haven't been tested on real hardware yet; every change is reviewed first and saved in Recovery, so it can be undone.

- **Fewer steps in Tune my PC.** Hanki now changes the Windows power mode itself (on the Balanced power plan), and offers full GPU clocks for each game in your list instead of asking you to optimize them one by one. On NVIDIA, it reads whether G-SYNC is on: then it answers the G-SYNC question for you and drops that step. NVIDIA doesn't let apps switch G-SYNC on, so when it's off, that step stays. In-game settings stay a step, since each game keeps its own.

# Hanki Tools 0.18.0-rc.2 — Tune my PC candidate

An unsigned preview for testing. It adds Tune my PC, the simpler layout, NVIDIA and AMD Radeon settings, more gaming checks and Launch and measure. Their settings changes haven't been tested on real NVIDIA or AMD hardware yet; every change is reviewed first and saved in Recovery, so it can be undone.

- **Tune my PC** is the first thing on the Performance page. Choose Gaming + Performance, Gaming + Quality, Creative work or Low power, and say whether your display has G-SYNC or FreeSync. Hanki then reads your display, Windows, mouse, graphics driver, processor, memory and storage settings and shows a plan: what it can change (before → after) and the steps only you can take (BIOS memory profile, in-game settings). Nothing changes until you review and apply; every change is saved in Recovery first. The recommendations and their sources are in docs/TUNING.md.
- **Simpler layout:** five areas in the sidebar (Home, Fix my PC, Tune my PC, History, Help) instead of about 20 items. Each area opens on its main action with its other tools as large tiles, and pages show a back link to their area. Text is larger throughout, and page introductions are one short sentence.
- **NVIDIA global settings:** Gaming → NVIDIA offers presets (Competitive, Maximum FPS, Visual quality, Quiet and cool, NVIDIA defaults), an editor for 16 settings, and your own saved presets. Each change is reviewed and can be undone.
- **AMD Radeon settings:** Gaming → AMD Radeon reads and changes Anti-Lag, Chill, Boost, Image Sharpening, Enhanced Sync, Wait for Vertical Refresh, Frame Rate Target Control and Anisotropic Filtering through AMD Software's own interface. Not yet tested on AMD hardware; please report anything that looks wrong.
- **More Windows gaming checks:** Game Bar background recording, optimizations for windowed games, variable refresh rate and mouse acceleration (Enhance pointer precision). The Competitive goal and Tune my PC offer to turn mouse acceleration off.
- **Overlays and background apps:** the gaming check lists overlays (Discord, Steam, NVIDIA, Xbox Game Bar and others), recorders (OBS, Medal and others), RivaTuner and its frame limit, and programs busy in the background. Hanki doesn't close anything.
- **Launch and measure:** Gaming → Games starts a game, waits for it to load, measures two minutes of play and compares it with the previous run of that game, together with the settings changed in between.
- When a setting was already changed by Hanki, a new change replaces it and one undo still returns to the original value.

# Hanki Tools 0.18.0-rc.1 — System and Performance candidate

Hanki is now two areas in one app. **Hanki System** finds what's wrong with Windows and fixes it safely. **Hanki Performance** measures what limits your PC or game and optimizes it, one reviewed and reversible change at a time. Nothing was removed; see docs/SYSTEM-AND-PERFORMANCE.md for where each tool moved.

- Home shows both areas with their status. Full System Scan is now Fix My PC; Battery & startup moved to Diagnose; file-scan history moved into Shield.
- Gaming: a read-only gaming check (refresh rate, which GPU games use, Game Mode, power settings, NVIDIA settings), six goals (Balanced, Maximum FPS, Competitive, Visual quality, Quiet, Laptop), a game list found from Steam, Epic, GOG and other launchers, and Optimize this game. Every change is reviewed first, recorded in Recovery and can be restored.
- NVIDIA: per-game driver settings (power mode, frame limit, low latency, texture filtering) through NVIDIA's own interface, checked against the driver's names. AMD Radeon settings aren't read yet.
- GPU, CPU, Memory and Storage pages: graphics hardware and displays, processor power limits, memory speed and commit headroom, pagefile health, drive types, games on hard disks, TRIM and DirectStorage readiness.
- Performance Lab: a new monitor that measures every second (per-thread CPU, GPU load, temperature and video memory, memory, disk, busiest programs) and records frame rates for DirectX games when Hanki runs as administrator; a Bottleneck Analyzer that shows its evidence and confidence; Stutter Diagnostics; baselines and fair before/after comparisons.
- History: System actions (scans, repairs, recorded changes) and Performance sessions (measurements, the settings tested, keep or restore) are kept apart.
- Fix My PC mentions significant gaming configuration problems and points to Gaming; it never changes them itself and never overclocks.
- Hanki explains common internet “FPS tweaks” it won't make, and why.

# Hanki Tools 0.17.0

Hanki Tools 0.17 is the first full release of the 0.17 series. It is not code-signed yet: signing through the SignPath Foundation program is pending, so Windows SmartScreen warns before the first run. Compare the published SHA-256 checksum before running it.

What's in 0.17:

- Full System Scan: one read-only pass across Windows, storage, devices, security and performance, saved to Diagnostic history so you can compare scans.
- Plain-language results: every tool leads with a headline and color-coded cards (Looks OK / Worth reviewing / Needs attention / Not available) and a next step; technical details stay one click away. Missing evidence is never shown as healthy.
- Guided checks for nine common problems, from a slow PC and crashes to Windows Update failures and a draining laptop battery, with steps that open the right tool.
- Diagnose: Windows Update check, crash timeline, event logs explained, dump inspection and Windows Activation.
- Performance: memory and drive-space summaries, samples and saved monitoring runs, pagefile guidance, power plans, and Battery & startup (battery wear, restarts and startup records).
- Maintain, Connect and Shield: large and duplicate files, startup entries with undo, a connection check that tests several DNS names, and Microsoft Defender explained.
- Help & community: remote help from someone you trust through Windows' own Quick Assist, with a scam warning first.
- Recovery keeps a record of supported changes and undoes them where Windows allows.

Testing: the automated checks and a UI smoke test pass on GitHub Actions for this build. Pre-release testing on Windows 11 is recorded in VALIDATION-v0.17.md; the Windows Update and Battery & startup checks were also run on a Windows 11 desktop and laptop. Some acceptance items, such as 150% and 200% display scaling, High Contrast and a clean-account run, are still partial.

Notes from the release candidates follow.
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
