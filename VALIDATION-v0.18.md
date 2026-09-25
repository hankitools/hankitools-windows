# v0.18 pre-release validation (RC8)

Developer test pass of the published, unsigned **0.18.0-rc.8** package on one Windows 11 PC, plus the same checks on
main after the fixes below. This is **not** release acceptance: `acceptance.json` is left for a named tester on a
signed executable, as RELEASE-CHECKLIST.md requires.

- Environment: Windows 11 Home 10.0.26200, x64, 100% scaling (96 DPI), standard (non-elevated) token, Intel Core
  i7-14700F, NVIDIA GeForce RTX 5070 (driver 617.14), 16 GB DDR4, Wi-Fi, Microsoft Defender active.
- Package: the ZIP matched its `.sha256`; `build-info.json` hash matched the executable; version 0.18.0-rc.8; bundled
  .NET runtime 10.0.12; unsigned. The executable contains none of the Debug-only override names
  (`HANKI_DEVELOPMENT_EDITION`, `HANKI_POLAR_*`, the Polar sandbox address), so it is a Release build.
- Automated: 717 checks pass on main; the packaged executable's UI smoke test passed (124 views at 1320×880 and
  1120×740, 17 screenshots).
- Method: nothing ran on the visible desktop. The packaged executable ran on a separate, invisible Windows desktop;
  everything else was driven off-screen, in process, against the same source. Fixtures were disposable, and every
  setting touched was restored: the Run key, the Uninstall key, the per-app GPU choice and the NVIDIA test profile
  match their state before the test.

## Defects found and fixed after RC8

| Found in | Defect | Fix and evidence |
| --- | --- | --- |
| full-system-scan-activation | Home and Recent activity said "nothing needs attention" for a scan cancelled after 5 of 15 checks. | A cancelled or stopped scan now says how far it got ("Cancelled after 5 of 15 checks") and asks for a new scan. Checks skipped for lack of administrator rights still count as run, so a normal scan isn't flagged. New test in HomeChecks. |
| all-module-navigation | Old names in eight places: "Full System Scan" (Hanki Pro page, System actions timeline and intro, the clear-history question), "Open Fix My PC" and "Run a full system scan" (guided checks), "Performance → Memory / Gaming" (two findings), "Startup / undo" (Recovery intro). | Now Fix my PC, Full scan, Tune my PC → Memory / Gaming and Maintain → Startup entries. |
| diagnostic-history-privacy | Clock-sync evidence included the time server's raw source, with IP addresses. Saved history was not affected. | Evidence names the server only. New test in HealthChecks. |

## Results by acceptance ID

Status: **Passed** = the required evidence was produced on this PC; **Partial** = the listed parts passed and the
rest still needs a tester; **Not tested**.

| Acceptance ID | Status | Evidence on this PC | Still required |
| --- | --- | --- | --- |
| clean-install-launch | Partial | Extracted to a path with spaces and non-ASCII characters (`Hanki testi ÄÖ`); window title shows 0.18.0-rc.8; smoke test passed from there; a second copy shows only the "Hanki Tools" message and waits; no TCP connections or UDP endpoints during 20 seconds idle without a licence. | Clean standard-user account; close and reopen by hand. |
| dpi-keyboard-contrast | Partial | 100% scaling: all 51 views rendered at the default and minimum window sizes and reviewed; nothing clipped or overlapping, rows wrap. | 150% and 200% scaling, Tab/Shift+Tab focus, High Contrast, mixed-DPI move. |
| all-module-navigation | Partial | Smoke report passed (124 views); every subview reviewed at both sizes (17 contact sheets); the copy fixes above. | Help dialog and collapsed Quick access by hand. |
| readonly-diagnostics | Passed | Memory in use: Hanki 9.50 GB, Windows 9.50 GB at the same moment. Event logs: with Hanki's own filter Windows has 678 System records (Hanki reads the newest 500 and says the cap was reached) and 179 Application records (Hanki read 178; one more was logged in between). Defender audit matches `Get-MpComputerStatus` (antivirus, real-time, behavior, downloaded-file, network and tamper protection on; Normal mode); exclusions stay unknown without administrator rights. A real minidump of `ping.exe` was inspected (12 streams, modules listed). No setting changed. | — |
| scan-cancel-history | Partial | Harmless fixture folder: 3 files scanned, nothing found; a System32 scan cancelled after 1 second. | Corrupting a disposable copy of the file-scan history natively. |
| cleanup-recycle-restore | Partial | Refused: Windows, Program Files, AppData (Local and Roaming), another profile and Hanki's own files. A Documents fixture was recycled by Hanki, found in the Recycle Bin by its original folder, and restored. | Multi-select Delete…, cancel, Delete permanently with confirmation, network and removable targets, the System actions entry. |
| app-uninstall-leftover | Partial | A current-user entry whose folder doesn't exist was listed as a leftover, removed after a `.reg` backup, and restored from that backup. | Uninstalling a disposable MSI and EXE app, declining UAC once. |
| startup-and-undo | Partial | Current-user Run fixture: disabled, undone to the exact value; undo refused after an outside change. | Machine scope with UAC; Startup-folder move (passed in 0.17, unchanged since). |
| power-dns-and-undo | Partial | Made-up program's GPU choice applied and undone through a temporary Recovery journal; processor maximum rewritten with its own value (100%); display mode validated in test-only mode (1920×1080 @ 320 Hz, nothing changed). NVIDIA per-game setting on a made-up executable written, read back, reset, its Hanki profile removed; removing NVIDIA's own profile refused. | Power plan and DNS apply/undo on a test adapter; Tune my PC Review and apply, then Undo; NVIDIA and AMD global presets. |
| defender-controls | Partial | Audit matches Windows Security. | Quick/full scan and definition-update requests, cancellation, UAC denial. |
| monitor-save-load | Partial | 30-second measurement (30 samples) saved and loaded; network download rate measured (up to 0.25 MB/s, no download reported); a file without samples refused. | Longer session with a game and frame capture (administrator). |
| ai-consent-cancel | Not tested | Unchanged since 0.17 (Partial there). | Approved request, invalid key and timeout on a test API account. |
| upgrade-data-retention | Partial | Scan history and journals written since 0.17 are read by RC8 and main; the data folder is separate from the app folder. | Upgrade from the previous signed package. |
| full-system-scan-activation | Partial | Standard-user scan (15 of 15 checks); cancel keeps completed results (5 of 15). | Administrator run; other Windows locales. |
| diagnostic-history-privacy | Passed | Saved history, including the three new modules' results, contains no IPv4 or MAC addresses, profile paths, device instance IDs or product keys. | — |
| community-edition-boundaries | Passed | Release executable: the development-edition and Polar overrides are compiled out; with `HANKI_DEVELOPMENT_EDITION=Pro` set it stays Community and makes no connections at idle. | — |

## New in 0.18

| Feature | Evidence on this PC | Still required |
| --- | --- | --- |
| Tune my PC (scan) | Gaming + Performance plan read (no changes needed on this PC, 4 steps for the person). | Review and apply, then Undo, on a PC with changes to make. |
| Gaming check, GPU connection, Resizable BAR | PCIe 4.0 x16 (card supports 5.0) and a 16 GB Resizable BAR window read correctly. | — |
| App crashes, network link speed, clock sync | Live reads pass on this PC (tests `live: …`). | — |
| Downloads during Performance Lab runs | Network rate measured live; detection covered by tests. | A run while something downloads. |
| NVIDIA per-game settings | Dummy executable write/read/reset/remove (above). | Global presets on a real driver. |
| AMD Radeon settings | Not tested: no AMD hardware. | A Radeon PC. |
| Launch and measure | Not tested. | A game from the list. |
| Delete files, uninstall apps | Recycle and leftover-entry paths (above). | Delete permanently; a real uninstall. |
| .NET 10 | Built with SDK 10.0.401; runtime 10.0.12 bundled; UI identical to the .NET 8 builds. | — |

## Blocking a verified release

1. **Signing.** PACKAGE-RELEASE.ps1 requires a valid, timestamped Authenticode signature; signing produces a new
   executable, so acceptance must be repeated on it.
2. **Named tester sign-off** on the exact signed executable for all 16 IDs, including the "Still required" items.

The runtime window is no longer a blocker: the .NET 10 build can be packaged until 10 November 2028.
