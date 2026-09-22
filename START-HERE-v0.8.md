# Hanki Tools v0.8

Close the existing app, extract the new source, run BUILD-WINDOWS.cmd and open HankiTools.exe in the newly created timestamped output folder. The window title shows v0.8. Prior local history and settings are retained.

## A clearer desktop

The left rail now contains all module navigation and administrator shortcuts. Home is an overview of six tool cards. A single page heading, compact module tabs and task-status footer replace the crowded top-level tab strip. Buttons have rounded edges, hover/pressed/selected states, visible keyboard focus and a system-color High Contrast fallback. Tab, Space and Enter continue to use native control semantics.

Hanki's visual identity uses deep pine backgrounds, snow-white text and ice-mint accents. A geometric snow-ridge H appears beside the wordmark and in the executable/window icon. Editable logo SVGs and the multi-resolution ICO are under src/IgezziGuard/Brand. Palette and design notes are in BRAND.md.

## Diagnose → Crash timeline

1. Choose Find restart markers. Hanki reads the last seven days of the System log for supported restart/bugcheck markers (Kernel-Power 41, EventLog 6008, WER-SystemErrorReporting 1001), matching provider as well as event ID. Multiple markers may refer to the same incident; they are not deduplicated into a crash count.
2. Select a marker to fill its **recorded local time**, or enter a known actual crash time. A restart marker's recorded time can be later than the crash. Event 6008's message may contain an earlier shutdown time. Hanki deliberately does not guess at localized date strings.
3. Choose a window of ±1–60 minutes and Build timeline. Events from System and Application are displayed chronologically, with provider, ID, record number, severity, message and cautious interpretation. Hardware, storage, dump, display, app-crash and memory-pressure events receive specific explanations when recognized.
4. Review / export allows editing and redaction before saving a local text report. Explain with Assistant prepares the report locally; it does not send it. Existing separate AI review/send gates remain in place. Very large reports may exceed the Assistant's 100,000-character limit; select relevant excerpts in that case.

Read limits: newest 500 queried events per log, 1,200 characters per message, warning/error/critical events and selected restart-related IDs. Caps, missing timestamps and access errors are disclosed. These are filtered windows, not a full EVTX export, automatic root-cause determination or crash-dump analysis. An empty result is not a clean bill of health. Ambiguous/invalid local times at daylight-saving transitions are rejected; use a nearby unambiguous center with a wider window.

Collection is read-only and cancellable. Reports can contain private names, paths and application information. No logs are cleared and no repairs run.

## Verification

Release cross-build: zero warnings/errors. All 74 non-destructive automated checks pass. The user reported v0.7 working on Windows; v0.8's redesigned native UI and new Event Log reader still need Windows acceptance testing.

Check the overview/sidebar, every module and nested tab, resized window, 100/150/200% display scaling, keyboard navigation, disabled button text, High Contrast and review dialogs. Compare marker timestamps and window contents with Event Viewer; test an empty window, blocked log access, cancellation and report export. Ensure prior startup/undo and AI review workflows remain accessible.

Microsoft references used for event semantics and native reading:
- https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/event-id-41-restart
- https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.eventing.reader.eventlogreader

Automated timeline checks cover provider/ID collisions, timestamp semantics, timezone offsets, inclusive window bounds, chronological order, unknown/empty results and cautious hardware/dump explanations. They do not execute Windows Event Log APIs in the Linux build environment.
