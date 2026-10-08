# Hanki Tools UI redesign: design and migration plan

Status: proposal for review. Nothing here is implemented yet. Written 2026-10-08 after the 0.19.0 acceptance run.

## Why

The product is good; the shell is the weak part. Observed during 0.19.0 acceptance testing:

- Unstable window sizing; wide layouts waste space and narrow ones fold buttons into "More".
- Several views are long text reports (Monitor, Event Logs details, Recovery) where cards, timelines and charts would read better.
- Review/confirm steps use plain `MessageBox`, and file pickers use the native dialog, so the important moments leave the app's design.
- Tabs inside tabs, plus 66 tools behind a finder.
- Custom-drawn WinForms controls make DPI scaling, keyboard focus and high contrast hard to guarantee. Those two acceptance checks (`dpi-keyboard-contrast`, and high contrast) are still open.

## What stays

- Every check, collector, journal and recovery rule. These are the tested part of the product.
- Copy, safety model (read-only by default, review before change, undo in Recovery), privacy statements.
- Portable, self-contained, code-signed `HankiTools.exe` on .NET 10; no installer, no updater, no telemetry.

## Size of the job (measured 2026-10-08)

- 140 source files, about 15.6k lines.
- About 6k lines are UI (47 files, 28 `ToolPage` panels). The rest is collectors, models, journals and tests.
- The UI is built in code with no view-models; some panels mix collection, interpretation and display (for example `DefenderToolsPanel`, `ExtendedPerformancePanel`). Those need a small extraction before they can be ported.
- `tests/HankiTools.Checks` links selected source files directly and runs about 740 checks.

## Recommended stack: WPF on .NET 10 with the built-in Fluent theme

| Option | For | Against |
| --- | --- | --- |
| WPF + Fluent theme (recommended) | Same runtime and publish model as today; no extra runtime to bundle; Windows 11 look with light, dark and accent colors; UI Automation, per-monitor DPI v2 and high contrast come from the framework; `WindowsFormsHost` lets old pages live inside the new shell during migration | The Fluent theme is newer and still marked experimental in some .NET releases; need our own sparkline/chart drawing or one NuGet package |
| WinUI 3 | Most current Fluent controls | Windows App SDK runtime to bundle and sign; larger package; unpackaged self-contained deployment has more moving parts; no WinForms hosting |
| Web shell (WebView2) | Fastest design iteration | A second runtime, weaker accessibility story, more to explain in privacy and security notes |

Decision needed: confirm WPF + Fluent. If you prefer WinUI 3, the plan below still holds except for the hosting step.

## Target structure

```
src/Hanki.Core/      class library, no UI: collectors, models, journals, recovery, licensing, scheduling
src/Hanki.App/       new WPF shell and pages (HankiTools.exe)
src/IgezziGuard/     legacy WinForms pages, shrinking to zero; removed in the last phase
tests/               ProjectReference to Hanki.Core instead of linking source files
```

Rules: Core never references a UI assembly; pages talk to Core through small interfaces and view-models; every confirmable change goes through the existing `ChangeJournal` so undo keeps working.

## The new shell (see the mockup in the chat)

- Left rail: Home, Fix my PC, Tune my PC, History, Help. Command palette (Ctrl+K) stays and becomes the main way into the 66 tools.
- Pages replace tab-in-tab layouts: a page header, one row of segmented sections, then content.
- Results are bordered rows with a severity dot, a one-line summary and an expander for evidence and next steps; a three-number summary (attention / OK / information) above.
- Review drawer replaces `MessageBox` for every change: Before / After, what is saved for undo, risk note, whether Windows will ask for administrator approval, then Cancel / Apply.
- Monitor and trends: small charts per counter plus the verdict line; the raw report remains one click away under Technical details.
- One progress and cancel affordance in the footer for all long operations.
- Remembered window size and position with a sensible minimum; narrow widths collapse the rail instead of folding buttons.
- Design tokens (color, spacing, type, radius) in one resource dictionary; light, dark and Windows high contrast supported from day one.

## Migration plan (each phase ships as a preview and gets its own acceptance pass)

0. Extract `Hanki.Core`. Move non-UI files, split the mixed panels, point the checks project at it. No visible change. Exit: all ~740 checks pass; build and signing unchanged.
1. New shell with the legacy pages hosted inside it through `WindowsFormsHost`. New navigation, palette, window behavior and footer; every module still works. Exit: UI smoke covers all 124 views.
2. Home and Fix my PC (results rows, summary, detail). Replace the old pages.
3. Review drawer and Recovery. Route every apply/undo through it; retire `MessageBox` review dialogs.
4. Maintain and Shield (files, apps, startup, duplicates, Defender, scanner).
5. Tune my PC and Performance Lab (gaming, GPU, CPU, memory, storage, monitor, comparisons) with charts.
6. Connect and Diagnose (network tools, Wi-Fi, event logs, crash timeline, dump analysis, activation, Windows Update).
7. Remove WinForms, rename project, update packaging/smoke scripts and docs.

## Risks and how to handle them

- Effort: this is a multi-week project even with the engine reused. Phases 0 and 1 give the visible shell early; the rest can ship incrementally or stop at any phase without leaving a broken app.
- Acceptance resets: every build changes the exe hash, and the release checklist requires evidence on the exact binary. Batch work into previews and run the full pass at phase ends, not per commit.
- Hosted WinForms pages in phase 1 will not match the new theme and cannot be drawn over (airspace). Accept this as temporary.
- Logic hidden in panels can change behavior when extracted. Add checks around each extraction before moving code.
- Fluent theme maturity: pin the .NET servicing version and verify high contrast and 200% scaling early in phase 1.

## Decisions needed before starting

1. Stack: WPF + Fluent (recommended), WinUI 3, or web shell.
2. Visual direction: keep the dark-first look and blue accent, or move to system light/dark following Windows.
3. Version and release plan: ship phases as 0.20 previews, or hold everything for one 1.0 launch.
4. Whether to keep the old WinForms build available (a branch/tag) while the new shell matures.

## Phase 1 notes (2026-10-08)

Implemented on `redesign/phase-1-shell`:

- `LegacyWorkspace` (WinForms) now owns every page, the "Find a tool" routes, task tracking and cancel/close logic. `HankiForm` is only the old chrome around it and stays reachable with `--legacy-shell`.
- New WPF shell (`src/IgezziGuard/Shell/`): navigation rail, header with back link and introduction, command palette (Ctrl+K, F1 for help, both working while a hosted page has focus), status/cancel footer, remembered window position, dark title bar. The workspace is hosted in a `WindowsFormsHost`.
- `--ui-smoke-test` now runs against the shell (all 124 hosted views, rail navigation, back link, palette filtering, screenshots through `PrintWindow`). `--legacy-ui-smoke-test` keeps the old check.

Findings that shape the next phases:

- Fluent `ThemeMode.Dark` must not be enabled while WinForms pages are hosted. It is process-wide and tints native tab panes and transparent controls (page background (44,52,60) instead of (16,19,24)). The shell is styled by `Theme.xaml` alone; only the title bar is darkened per window. Revisit when the last WinForms page is gone.
- The legacy pages use a fixed dark palette, so a light theme can only arrive page by page. `Theme.xaml` uses named brushes so a light dictionary can be added.
- Adding WPF to the exe project grows the self-contained single-file exe (WPF runtime assemblies). The size is recorded in the phase 1 build notes; removing WinForms in phase 7 recovers part of it.
- Enabling WPF drops `System.IO` and `System.Net.Http` from the implicit global usings; they are re-added in the project file.
- `HankiTools.exe --ui-smoke-test <report>` without a screenshots folder never exits on its own (the window refuses to close while some page counts as busy). The build always passes a folder, so it is unaffected; worth fixing when the smoke test is next touched.

## Phase 2 notes (2026-10-08)

Implemented on `redesign/phase-2-home-fix`: Home, Fix my PC (landing) and Full scan are native WPF pages in `src/IgezziGuard/Shell/Pages/`.

- `ShellWindow` shows a native page when the selected workspace tab has one (`nativeFactories`), otherwise the hosted workspace. The hidden workspace keeps its size (Visibility.Hidden) so hosted pages stay laid out. A native page that throws is logged and the hosted page is shown instead.
- `FullScanController` is the single scan engine (progress, cancel, repair review, history). The page, the footer's "Running" list, "Cancel tasks" and closing the window all use it.
- Home search, tiles, activity and last-scan summary use the same Core models as before (`HomeSearch`, `PcGlance`, `HomeActivity`, `StatusChips`); only the drawing is new.
- Dialogs that are still WinForms (repair review, customer report, About, Assistant hand-off) are opened with the window as owner.
- Gotcha: a WinForms control that has never had a window handle does not raise `SelectedIndexChanged`. Because Home is now native, the hosted workspace starts hidden, so the shell creates its handles up front (`workspace.Handle`, `Tabs.Handle`).
- Not yet in the native pages: Windows high-contrast colors (the WinForms pages switch to system colors; the native pages use the dark palette), and per-monitor DPI testing. Both belong in the phase 2 acceptance pass.
- The legacy Home, Dashboard and FullScan panels are still constructed inside the workspace so `--legacy-shell` keeps working; they are removed in phase 7.

## Phase 3 notes (2026-10-08)

Implemented on `redesign/phase-3-review-recovery`:

- `ReviewRequest` (Core, no UI types) describes any confirmation: title, introduction, Before/After rows, marked notes (undo, warning, administrator), a file list, mutually exclusive choices with an acknowledgement gate, checkable items, options and a validation callback. `ReviewText.Parse` turns the plain-text confirmations that pages already write into one, keeping every sentence.
- `ReviewDrawer` (WPF) shows it as a panel on the right edge of the window. Cancel has focus, Enter never confirms, Escape cancels, and blocked items can never be counted as ticked. `ReviewPresenter` installs it; when it is not installed (the legacy window) every caller falls back to the dialog it had before.
- Routed through the drawer: `ToolPage.Review` (about 25 confirmations in the hosted pages), the startup-change confirm, uninstall and leftover-entry confirms, the delete-files review (Recycle Bin or permanent with "I understand"), the repair review, the network-probes confirm and Recovery's undo.
- Recovery is a native page: plain names and values (power plans by name, empty DNS as Automatic), status words, and "Put back" through the drawer. The text rules live in `RecoveryText` (Core, tested).
- Still native message boxes (not changes to the system, so not part of this phase): AI chat clear and replace, the Windows Performance Options prompt, Quick Assist warnings, and the app-usage mapping prompts.
- The airspace rule shapes the design: WPF cannot draw over the hosted WinForms pages, so the drawer is a borderless owned window positioned over the right edge of the content area instead of an overlay inside the window.
- NVIDIA values in Recovery still show raw hex (0x0000000A); naming them needs the NVIDIA catalog and belongs with the Tune my PC port.

## Phase 4 notes (2026-10-08)

Implemented on `redesign/phase-4-maintain-shield` in three commits:

- Shared controls: `DataList` (sortable, virtualized dark table), `DiagnosisView`, `SubTabs`, `TaskTracker`, dark field/list/chip styles. Native pages register their work with `TaskTracker`, so the footer lists it, Cancel tasks cancels it and closing the window waits for it.
- Route support: `LegacyWorkspace.CurrentPath` and `NativePage.OnRoute` let Find a tool entries such as "Maintain / Apps & storage" select the native tab.
- Maintain is native: Files & storage, Apps, Startup, Duplicates. Usage review and Startup folders are still hosted pages inside it.
- Shield is native: Defender audit, Scans & alerts (with the optional minute-by-minute watch), File scanner, File scan history.
- All confirmations (delete, uninstall, startup change, repair, Defender scan/update/cancel, duplicate recycle) go through the review drawer.

Findings:

- The native folder picker (`OpenFolderDialog`) works; the earlier "won't close" was my test tool, not the app. Pick the folder by double-clicking into it, then Select Folder.
- Defender scan, update and cancel were reviewed in the drawer but not run: they raise UAC. Everything else on Shield was exercised live.
- Not yet native: Usage review, Startup folders; Diagnose, Connect, the Performance area, Assistant, Help pages, History pages.

## Phase 5 notes (2026-10-08), first slice

Implemented on `redesign/phase-5-performance`:

- `TrendChart` and a dark ComboBox style.
- Performance Lab is native: Monitor (real measurement, verdict, cards, one trend chart per counter, baseline / compare / save to sessions / save and load file), Bottleneck Analyzer, Stutter Diagnostics. Comparisons is still a hosted page; Benchmarks and Advanced Tuning are the same "still being built" notes.
- Bug found only by running it: WPF controls must be read on the UI thread, and the Monitor read its duration list from inside the background measurement. The UI check cannot see this because it never measures. Rule for native pages: read control values before `Task.Run`.

Not done in phase 5 (all still hosted): Tune my PC (plan, apply through Recovery), Gaming (overview, games, NVIDIA, AMD), GPU, CPU and power plans, Memory and pagefile, Storage, Performance sessions. Tune my PC changes Windows, NVIDIA and AMD settings, so it deserves its own careful port and live test.

## Phase 5 notes, continued (2026-10-08)

- Tune my PC (the Performance landing) is native: scenario cards, G-SYNC question, plan, reviewed apply, tweaks Hanki won't make, tool cards. Change reviews use the drawer (non-optional changes ticked, optional unticked, "you change it" steps as cards with an Open settings link).
- GPU, CPU (power plans still hosted), Memory (memory and pagefile still hosted), Storage and the Gaming overview are native. Games, NVIDIA and AMD Radeon are still hosted inside the native Gaming page; so are Comparisons, Power plans and Memory & pagefile. Performance sessions is still hosted.
- `ReportView` is the pattern for a page of read-only checks: buttons run on the UI thread, heavy work moves to a background thread inside the check.
- Testing without touching the user's screen: the UI check runs on a private hidden desktop (CreateDesktop + CreateProcess with lpDesktop), and native pages are captured with RenderTargetBitmap because PrintWindow cannot see WPF on a hidden desktop. `BUILD-WINDOWS.ps1` still starts the smoke test as a normal window; it should use the hidden desktop too.
- Not run for real (they change the system): applying a Tune plan, ReTrim, the processor change.
