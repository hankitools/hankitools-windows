# v0.17 pre-release validation (RC3 → RC4)

Native pre-release testing of the unsigned **0.17.0-rc.3** candidate on one Windows 11 PC, driven through Windows UI Automation against the exact packaged executable, with fixes re-verified on **0.17.0-rc.4**. This is a developer test pass, **not** release acceptance: `acceptance.json` is intentionally left for a named tester on the exact signed executable, as RELEASE-CHECKLIST.md requires.

- Environment: Windows 11 Home 10.0.26200, x64, 100% scaling (96 DPI), English UI with Finnish regional adapter names, standard (non-elevated) token, Wi-Fi, Microsoft Defender active.
- RC3 package: ZIP matched its `.sha256`; `build-info.json` hash matched the executable; runtime 8.0.31; unsigned.
- RC4 build: all 358 automated checks and the structural UI smoke test (74 views at 1320×880 and 1120×740) pass. The smoke test now also fails if a Guided checks step points to a missing tool.
- Every setting touched was recorded first and restored afterwards: power plan, IPv4/IPv6 DNS, user and machine Run keys, both Startup folders and Hanki's data folder all matched the pre-test baseline at the end.

## Defects found and fixed in RC4

| Found in | Defect | Fix and evidence |
| --- | --- | --- |
| readonly-diagnostics | Dump inspection rejected a real Windows Error Reporting minidump ("Duplicate stream type"). WER dumps contain several reserved `UnusedStream` (type 0) entries. | Type-0 entries are skipped; real duplicates are still rejected. New tests for both. RC4 inspects the same WER dump: 14 streams, exception 0xE0434352. |
| full-system-scan-activation | Scan summaries and history comparison showed UTC ("7:01 AM" at 10:01 local). Scan history, Recovery and technician reports used the same pattern. | Local time everywhere; RC4 scan line matched the wall clock. |
| startup-and-undo | Startup folders listed `desktop.ini` (hidden system folder settings) as a startup file and allowed disabling it. The test disabled it by accident; Recovery restored it intact. | Hidden system files and `desktop.ini` are excluded. RC4 lists only the fixture; disable and Recovery restore verified. |
| power-dns-and-undo | A DNS change refused without administrator rights stayed "Pending / inspect" in Recovery, offering an undo for a change that never happened and blocking later changes to that adapter. | A refused write whose state is verified unchanged is closed as "Not applied (unchanged)"; undo is declined; later changes are allowed. New tests; verified natively on RC4. |
| monitor-save-load, scan-cancel-history | Cancelling a read-only tool said "Any completed actions remain in effect. Check Recovery…". | Read-only tools say nothing was changed. Verified on RC4. |
| diagnostic-history-privacy, full-system-scan-activation | The Home last-scan card and the scan counts strip were painted without accessible text. | Both expose their text to screen readers. |

## Results by acceptance ID

Status: **Passed** = the required evidence was produced on this PC; **Partial** = the listed parts passed and the rest still needs a tester; **Not tested**.

| Acceptance ID | Status | Evidence on this PC | Still required |
| --- | --- | --- | --- |
| clean-install-launch | Partial | ZIP extracted to a path with spaces and non-ASCII characters; correct version in title; smoke test passed from there; second instance shows "Hanki is already open…" and exits on OK; close and reopen work. | Clean standard-user account without a separately installed .NET runtime. |
| dpi-keyboard-contrast | Partial | 100% scaling at minimum and default sizes; Ctrl+K opens Find a tool, Escape closes it, F1 opens Help. | 150% and 200% scaling, Tab/Shift+Tab focus review, High Contrast, mixed-DPI move. |
| all-module-navigation | Partial | Smoke report passed (74 views); summary/details toggles exercised in Connect, Diagnose, Performance and Shield; screenshots of every module reviewed during development. | Manual pass over every subview, help dialog and collapsed Quick access. |
| readonly-diagnostics | Passed | Available RAM 6.37 vs Windows 6.36 GiB at the same moment; 7-day event counts identical to `Get-WinEvent` (0 critical / 142 errors / 359 warnings); Defender real-time state matched `Get-MpComputerStatus`; exclusions stayed "unknown" without admin; real WER dump inspected (RC4); no setting changed. | — |
| scan-cancel-history | Partial | Harmless fixture folder: "No suspicious files found"; cancelling a System32 scan works; history counts and local time correct. | Corrupting a disposable copy of the file-scan history; detection path (no live malware created). |
| cleanup-recycle-restore | Partial | Disposable Documents folder: inventory (junction not followed), type chips, preview, one cancel, recycle ("No permanent-delete fallback was used"), restore via Recycle Bin. | Native refusal of network, removable and system targets (covered by unit tests only). |
| startup-and-undo | Partial | Current-user Run fixture: cancel keeps it, disable, exact undo, refusal after an external change; Startup-folder move and Recovery restore (RC4). | Machine scope with administrator fixtures and UAC denial. |
| power-dns-and-undo | Partial | Power plan apply and Recovery undo; DNS as standard user fails clearly, stays unchanged and is recorded as not applied (RC4); IPv6 unchanged. | DNS apply/undo as administrator on a test adapter; disconnected adapter; external-change conflict for power. |
| defender-controls | Partial | Audit matches Windows Security state; opt-in alerts off by default, first check within 60 s, off again on request. | Quick/full scan and definition-update requests, cancellation and UAC denial (needs a person at the UAC prompt). |
| monitor-save-load | Partial | Cancelled session; completed 30-second session with plain summary; save, load baseline; malformed file rejected. | Longer sessions; a PC without disk/GPU counters. |
| ai-consent-cancel | Partial | Missing key refused; key pasted into the message refused; review dialog requires consent; declining keeps the draft and opens no connection to OpenAI. | Approved request, invalid key and timeout on a test API account with synthetic text. |
| upgrade-data-retention | Partial | Scan history and journals written by earlier 0.16/0.17 builds were read by RC3 and RC4. | Upgrade from the previous signed package, pending-change reconciliation, deleting the app folder. |
| full-system-scan-activation | Partial | Standard-user scan (10/10 checks); cancel keeps completed results (6/10); network-probe consent declined starts nothing; Activation report contains no product key. | Administrator run; other Windows locales. |
| diagnostic-history-privacy | Passed | Open and compare use evidence wording ("Not observed (not proof of resolution)"); saved history contains no paths, IP/MAC addresses, device IDs or product keys; a corrupt copy is preserved and never overwritten; clear asks first; recovery and startup journals untouched. | — |
| community-edition-boundaries | Passed | Release executable with `HANKI_DEVELOPMENT_EDITION=Pro`: automatic repair, scheduling and customer reports stay unavailable; no login wall; no network connections at idle. | — |

## Blocking a verified release

1. **Signing.** PACKAGE-RELEASE.ps1 requires a valid, timestamped Authenticode signature. No code-signing certificate or `signtool` is available on the test PC. Options include Azure Trusted Signing or an OV/EV certificate; signing produces a new executable, so acceptance must be repeated on it.
2. **Named tester sign-off** on the exact signed executable for all 15 IDs, including the "Still required" items above.
3. **Runtime window.** This branch targets .NET 8; PACKAGE-RELEASE.ps1 refuses to package after 10 November 2026.

Automation notes for repeat runs: Windows 11 message boxes and file dialogs expose no button patterns to UI Automation; they respond to the keyboard (Enter, Escape, Alt+N for file names, Alt+F then Tab/Enter for folders).
