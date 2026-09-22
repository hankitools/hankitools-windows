# v0.6 validation

## Completed in Linux

- .NET 8 Release build targeting Windows: zero errors, zero warnings.
- All 36 non-destructive checks pass. Coverage includes path/recycling guards, file inventory, app metadata parsing, pagefile interpretation, report preparation, CPU delta arithmetic and ICMP summary formatting.
- No native Windows collector, Windows Forms interaction, Defender query, Wi-Fi command or recycling operation was executed in this environment.

## Required Windows acceptance checks

1. Build with `BUILD-WINDOWS.cmd`; launch on Windows x64. Check nested tabs, resizing, readable outputs and dashboard shortcuts.
2. Diagnose: compare selected events against Event Viewer; verify seven-day bounds, 50-event cap per log and 500-character message truncation. Exercise empty/inaccessible logs and cancellation.
3. Defender: compare reported fields with local PowerShell/Windows Security. Check normal-user permission failures, managed policies and devices using another antivirus. Verify no settings changed.
4. Connect: test Ethernet-only, disconnected Wi-Fi, connected Wi-Fi, location access denied, VPN and multiple gateways. Compare native output and probe results. Test blocked ICMP and cancellation. Check non-English output for encoding issues.
5. Performance: compare approximate CPU and commit readings against Task Manager under idle and controlled load, allowing different measurement windows. Check baseline reset, repeated runs, cancellation and failed runs; a failed run must not become the baseline. Verify high-core-count limitations and memory values on the target machine.
6. Export: edit/redact before save, cancel either dialog, confirm overwrite prompt, exercise unwritable targets. Verify Assistant preparation sends nothing and clipboard review gates still apply.
7. Close during each collection: cancellation completes without orphaned command processes or UI exceptions. Reopen and verify the performance baseline was not persisted.
8. Run the earlier file-recycling, UAC shortcut, scanner and app-inventory acceptance checks before distributing the app. Quarantine, restore and tuning must remain disabled.

Passing arithmetic checks and cross-compilation does not validate native Windows behavior or establish diagnostic accuracy.
