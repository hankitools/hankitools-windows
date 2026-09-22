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
