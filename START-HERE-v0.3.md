# Hanki Tools v0.3 — source preview

This supersedes earlier start guides. Build on Windows 11 with .NET SDK 8 or later by double-clicking BUILD-WINDOWS.cmd. Run HankiTools.exe in the new timestamped dist folder. Keep the Data directory beside it. Hanki itself does not request elevation.

## Quick links

Persistent left sidebar: Windows PowerShell (Admin), Command Prompt (Admin), File Explorer. Admin shortcuts use normal Windows UAC approval. Declining does nothing and is not retried. Absolute Windows executable paths are used, not PATH searches. PowerShell starts without profiles and CMD with AutoRun disabled. These terminals do not execute commands automatically.

## Maintain

Choose a folder or drive. Inventory runs in the background and can be cancelled. Click column headers to sort (size is numeric); Largest 100 shows the biggest files matching the current filters. All files removes the top-100 limit. Filename substring, one extension and minimum MiB filters apply when clicked. Applying filters/sorting clears selection. Open location reveals a selected file in Explorer; it does not execute it.

Scans skip reparse points (including junctions, symlinks and many cloud placeholders), count access errors and stop at 100,000 files. A capped inventory is labelled partial: choose a smaller folder for a complete result. Cancelled inventories are discarded. Sizes are logical lengths, not physical disk allocation; hard links and compression can make totals differ from disk usage.

## Cleanup safeguards and limitations

- Select at most 100 individual files and click Preview cleanup. Every exact path and total size is shown. Default action is Cancel. If any selected file is blocked, the recycle confirmation is disabled.
- Cleanup is limited to local fixed-drive personal Desktop, Documents, Downloads, Pictures, Music and Videos. Downloads currently uses the standard user-profile Downloads path; custom relocated Downloads folders are scan-only unless also under another allowed location.
- Windows, Program Files, ProgramData, AppData, Hanki's installation folder, system/read-only/offline files, network/removable drives and reparse paths are blocked. Files outside the allowed personal locations are scan-only.
- Size, modification/creation times and path restrictions are checked again before and during the operation. These are best-effort checks, not an atomic filesystem security boundary against another process deliberately racing the operation. Close programs using the selected files.
- Windows IFileOperation is configured with FOFX_RECYCLEONDELETE and FOFX_EARLYFAILURE. A callback vetoes transfers without the recycle flag and unexpected paths. No permanent-delete fallback, broad cleanup scripts, directory deletion or Recycle Bin emptying is implemented.
- Files are processed individually; cancellation or the first error stops the remaining batch. Already recycled files stay recycled and are listed in the result. Restore using Windows Recycle Bin.
- Recycling does NOT immediately free disk space. Cloud-synced deletions may propagate to other devices even if the file is locally hydrated.
- Shield quarantine remains excluded from compilation. Maintain cleanup is a separate, explicitly confirmed personal-file action.

## Validation

The complete Windows-targeted project was cross-compiled successfully with .NET SDK 8.0.408 and Windows Desktop reference pack 8.0.15: zero warnings and zero errors. All 11 non-destructive checks passed on Linux. See tests/ for checks of inventory, path boundaries, cancellation and recycle veto logic:

    dotnet run --project tests/HankiTools.Checks.csproj

These tests only create/remove their own unique temporary fixture folder. They never invoke Shell recycling. Windows UI layout, UAC and actual Recycle Bin behavior remain unverified and must be validated before treating this as a production release. Test only on disposable copies first. This archive contains source, not a self-contained executable; BUILD-WINDOWS.cmd creates that on Windows.

### Manual Windows acceptance matrix

1. Run all three shortcuts. Approve and decline UAC separately; Hanki remains open. No command executes in either terminal.
2. Scan a fixture folder containing 1-byte, 20-byte, 2-KiB and larger files, nested folders, Unicode/spaced names. Verify numeric sorting, filters, top 100, selection totals and Explorer reveal.
3. Cancel a large scan. Try to close during a scan; closing waits for cancellation. Shield/Connect and Maintain operations should not overlap.
4. Preview a disposable file in Documents. Cancel: file unchanged. Confirm: verify it appears in the Windows Recycle Bin and can be restored byte-for-byte.
5. Change a fixture after scanning; preview should block it until rescanned. Test a locked fixture: failure/cancellation must stop the batch.
6. Test Windows/Program Files/AppData, a network share, removable drive, junction, symlink, offline placeholder and read-only file. They must not be eligible for cleanup.
7. With a disposable test account/VM, disable recycling or exceed its capacity. Hanki must refuse/cancel rather than permanently delete. Do not ship until this test passes.
8. Test Windows display scaling 100%, 150%, 200% and minimum window size. Check preview scrollability and keyboard cancellation.

API reference: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags
