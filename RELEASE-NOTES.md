# Hanki Tools 0.19.0-rc.7 — Help tiles at high DPI

An unsigned preview fixing clipped Help cards at increased Windows display scaling.

- **Readable Help cards.** Cards now scale vertically with display DPI, and their columns and spacing adapt to the available scaled width so headings and descriptions have room to wrap.

# Hanki Tools 0.19.0-rc.6 — Home search and Quick access

An unsigned preview removing duplicate search UI and fixing the sidebar shortcuts menu.

- **One search entry point.** Removed the extra search box from Home; Find a tool in the header remains available.
- **Quick access menu.** Replaced the expanding shortcut list with a compact popup that opens above its sidebar button, preserving all six shortcuts and the Windows UAC note.

# Hanki Tools 0.19.0-rc.5 — A cleaner Home at high DPI

An unsigned preview with a simpler Home screen and a fix for clipped PC-at-a-glance tiles.

- **Less clutter on Home.** Recent actions no longer repeat scans, checks and changes already available in History. The scan and Tune my PC status stay with their main actions.
- **Clearer first view.** The two main actions use less vertical space, with shorter headings and more room for the PC overview.
- **Scaling fix.** PC-at-a-glance tiles now scale their height and grid density with Windows display DPI, so usage bars no longer cross values and details remain visible at higher scaling.
- **More legible navigation.** The sidebar now scales with Windows display DPI, keeps the brand and destinations inside the panel, and uses consistent text sizing and row spacing.

Validation includes the automated home/localization checks and the UI smoke test at the current 150% display scaling. The smoke test checks tile heights and sidebar sizing at 100%, 150% and 200%; manually inspect those settings on Windows before public distribution.

# Hanki Tools 0.19.0-rc.4 — Clearer results and guided troubleshooting

An unsigned preview with simpler scan results and a guided internet troubleshooting flow.

- **See what needs attention.** Full scans start with actionable findings. Expand other completed checks and missing evidence separately. Each finding offers a manual next step, with technical details kept one click away.
- **Return to where you were.** Back and Alt+Left restore the previous tool tab and keep your troubleshooting state.
- **One internet step at a time.** Connect opens a read-only guided check, suggests the next step and lets you test again or confirm that the problem is resolved. More help opens detailed tools and the Hanki knowledge base.
- **Relevant repair actions.** Repair review appears only for a recent scan with a supported proposal, administrator access and an eligible edition. Manual guidance remains available.
- **Clearer controls.** New core controls and summary counts support all twelve interface languages. Back links and action labels fit better at 150% scaling. Detailed diagnostic explanations may still fall back to English.
- **App-hang history fix.** Reading a hung application's event no longer fails because its missing module name was serialized as an object.

Validation covers the full non-destructive Windows test suite, localization checks, navigation and fixed-fixture usability checks. The release workflow requires a successful main-branch Windows build, including packaged-app smoke testing and the twelve-language UI matrix. Code signing and broader native-action acceptance remain pending. This preview does not change network settings automatically.

# Hanki Tools 0.19.0-rc.3 — Twelve interface languages

An unsigned preview adding the same language choices as the website articles.

- **Twelve languages.** English, Finnish, German, Spanish, French, Italian, Japanese, Korean, Dutch, Polish, Brazilian Portuguese, and Simplified Chinese.
- **Your language, remembered.** Choose Language in the sidebar, or follow the Windows display language. Changes apply on the next launch. Regional date and number formats stay unchanged.
- **Translated everyday controls.** Navigation, page introductions, home and tuning choices, dashboard values, tabs, primary actions, common report controls, and search use the selected language. English search aliases remain available.
- **Coverage.** This is the first localization pass. Detailed diagnostic reports, repair explanations, guided troubleshooting steps, and some secondary forms and status messages remain in English. Native-speaker review is pending; see docs/LOCALIZATION.md.

Validation includes automated catalog and preference checks, the full Windows CI suite, packaged-app smoke testing, and 124 UI view visits in each of the 12 languages at two window sizes (1,488 visits). Code signing and native Windows acceptance remain pending. Smart App Control can block this unsigned preview on protected PCs; publication does not change its trust status.

# Hanki Tools 0.19.0-rc.2 — Tactical Vision

An unsigned preview for testing, adding per-game NVIDIA color saturation controls.

- **Tactical Vision.** In Gaming → Games, select a game and choose Tactical Vision. Enable it and choose Digital Vibrance from 51–100%; the default is 70%. Existing games remain off until enabled.
- **Automatic activation and restoration.** Keep Hanki open. Colors boost while the selected game's executable is focused and return to their previous setting when you switch away, close the game, or exit Hanki. A recovery record allows Hanki to restore colors after an interrupted session when reopened with the display connected.
- **Display scope.** The boost affects the entire display containing the game window. Requires an SDR display connected directly to NVIDIA. HDR is unsupported, and some drivers may not expose the required Digital Vibrance controls. This is a color adjustment, with no FPS improvement claimed.
- **Manual settings respected.** An existing stronger boost is preserved, and manual changes in NVIDIA Control Panel take precedence on restoration.

Testing: build, gaming regression checks, restoration and recovery tests, and UI smoke checks passed for the feature commit. Live NVIDIA color output, fullscreen behavior and driver compatibility still need real-game verification. Native Windows acceptance and code signing remain pending. See docs/TACTICAL-VISION.md for usage and recovery details.

# Hanki Tools 0.19.0-rc.1 — Tune my PC looks further

An unsigned preview for testing. Tune my PC checks more of your PC and turns more of what Hanki already measures into advice. It still changes nothing until you apply, and each new change is saved in Recovery first.

- **Laptops.** Gaming on a laptop whose screen runs through the integrated graphics suggests the MUX switch or Advanced Optimus (the dedicated-GPU mode in your laptop maker's app). Low power suggests hybrid mode, sets the power mode for battery as well as plugged in, and offers to start Energy saver at 50% battery.
- **Auto HDR.** Gaming + Quality with HDR on offers to turn on Auto HDR for DirectX 11 and 12 games.
- **Graphics driver age.** A driver six months old is an optional update step; after a year it's a required one.
- **Network (Gaming + Performance).** A slow or half-duplex wired link is a cable step, Wi-Fi gets an optional "use a cable" step, and there's a step to pause game-launcher and Windows Update downloads while you play.
- **Hardware.** PCIe lane, Resizable BAR and motherboard-output findings from the gaming check now appear in the plan, as does memory that ran short earlier.
- **Processor helpers.** Ryzen 9 X3D processors with two core groups get a check for AMD's 3D V-Cache optimizer and the Balanced power plan. Processors Intel lists for Application Optimization (APO) get a step when Intel Dynamic Tuning isn't installed.
- **Creative work: color.** A YCbCr 4:2:2 or 4:2:0 signal, or 6-bit color on an external display, is a step. On Windows 11 24H2, automatic color management is suggested for wide-gamut displays.
- **Streaming.** OBS profiles that encode with x264 on the processor get a step to use NVENC, AMF or Quick Sync instead.
- **Your measured games.** Launch and measure now saves what limited each run. Tune my PC turns each game's latest run into advice for that game: for example, lower textures when video memory was full, or raise graphics settings for free when the processor was the limit.

Testing: 737 automated checks and a UI smoke test. Reading the new facts was checked on a Windows 11 desktop, and the Energy saver change was written, read back and restored on a throwaway copy of a power plan. Not yet tried on a laptop, an X3D or APO processor, or with OBS installed.

# Hanki Tools 0.18.0 — Fix my PC and Tune my PC

Hanki Tools 0.18 is the first full release of the 0.18 series. It is not code-signed yet: signing is pending, so Windows SmartScreen warns before the first run. Compare the published SHA-256 checksum before running it.

What's new in 0.18:

- **Two ways in.** Fix my PC finds what's wrong and fixes it safely; Tune my PC sets your PC up for gaming, creative work or low power. Five areas in the sidebar replace about 20 items. Home searches in everyday words ("laggy", "bsod", "fps") and shows your PC at a glance and your recent activity.
- **Tune my PC.** Choose Gaming + Performance, Gaming + Quality, Creative work or Low power. Hanki reads your display, Windows, mouse, graphics driver, processor, memory and storage settings and shows a plan: each change before and after, and the steps only you can take. Nothing changes until you apply, and every change is saved in Recovery first.
- **Gaming.** A read-only gaming check (refresh rate, which GPU games use, Game Mode, power, overlays and busy background apps, PCIe lanes, Resizable BAR), your games from Steam, Epic, GOG and other launchers, Launch and measure, NVIDIA per-game and global settings with presets, and AMD Radeon settings.
- **GPU, CPU, Memory, Storage and Performance Lab.** Graphics hardware and displays, processor limits and power plans, memory speed and headroom, drive types, TRIM and DirectStorage readiness. The Lab measures every second, with frame rates for DirectX games when Hanki runs as administrator; the Bottleneck Analyzer and Stutter Diagnostics show their evidence, including downloads during a run, and before-and-after comparisons are kept as Performance sessions.
- **Fix my PC** adds checks for apps that keep crashing, network link speed and clock sync, and points out significant gaming configuration problems without changing them.
- **Maintain** deletes files (to the Recycle Bin by default, or permanently after you confirm), uninstalls apps with their own uninstaller, and removes leftover app entries after saving a .reg backup.
- **History** keeps System actions and Performance sessions apart; Recovery undoes changes from both areas.
- **Built on .NET 10**, supported until November 2028. The portable ZIP is smaller, about 45 MB.

Since 0.18.0-rc.8: Home and Recent activity say how far a cancelled scan got instead of "nothing needs attention", older names were replaced throughout, and clock-sync evidence names the time server without its IP address.

Testing: 717 automated checks and a UI smoke test pass on GitHub Actions for this build. Pre-release testing on Windows 11 is recorded in VALIDATION-v0.18.md. Not yet tried by a person on real hardware: applying Tune my PC changes, NVIDIA and AMD global presets, Launch and measure, uninstalling apps, permanent deletion, and 150% and 200% display scaling. Every change is reviewed first and can be undone in Recovery; please report anything that looks wrong.

Notes from the release candidates follow.

# Hanki Tools 0.18.0-rc.8 — built on .NET 10

An unsigned preview for testing. Hanki now runs on .NET 10, the long-term support release that Microsoft supports until November 2028; .NET 8 support ends on 10 November 2026. The portable package still includes everything it needs, so there is nothing extra to install. Features and behavior are the same as in 0.18.0-rc.7.

# Hanki Tools 0.18.0-rc.7 — new checks: app crashes, link speed, clock, PCIe lanes, Resizable BAR, downloads

An unsigned preview for testing. Every new check only reads what Windows already records; nothing is changed or sent.

- **App crashes (Fix my PC).** Apps that crashed or stopped responding three or more times in the last 14 days, from the same records Reliability Monitor uses, with the part they crashed in. Crashes in the graphics driver point to a driver update; single crashes are listed as information.
- **Network connection speed (Fix my PC).** A wired connection stuck at 100 Mbps or in half duplex usually means a damaged cable or a bad port, and is flagged. Wi-Fi shows its link rate.
- **Clock sync (Fix my PC).** When Windows last set its clock and from which server, failed syncs (often blocked time requests), and automatic time turned off.
- **Graphics card connection (Tune my PC → Gaming and GPU).** A desktop graphics card running on x4 or fewer of its lanes (secondary slot, riser, shared M.2 lanes) is flagged in Fix my PC too; x8 is explained as a small cost. Resizable BAR shows as on or off for cards that use it, and is a Fix my PC item for Intel Arc, which needs it.
- **Downloads during Performance Lab runs.** The monitor now measures data arriving over the network. The Monitor summary, Bottleneck Analyzer and Stutter Diagnostics say when something was downloading during a run, and which program was most likely receiving it (Steam, Windows Update, a browser, cloud sync).

# Hanki Tools 0.18.0-rc.6 — layout and memory fixes

An unsigned preview for testing. It fixes issues found while reviewing 0.18.0-rc.5; everything else behaves as in rc.5.

- **Evenly sized tabs.** Views whose name contains "&", such as Shield → Scans & alerts and Memory → Memory & pagefile, no longer grow wider each time the window lays out.
- **Clearer memory headroom.** Memory health gives the peak since the last restart in GB instead of a percentage that could read above 100%. When programs reserved more memory than Windows can back today, it says so: Windows had grown the pagefile to make room, which can cause stutter while you play.

# Hanki Tools 0.18.0-rc.5 — Home search, deleting files and uninstalling apps

An unsigned preview for testing. Deleting files, uninstalling apps and removing leftover app entries are new and haven't been tried on real PCs yet: every removal is confirmed first, and the Recycle Bin (the default) and .reg backups let you undo most of them.

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
