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
