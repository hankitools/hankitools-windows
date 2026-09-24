# Hanki System and Hanki Performance (HANKI-ARCH-200)

Hanki Tools is one app with two product areas. This note is the contract that the
HANKI-GAME-200, HANKI-GPU-100 and HANKI-PERF-300 stories build on.

| | Hanki System | Hanki Performance |
| --- | --- | --- |
| Purpose | Diagnose, repair, maintain, protect and recover Windows. | Measure, analyze, optimize and verify performance. |
| Message | Find what's wrong and fix it safely. | Understand what limits performance and optimize it. |
| Starts from | Something is broken or unhealthy. | The PC works, but could be faster or smoother. |
| Workflow | Problem → diagnosis → recommended repair → approval → repair → verification → recovery history. | Baseline → measure → analyze → recommendation → approval → test the change → measure again → keep or revert. |
| Words | Issue, finding, repair, fix, health, recovery, verification. | Observation, bottleneck, recommendation, optimization, test, baseline, comparison, performance session. |
| History | System actions | Performance sessions |

A healthy system can have performance opportunities. Performance text never calls an
opportunity a fault; `Navigation.UsesFaultLanguage` is checked against every
Performance page introduction in the tests.

## Navigation

`src/IgezziGuard/Navigation.cs` is the single list of destinations. `HankiForm` builds
one workspace page per entry, the sidebar groups, the area label above each title
(HANKI SYSTEM / HANKI PERFORMANCE) and the introductions from it.

- Home
- SYSTEM: Overview, Fix My PC, Diagnose, Maintain, Shield, Connect, Recovery
- PERFORMANCE: Overview, Gaming, GPU, CPU, Memory, Storage, Performance Lab
  (Monitor, Comparisons, Bottleneck Analyzer, Stutter Diagnostics, Benchmarks, Advanced Tuning)
- HISTORY: System actions (timeline, saved scans), Performance sessions
- SUPPORT: Assistant, Help & community, Hanki Pro

Both areas share the shell, typography, controls and accessibility behavior. The
Performance area uses its own accent (`HankiTheme.PerformanceAccent`) for the area
label, sidebar group, selected item and cards; System keeps the Hanki blue.

### Where the 0.17 tools went

Nothing was removed and nothing free moved behind Pro. `Navigation.Moved` records each
move and the UI smoke test fails if a destination is missing.

| 0.17 location | Now |
| --- | --- |
| Full system scan | Fix My PC |
| Diagnostic history | System actions → Saved scans |
| Scan history | Shield → File scan history |
| Performance → Snapshot / pagefile | Memory → Memory & pagefile |
| Performance → 30-second sample | Performance Lab → Comparisons |
| Performance → Long monitoring / saved runs | Performance Lab → Monitor |
| Performance → Power tuning | CPU → Power plans |
| Performance → Battery & startup | Diagnose → Battery & startup (a health check) |

## Findings: System health vs Performance

Both areas use the common `DiagnosticResult` model. Performance modules:

- use `DiagnosticCategory.Performance` and store their latest run in
  `performance-checks.json` (`PerformanceStatus`), never in the system scan history;
- use `Healthy` for "no opportunity", `Informational` for observations and `Warning`
  for an optimization opportunity. `Critical` is reserved for a real misconfiguration
  (for example a game forced onto the integrated GPU of a hybrid laptop);
- carry current state, recommended state and whether Hanki can apply it in the
  explanation/metadata, so the Fix My PC integration (GAME-214, GPU-112) can show only
  meaningful items and link back to the Performance page.

Fix My PC may surface significant gaming configuration issues. It never runs tuning or
overclocking, and minor preferences stay in Hanki Performance.

## Performance sessions

`src/IgezziGuard/Performance/PerformanceSessions.cs`

- `PerformanceMeasurement`: a time window with metrics keyed by `PerformanceMetrics`
  ids (CPU, busiest thread, memory, disk, GPU load, temperature and video memory, and FPS, 1% low and frame time when frames were captured).
- `PerformanceSession`: name, baseline, the Recovery journal ids of the changes tested,
  the measurement afterwards, and the outcome.
- `PerformanceComparison`: a conservative verdict. Without frame-rate data a session is
  only *Measured*; runs must be comparable (same source, at least 75% of the length);
  a change must exceed 3% and 1% lows must not fall before Hanki says *Improved*.
- `PerformanceSessionStore`: `performance-sessions.json`, newest 100.

Performance Lab → Monitor saves runs as sessions today. Optimization tests
(GAME-210/212, GPU-109, PERF-312) add their changes and after-measurement.

## History separation and shared rollback

Recovery (`ChangeJournal`, `recovery.json`) stays the one rollback mechanism for both
areas. Journal kinds listed in `Navigation.PerformanceChangeKinds` ("Power plan",
"Display mode", "GPU preference", "NVIDIA setting", "NVIDIA global setting",
"Processor power") belong to Performance sessions; everything else appears in System
actions (`src/IgezziGuard/History/SystemActions.cs`, together with scans and the repair
audit).

A reviewed Performance change to a setting Hanki already changed replaces that change
(`ChangeReview`): Hanki undoes its earlier change, but only while the setting still has
Hanki's value, and records the new one from your original value. Recovery so keeps one
active entry per setting, and its undo always goes back to where you started.

## Safety distinction

System actions restore a known healthy state. Performance changes are experiments, so
every write follows: baseline → snapshot (Recovery journal) → apply only approved
changes → measure → keep or revert. Selecting a profile never changes anything by
itself.

Goals and Optimize This Game change a game's own driver profile. NVIDIA's global
profile (every game without its own value) changes only from Gaming → NVIDIA, where you
choose a preset or a value yourself and review it like any other change.

Rules for every GAME, GPU and PERF story (GAME-211, PERF-314):

- Each recommendation names the measurement or configuration that triggered it.
- No change without a documented technical reason; "no change recommended" is a valid
  answer, and more tweaks are not better.
- Never: disabling security mitigations or Defender, indiscriminate service changes,
  HPET/timer or BCDEdit tweaks, scheduler/affinity registry packs, Nagle/TCP tweaks,
  undocumented driver flags, standby-memory purging, pagefile disabling, shader-cache
  clearing on every launch, firmware changes.
- Advanced Tuning (GPU-110, vendor auto-tuning) lives only in Performance Lab →
  Advanced Tuning, needs explicit confirmation, and is never started by a profile,
  Optimize This Game or Fix My PC.

## Shared services

Do not duplicate these per area: hardware detection (one GPU/display service for
Gaming, GPU and Storage), `WindowsCommand`, elevation checks, `ChangeJournal`/Recovery,
`DiagnosticResult`/`DiagnosticOrchestrator`, entitlements, `LocalJson` storage and the
Hanki controls and theme.

## Implementation status (September 2026)

| Story | Where | Status |
| --- | --- | --- |
| ARCH-200 | `Navigation.cs`, `AreaPanels.cs`, `HankiForm.cs` | Done: areas, home, Performance shell, Lab, separate histories, docs, tests. |
| GPU-101 | `Performance/GraphicsProbe.cs`, `GpuPanel.cs` | Done: DXGI adapters in Windows' high-performance order, drivers, VRAM, displays and modes, HDR, vendor interfaces. |
| GPU-102 | `Performance/Nvidia.cs` | Done: NVAPI DRS read of global and per-game profiles; every setting id is checked against the driver's own name. |
| GPU-103, GAME-203 | – | Not yet: AMD ADLX is detected, but Radeon settings aren't read. Needs AMD hardware to build and test. |
| GPU-104, GAME-201 | `Performance/GamingHealth.cs`, Gaming → Overview | Done: read-only Gaming Health Scan in the common result model. |
| GPU-105, GAME-204 | `Performance/GamingProfiles.cs` | Done: six vendor-neutral goals turned into reviewed, capability-aware changes. |
| GPU-106 | `Performance/PerformanceSettings.cs`, `ChangeReview` | Done: snapshot in Recovery, apply, verify, restore; partial failures reported per change. |
| GPU-107, GAME-202 | Gaming → Games, `Nvidia.WriteApplicationSetting` | Done for NVIDIA: per-game profile changes (a "Hanki: game.exe" profile when NVIDIA has none), NVIDIA's own values restorable, Hanki profiles removable. |
| GPU-108 | `Performance/WindowsGamingProbe.cs` | Done: Game Mode, GPU scheduling, per-app GPU choices, power mode and processor limits (read); GPU choice and processor maximum can be applied. |
| GPU-109, GAME-212, PERF-312 | Lab → Monitor, `PerformanceComparison` | Done: baselines, comparable before/after, keep or restore through Performance sessions. |
| GPU-110 | Lab → Advanced Tuning | Deliberately separate and not started (placeholder only). |
| GPU-113 | Gaming → NVIDIA, `Performance/NvidiaPresets.cs` | Done: NVIDIA global settings (16 from the public NVAPI SDK, each checked against the driver's own name) with built-in presets, a settings editor and your own saved presets (`nvidia-presets.json`). Changes are reviewed, recorded in Recovery as "NVIDIA global setting" and restorable. |
| GPU-111, GAME-213 | Gaming page | Done: overview with goals and review, games tab. |
| GPU-112, GAME-214 | `Performance/GamingDiagnostic.cs` | Done: Fix My PC shows only high-impact gaming findings, pointing to Gaming; never applies them. |
| GAME-205 | `Performance/GameLibrary.cs` | Done: Steam, Epic, GOG and publisher records, manual .exe, rescans without duplicates. |
| GAME-206, PERF-310, PERF-311 | `Performance/MonitorModel.cs`, Lab → Bottleneck Analyzer | Done: evidence-based limiter with confidence and recommendations. |
| GAME-207 | Gaming scan + `Display mode` changes | Done: refresh-rate mismatches per display, applied with a 15-second keep-or-revert. |
| GAME-208 | `GamingProfiles.Conflicts` | Done for NVIDIA limits and vertical sync; in-game and third-party limiters aren't visible, which the results say. |
| GAME-209, PERF-309 | Lab → Stutter Diagnostics | Done: spikes matched to CPU, GPU, memory, disk, heat and background activity, as possibilities. |
| GAME-210 | Games → Optimize this game | Done: analysis, reviewed proposal, Recovery, session; measure before and after in Lab → Monitor. |
| GAME-211, PERF-314 | `Performance/Guardrails.cs` | Done: only documented change kinds can be applied; common internet tweaks listed with reasons. |
| PERF-301 | `Performance/SystemMonitor.cs`, `PerformanceRecorder.cs` | Done: per-thread CPU, effective clock, GPU load/VRAM/temperature/clock (vendor-neutral), memory, disk, busiest programs, DXGI frame times (administrator). |
| PERF-302 | CPU → Processor | Done. |
| PERF-303, 304, 305 | Memory → Memory health | Done: commit headroom, module speed vs rating (XMP/EXPO guidance only), channels, pagefile health. |
| PERF-306, 307, 308 | Storage | Done: media per drive, games on hard disks, free space, TRIM, optimization schedule, ReTrim (administrator, SSDs only), DirectStorage readiness. |
| PERF-313 | Performance overview | Done: one read-only check across areas. |
