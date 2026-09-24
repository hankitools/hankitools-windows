# HANKI-101 — Existing diagnostic architecture audit

> The app is now split into Hanki System and Hanki Performance; see
> [docs/SYSTEM-AND-PERFORMANCE.md](docs/SYSTEM-AND-PERFORMANCE.md) (HANKI-ARCH-200).

Audited on 2026-09-22 against `main` commit
`5063669a209e2fbfa4ab224b1a13772c6ceefea7` (application version `0.16.0-rc.3`).
Scope: [issue #1](https://github.com/hankitools/hankitools-windows/issues/1).
This note is based on source inspection, not a Windows acceptance test or a claim
that every native operation works. It lives alongside the existing root-level
project documentation. Paths below are relative to the repository root.

**Conclusion:** Hanki has useful collectors, bounded file scanning, and two
conflict-aware recovery mechanisms. It does not yet have a common diagnostic
result contract, module registry, full-system scan coordinator, ranked finding
pipeline, or general repair workflow. Build on the existing collectors and
journals; a new application framework is unnecessary.

This change adds documentation only. HANKI-102/HANKI-103 are not implemented.
The future design below is a recommendation, not a description of existing
functionality or a substitute for those issues' specifications. All existing
Community/free/open-source features remain available; no licensing, payment,
authentication, Pro gating, or runtime behavior changes are introduced.

## 1. Project, composition and navigation

| Area | Current implementation and implication |
| --- | --- |
| Solution/application | `IgezziGuard.sln` contains `src/IgezziGuard/IgezziGuard.csproj`: one .NET 8 Windows Forms executable, `net8.0-windows`, assembly `HankiTools`, namespace `IgezziGuard`. Nullable references, deterministic builds and warnings-as-errors are enabled. No package references or service-host framework are declared in this project. |
| Entry point | `src/IgezziGuard/Program.cs` initializes WinForms on STA, installs fatal exception handlers, handles the explicit UI smoke-test mode, creates local storage and holds a per-user exclusive instance lock before running `HankiForm`. |
| Composition root | `src/IgezziGuard/HankiForm.cs` constructs panels directly, connects events, builds module/subview navigation and coordinates busy indicators and cancellation. There are no separate view-models or DI container/service registrations. |
| Shared UI | `src/IgezziGuard/HankiControls.cs`, `HankiTheme.cs`, `ToolIcon.cs` and `ToolLauncher.cs` implement the custom controls, theme, icons and tool discovery. `WorkspacePages`/`HankiTabs` and ordinary WinForms controls host the pages. UI is programmatic rather than XAML/MVVM. |
| Report surfaces | `src/IgezziGuard/DiagnosticPanel.cs` accepts a `Func<CancellationToken, Task<string>>` and optional presentation delegate. `src/IgezziGuard/ToolPage.cs` supplies action buttons, review dialog, cancellation, previous report, search, export and Assistant handoff. Several older panels have their own equivalents. |
| Presentation | `src/IgezziGuard/ResultPresentation.cs` defines `ResultCard(Title, Body)` and extracts Hanki-labelled fields from report strings. `ResultCardsView.cs` displays cards/details. Cards are presentation objects, not typed health findings. |
| Tests | `tests/HankiTools.Checks.csproj` is a separate console checks project, not a solution project. It links selected production source files directly. `tests/Program.cs` supplies assertions, fixtures and fake backends/HTTP transport. |

`HankiForm` exposes Home, Shield, Connect, Maintain, Performance, Diagnose,
Assistant, Recovery, Scan history and Help & community. Subviews include file
inventory, installed apps, usage, registry/folder startup, duplicates, snapshots,
short/long monitoring, tuning, crash timeline/logs/dumps/guidance, Defender
controls and advanced network tools. `SupportPanel.cs`, `AppInfo.cs` and
`DesktopShortcuts.cs` provide help/contact destinations and Windows handoffs.
Adding a module currently requires explicit composition and navigation wiring;
there is no discovery catalog of executable diagnostic modules.

## 2. Existing diagnostics and reusable components

All filenames in this table are under `src/IgezziGuard/`.

| Component | Collection/output today | Reuse boundary |
| --- | --- | --- |
| `PerformanceSnapshot.cs`: `Collect` | PSAPI `GetPerformanceInfo`/`EnumPageFiles`, registry pagefile/crash configuration, fixed-drive space and process working sets; returns a formatted string with unavailable-field notes. | Reuse collection and interpretation rules; expose measurements before formatting for reliable ranking. Pagefile inspection is read-only. |
| `PagefileAdvisor.cs`, `ReviewModels.cs`: `ReviewParsing` | Memory/pagefile explanations, commit guidance, installer date/size parsing. | Reuse pure rules; do not convert a single snapshot into a sizing guarantee or automatic setting change. |
| `PerformanceSession.cs`: `Sample` | `GetSystemTimes` and `GetPerformanceInfo`, 30–900 one-second samples, `IProgress<string>`, returns `PerformanceRun`. | Already a UI-independent async collector with structured data. Counter discontinuity fails the run. |
| `ExtendedPerformance.cs`: `ExtendedPerformancePanel` | CPU collection plus private `Devices` PowerShell/CIM disk/GPU sampler, saved baseline and comparison UI. | `PerformanceRun`, `DeviceSample`, `SavedSession` and `SessionValidation` in `SessionValidation.cs` are reusable. Device collection is currently private to the panel and needs a small extraction before shared orchestration. |
| `ReadOnlyDiagnostics.cs`: `Defender` | `Get-MpComputerStatus`/`Get-MpPreference` via `WindowsCommand.PowerShellCapture`; formatted by `DefenderAuditSummary.cs`. | Reuse the status/preferences selection and explicit unknown/error interpretation. Keep JSON stdout separate from tool notes. |
| `DefenderReview.cs`, `DefenderToolsPanel.cs` | Protection-alert interpretation plus status and recent-threat queries; optional in-app timer. | Reuse interpretation. Collection/control/UI responsibilities are mixed in the panel; alert flags are not proof of infection. |
| `CrashEventReader.cs`, `CrashTimeline.cs` | Native `EventLogReader`, provider/ID-aware `CrashEvent` evidence, time windows and coverage notes. Newest 500 queried events per log; descriptions capped. | Stronger starting point for structured event evidence than parsing displayed log text. Time proximity does not establish causality. |
| `ReadOnlyDiagnostics.cs`: `CrashLogs` | PowerShell `Get-WinEvent`, last seven days, up to 50 events per System/Application log; formatted report. | Reusable bounded query, but reports combine prose/JSON and unavailable/no-match notes. Do not mistake this for a full log export. |
| `DumpInspector.cs`: `Inspect(Stream)` | Bounded MDMP structural triage; PAGE header recognition; formatted evidence. | Reusable local parser; no symbolized root-cause result. `DumpAnalysisPanel.cs` separately launches an installed debugger after review. |
| `NetworkDiagnostics.cs`: `Run` | .NET adapter enumeration, DNS lookup and TCP probes; `IProgress<string>` output. | UI-independent checks, but needs result objects to distinguish findings from probe failures. External targets can see source IP. |
| `ReadOnlyDiagnostics.cs`: `Network`; `DiagnosticRules.cs` | Native `netsh wlan show interfaces`, bounded ICMP probes and ping/CPU arithmetic rules. | Reuse probe/rule logic, preserving localised Wi-Fi output and inconclusive packet-loss interpretation. |
| `NetworkToolsPanel.cs` | Traceroute, DNS-server comparisons, bounded Cloudflare download/upload test, selected-adapter DNS changes. | Query methods and consent live inside a UI class. Speed tests must remain an explicit network/data-use choice, not an invisible full-scan default. |
| `FileInventory.cs`: `FileInventory.Scan` | Recursive bounded inventory, `InventoryFile`, `InventoryProgress`, `InventoryResult`; skipped/errors/cap tracked. | Reuse directly with explicit target and coverage. Inventory size is logical size, not assured reclaimable storage. |
| `DuplicateFinder.cs` | Size grouping and SHA-256, 2 GiB/file and 20 GiB hash-read budget; `DuplicateResult`/`DuplicateGroup`. | Reuse read-only grouping and unchanged-file checks; hard links and scan exclusions affect conclusions. Bytewise equality helper is separate from grouping. |
| `InstalledApps.cs`: `Read`; `UsageReview.cs` | HKCU/HKLM uninstall registry in supported views, `InstalledApp`; opt-in mapped-executable observations described by `TrackedApp`. | Registry inventory is not complete package inventory or usage evidence. Missing usage stays unknown; do not label old install dates as unused. |
| `ScannerService.cs`, `SignatureDatabase.cs`, `Models.cs` | `ScanAsync` with `ScanProgress` and `ScanSummary`/`ScanFinding`; bundled EICAR signature and heuristics, skips files over 512 MiB, no archive unpacking. | Reuse only as an explicitly experimental file-scanning module with user-selected scope. `DetectionSeverity` is threat-specific, not a system-wide health severity. No live quarantine action is exposed. |

`TroubleshootingPanel` (in `DumpAnalysisPanel.cs`) builds static symptom-based
checklists. Its ticks stay in memory. It is neither a diagnostic dependency graph
nor an automatic repair decision engine.

## 3. Repairs, safety checks and verification today

| Action | Files/types | Existing safeguards and limits |
| --- | --- | --- |
| Registry Run disable/undo | `src/IgezziGuard/StartupPanel.cs`: `UserRunBackend`; `StartupActions.cs`: `IStartupBackend`, `StartupActions`, `StartupValue`, `StartupAction` | Supports current-user Run and machine 32/64-bit Run scopes. Preview/confirmation, exact value/type backup, journal before mutation, re-read before removal, post-write check and conflict refusal on restore. Synchronous operations; no cancellation token. This does not manage services, scheduled tasks or all Windows startup state. |
| Power plan, IPv4 DNS, startup-folder files | `src/IgezziGuard/ChangeJournal.cs`: `ISettingBackend`, `ChangeJournal`, `SettingChange`; `WindowsSettings.cs`: `WindowsSettings`, `TuningPanel`, `StartupFoldersPanel`, `RecoveryPanel` | Journal pending action before write, compare expected state again, write, read back and mark applied. Undo requires current state to match a recorded before/after state and verifies restoration. Sidecar lock and flushed temporary-file replacement protect journal updates. No general transaction or automatic rollback. |
| Setting backends | `src/IgezziGuard/WindowsSettings.cs` | `powercfg` with GUID validation; IPv4 adapter validation and `Set-DnsClientServerAddress`; startup files moved to a hashed local backup name with content-state checks and no-overwrite moves. DNS path deliberately targets IPv4. Startup-folder targets are top-level entries only, with reparse checks and 4 MiB state-hash limit. |
| Selected file deletion | `src/IgezziGuard/MaintainPanel.cs`, `DuplicatePanel.cs`, `RemovalDialogs.cs`, `FileInventory.cs`: `CleanupPolicy` (`LocationBlock`); `RecycleService.cs`: `RecycleService`, `RecycleGuard` | Review of one or many selected files, then Recycle Bin (default: Shell `IFileOperation` on STA, recycle-only flag and per-item veto) or permanent deletion after an explicit "can't be undone" confirmation. Local fixed drives only; Windows, program and system folders on any drive, Store app files, app data, other profiles, drive-root system files and Hanki's folder are protected; links, system/read-only files and files changed since the scan are refused. Each file is checked again right before it goes; failures don't stop the rest. Recorded in `RemovalLog` (System actions). |
| App uninstall and leftover entries | `AppsPanel.cs`, `InstalledApps.cs`, `AppRemoval.cs` | One app at a time through its own registered uninstaller (msiexec /x for Windows Installer products), started with ShellExecute so the uninstaller asks for elevation itself; afterwards the registration is re-read to confirm. Leftover entries (Windows Installer says not installed, or both the uninstaller and install folder are gone) can be removed after a `reg export` backup; entries for all users are deleted by an elevated `reg.exe delete` after Windows' prompt. Recorded in `RemovalLog`. |
| Defender scan/update/cancel requests | `src/IgezziGuard/DefenderToolsPanel.cs` | Reviewed `MpCmdRun.exe` launch via UAC. Launch success is not scan completion. Defender may remediate independently; Hanki cannot undo its policy actions. General Hanki cancellation does not cancel Defender scanning. |
| Windows handoffs | `src/IgezziGuard/DesktopShortcuts.cs`, `AppsPanel.cs`, `PerformancePanel.cs` | Opens Windows tools/settings. Pagefile changes and uninstall actions are not implemented as Hanki repair backends. Opening a settings page is not a verified repair. |

The two journals are the most useful repair abstractions to extend. Their state
schemas and scopes differ; do not merge or replace them as a prerequisite to
read-only diagnostics. A future repair adapter can reference an existing journal
entry rather than invent another undo mechanism. Neither journal currently
records a full-system finding ID or verifies that a user's symptom improved.
Read-back verifies setting state, not clinical/root-cause resolution.

## 4. Execution flows and async boundaries

### Read-only report

A button in `DiagnosticPanel` creates a CTS and invokes the supplied collector
through `Task.Run`. The collector calls Windows APIs or a command runner, returns
text, and an optional `ResultPresentation` delegate makes cards. The panel catches
cancellation and exceptions and updates UI state. A user may review/redact a local
export or hand the report to Assistant. There is no common diagnostic run saved
at this point.

`ToolPage.Run` similarly creates a CTS, disables page actions, runs work off the UI
thread and restores UI state. Its timer reports elapsed time, not collection
percentage. It retains one previous report in memory. Some derived-page actions
set `Output.Text` directly, so not every operation passes through this wrapper.

The file scanner instead uses `HankiForm.Run`, `Progress<ScanProgress>` and
`ScannerService.ScanAsync`, then formats findings and saves a summary through
`HistoryStore`. Cancellation throws out of scanning; it does not produce a
completed `ScanSummary`. Native inventory and process/event loops check tokens
between work items; interruption is cooperative, not instantaneous.

### Reviewed change

For power/DNS/startup-folder actions: read current state → show review dialog →
`ToolPage.Run` → `ChangeJournal.Apply` → lock/persist pending record → recheck →
`WindowsSettings.Write` → read-back verification → mark applied. Undo is a
separate reviewed operation. A failure or cancellation after the write can leave
a pending record and a changed machine; the UI instructs users to inspect Recovery.
Registry Run changes follow the analogous `StartupActions` flow synchronously.

### Application coordination

`HankiForm` enumerates busy panels and explicitly calls their cancellation methods.
Its closing handler stops opt-in timers, requests cancellation and asks the user
to close again when work finishes. Pages have independent CTS instances; the
shell's busy inventory is not a shared scheduler or a global repair lock.
`Progress<T>` created on the UI thread is already used to marshal updates.
A future coordinator should use that pattern rather than touching controls from
collector code. Shell COM recycling must retain its STA boundary; blindly moving
all existing actions into a worker task would break it.

## 5. Commands, Windows access, elevation and errors

`src/IgezziGuard/WindowsCommand.cs` is the preferred existing command boundary.
It uses explicit executable paths, `ArgumentList`, redirected streams and
`UseShellExecute=false`; PowerShell uses an encoded command and UTF-8 output.
`PowerShellCapture`/`RunCaptured` return separate stdout/stderr in `CommandOutput`;
`PowerShell`/`Run` return `DisplayText` that appends tool notes. Output retention is
capped at two million characters per stream. Default timeout is 60 seconds,
with caller overrides, linked cancellation and best-effort process-tree kill.

Failures are not yet a unified result type:

- A nonzero exit becomes an `IOException` containing exit code and stream text.
- Timeout becomes an `IOException`; caller cancellation propagates as
  `OperationCanceledException`. Launch failures can be `Win32Exception`.
- `ReadOnlyDiagnostics` retains a separate private command runner for logs/Wi-Fi:
  it appends exit codes/errors to text, returns timeout text, and reads streams
  without the central runner's output cap. A nonzero exit need not throw there.
- Several collectors catch local errors and preserve partial reports or unknown
  values. `NetworkDiagnostics` emits progress messages; file scanners aggregate
  error counts. These are different semantics, not interchangeable success flags.
- UI wrappers render exceptions as text/dialogs. `Program.ShowFatal` writes
  unhandled exceptions to `SecurityPaths.ErrorLog` best effort. There is no common
  structured diagnostic logger, correlation ID, or command execution history.

The manifest (`src/IgezziGuard/app.manifest`) is **asInvoker**. There is no general
privileged helper or repair elevation broker. Machine registry/DNS/common startup
changes depend on the current process's rights and can fail. `ToolPage` explains
manual administrator relaunch. `DesktopShortcuts` explicitly elevates requested
admin consoles; `DefenderToolsPanel` elevates Defender and handles UAC rejection
(code 1223). Neither gives the ordinary command runner elevated rights.

Windows integration spans registry views, native PSAPI/kernel counters, native
Event Log APIs, .NET network/process APIs, Shell COM and PowerShell/CIM. It is not
a single WMI service. `DumpAnalysisPanel` validates a Microsoft-signed CDB/KD,
locks the executable for reading, supplies fixed analysis commands, isolates
selected debugger environment variables and uses the debugger directory as its
working directory. Symbol-server access is opt-in; no dump upload is implemented.
This is not a sandbox for hostile dump/debugger content.

## 6. Persistence, settings and privacy

`src/IgezziGuard/SecurityPaths.cs` retains the legacy per-user root
`%LOCALAPPDATA%\IgezziGuard`. Portable app deletion does not remove this data.

| Store | Owner and content |
| --- | --- |
| `history.json` | `HistoryStore.cs`: up to 100 scan summaries, bounded/validated reads, atomic replacement. Corrupt/unreadable history is preserved and reported; this is not a detailed diagnostic evidence store. |
| `startup-actions.json`, `startup-machine-1.json`, `startup-machine-2.json` | `StartupPanel`/`StartupActions`: scope-specific saved commands, registry value kinds and action states. |
| `recovery.json`, `disabled-startup/` | `WindowsSettings`/`ChangeJournal`: before/after setting states and startup-file backups. Pending records support inspection/reconciliation, not automatic replay. |
| `app-observations.json` | `UsagePanel.cs`/`UsageReview.cs`: explicitly mapped executables and opt-in observations while Hanki runs. A 30-second timer is not historical usage telemetry. |
| `symbols/`, `errors.log`, `app-instance.lock` | Debugger cache, fatal error log and per-user instance guard. |
| User-selected session/report files | `ExtendedPerformancePanel` saves versioned JSON sessions; `SessionValidation` checks loaded values. Report exports are reviewed text. Baselines/previous reports elsewhere can remain memory-only. |

There is no central settings service. Panel state, persisted observations,
journals and explicit exports have separate lifecycles. `QuarantineItem` and the
quarantine path still exist in models/path definitions; these declarations do
not establish a functioning quarantine or restore feature.

`AssistantPanel.cs`/`AssistantPrompt.cs` prepare reviewed text. `AiChatPanel.cs`
and `AiClient.cs` provide optional explicit-consent API conversation with a fixed
Responses endpoint, `store=false`, no execution tools and cancellation. AI is
not a local diagnostic authority or repair executor. Logs, paths, network IDs,
startup commands, dump output and saved machine names can contain private data;
future persistence/export must preserve the current review boundary.

## 7. Tests, build and release constraints

`tests/Program.cs` exercises parsing, unknown-field handling, scanner/inventory
fixtures, duplicate checks, dump bounds, CPU/ping rules, timeline interpretation,
session validation, AI payload/mock transport, archive behavior, and journals
with fake backends including conflict/interruption/write-failure cases. This is
useful for future contracts/adapters; it is not live native Windows acceptance.
Because source is linked explicitly, new pure contract/adapter files will need
intentional test-project inclusion if that convention is retained.

`src/IgezziGuard/UiSmokeTest.cs` is invoked through the explicit app argument.
`BUILD-WINDOWS.ps1` runs checks, resolves a current .NET 8 runtime patch, publishes
a self-contained win-x64 single-file candidate with external signature data,
optionally signs it, runs UI smoke and writes build/acceptance metadata and ZIP
checksums. `build/Packaging.ps1` and `build/ArchiveBuilder.cs` handle archives.
`.github/workflows/windows-build.yml` runs the candidate workflow on Windows 2022
and uploads unsigned candidate/diagnostic artifacts. The solution build alone
is not the full validation path.

`PACKAGE-RELEASE.ps1` requires a valid timestamped signature, exact executable
and payload hashes, current runtime and recorded acceptance evidence before
public packaging. Its .NET 8 release-window cutoff is 2026-11-10 UTC.
`RELEASE-CHECKLIST.md`, `RELEASE-STATUS.md`, `PRIVACY.md` and `SECURITY.md` remain
relevant release constraints. This audit neither approves a release nor records
native acceptance as passed. No runtime build/tests are required to establish
that this documentation-only change leaves executable behavior unchanged.

## 8. Gaps and risks to preserve in the next design

| Gap/risk | Consequence and recommended boundary |
| --- | --- |
| Text is the result contract | Ranking cannot safely depend on English labels or localised native output. Add typed outcomes/evidence before aggregating; keep existing text presentation. |
| Mixed JSON and tool notes | Defender audit uses separate captured streams, but `DefenderToolsPanel.Status` and `ExtendedPerformancePanel.Devices` still parse the display-text return from `PowerShell`. Stderr can corrupt JSON parsing. Record this for a separately scoped fix; do not silently discard stderr or malformed output. |
| Missing/denied/partial data | Unknown is not healthy and a collector error is not a confirmed machine fault. Preserve coverage, skipped/error counts, truncation and collection timestamps. |
| Permissions and compatibility | Policies, non-Defender antivirus, missing cmdlets/debugger, inaccessible Event Logs, GPU drivers, registry views, disconnected adapters and CPU processor groups affect coverage. Detect unsupported/unavailable modules, avoid automatic elevation. The packaged target is Windows x64; no new ARM64/older-Windows assurance is implied. |
| Localisation | Existing registry GUID/type checks and invariant parsing are useful; native command text and formatted numbers can vary. Prefer stable API/JSON fields, not display labels, for new decisions. |
| Independent concurrent pages | Full scan could contend with monitoring, file hashing or setting changes and compare measurements from different windows. Start with explicit sequential module execution and clear timing/coverage; no silent repair during collection. |
| Cancellation versus mutation | Cancelling a process or UI operation is not rollback. Retain pending journal state and verification; Defender can continue outside Hanki. Do not promise global undo. |
| Stale previews and filesystem races | Journals recheck values and cleanup checks paths/metadata, but those checks are not a general OS transaction. Re-evaluate preconditions immediately before each approved action; retain existing conflict refusals. |
| Journal trust and scope | Local JSON contains sensitive before/after values; it is not a signed instruction source. Do not feed user-writable history into a new elevated generic executor. Preserve per-user identity, backend allowlists and validation. |
| Broad/destructive operations | File deletion, startup changes and DNS/power writes have distinct safety and threading needs. No system-wide cleanup, arbitrary command executor, registry optimizer, service disablement or automatic malware remediation is justified by this audit. |
| Evidence versus root cause | Empty logs/scans, failed pings, high commit, faulting modules and before/after measurements do not by themselves prove a cause or a repair benefit. Rank review priority with explanations, not certainty claims. |

## 9. Recommended target shape — not implemented

Keep the existing project and namespace. Suggested future files (these do **not**
exist in this change):

| Future location | Responsibility |
| --- | --- |
| `src/IgezziGuard/Diagnostics/DiagnosticResult.cs` | UI-independent common result envelope: stable module/finding identity, collection outcome, separate finding severity/priority, explanation, evidence/coverage, start/end time and optional recommendation/action reference. No controls or executable script strings. |
| `src/IgezziGuard/Diagnostics/IDiagnosticModule.cs` | Small async module contract accepting cancellation and progress, exposing identity and scope/capability requirements. It collects evidence, never performs a repair. |
| `src/IgezziGuard/Diagnostics/Modules/` | Thin adapters over existing collectors. Keep `ScanSummary`, `PerformanceRun`, `CrashEvent` and inventory models as domain evidence rather than forcing them into `ScanFinding`. |
| `src/IgezziGuard/Diagnostics/DiagnosticOrchestrator.cs` | Explicit list of modules, bounded sequencing, aggregate progress, per-module failure isolation, cancellation and final run coverage. No WinForms references, licensing or automatic elevation. |

Keep composition explicit in `HankiForm` (or a small factory only when needed).
A DI container, plugin discovery, new solution/project split or MVVM migration
is not required. Constructor injection already used by `ChangeJournal`,
`StartupActions` and `ScannerService` is a sufficient testability precedent.

The intended later flow is: **Full System Scan → ranked findings → explanations
and recommendations → user-approved action → action-specific safety checks →
repair → verification → results/history**. Collection and action execution must
remain separate. Initially, a recommendation can navigate to an existing reviewed
action instead of adding a new executor. Preserve explicit consent for external
probes, speed tests, symbol downloads and AI requests.

Progress should identify the current module and completed/total modules, with
optional native progress when a collector supports it. Do not fabricate a precise
percentage for unknown work. A coordinator-owned CTS should reach each adapter;
create `Progress<T>` on the UI thread and register the coordinator with the shell's
busy/cancel/close handling. Preserve completed module evidence on cancellation
and label the overall run incomplete; never relabel a cancelled module healthy.

### Minimal follow-up proposals for HANKI-102/HANKI-103

These are architecture recommendations only; read each issue before implementation
and reconcile its acceptance criteria. No implementation prerequisites are added
by this documentation change.

1. Agree the common result/module contract first, separating execution outcomes
   (completed, partial, unavailable, failed, cancelled) from health findings.
   Add narrow fake-module tests for unknown/error/cancellation semantics.
2. Adapt a small existing read-only collector first. Reuse typed performance or
   timeline evidence where possible. Extract only collection currently trapped
   in a panel when the selected module needs it; do not refactor all pages.
3. Add a small sequential coordinator with an explicit module list, progress and
   failure isolation. Test that one unavailable module does not erase successful
   results and that cancellation prevents subsequent work.
4. Introduce structured capture at JSON parsing boundaries as separately scoped
   needed work; retain raw evidence/errors. Do not broadly replace command runners
   or retrofit every report as part of the initial contract.
5. Connect the future aggregate view to current busy/cancel/navigation patterns
   without removing standalone Community tools. Decide versioned diagnostic-run
   persistence only when required; do not overload scan history or migrate repair
   journals unnecessarily. Repair orchestration needs its own approved scope.

## 10. Issue question cross-reference

| # | Audit answer |
| --- | --- |
| 1. Reusable diagnostics? | Section 2 names collectors, rules and concrete source paths, including UI-bound extraction limits. |
| 2. Reusable repairs? | Section 3 identifies both journals/backends, recycling and Defender boundaries. |
| 3. Common `DiagnosticResult` location? | Section 9 proposes `Diagnostics/DiagnosticResult.cs` within the existing application. |
| 4. Module/interface location? | Section 9 proposes `Diagnostics/IDiagnosticModule.cs` and thin `Diagnostics/Modules/` adapters. |
| 5. Orchestrator location? | Section 9 proposes UI-independent `Diagnostics/DiagnosticOrchestrator.cs` with explicit shell composition. |
| 6. Progress/cancellation integration? | Sections 4 and 9 cover CTS, UI-thread `Progress<T>`, busy/close wiring and incomplete-run semantics. |
| 7. Elevation and repair limits? | Sections 3 and 5 distinguish asInvoker, explicit runas, current-process rights and external Defender lifetime. |
| 8. Command/process failures? | Section 5 distinguishes exceptions, exit-code text, timeout, cancellation and partial-report errors. |
| 9. Full-scan obstacles? | Sections 1, 4 and 8 identify UI coupling, text contracts, independent lifecycles, missing ranking and coverage. |
| 10. Smallest changes before follow-ups? | Section 9 recommends contracts plus thin adapters/coordinator without a framework migration or broad refactor. |
| 11. Similar abstractions to extend? | Sections 1–3 identify `DiagnosticPanel`, `ToolPage`, domain records, `ISettingBackend` and `IStartupBackend`; preserve their distinct scopes. |
| 12. Compatibility/localisation/permissions/process/destruction risks? | Sections 5 and 8 record concrete limits and safety boundaries. |
