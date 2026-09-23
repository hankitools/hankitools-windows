# Windows release acceptance

Use disposable files and a test PC/VM for actions that change settings. Record Windows version, display scaling, tester, date, exact executable SHA-256 and evidence notes in the generated acceptance.json. Do not mark untested or unavailable functionality as passed. If a feature cannot be validated, keep this candidate internal or disable that feature for the public build and revise the release scope/checklist.

| Acceptance ID | Required evidence |
| --- | --- |
| clean-install-launch | On a clean Windows 11 x64 standard-user account without a separately installed .NET runtime: extract the full portable ZIP, launch, check correct version, open every module, close/reopen. Try a path with spaces/non-ASCII characters. Verify a second instance is handled clearly. |
| dpi-keyboard-contrast | At 100%, 150% and 200% scaling: resize to minimum/default, use Tab/Shift+Tab/Enter/Escape and Ctrl+K, inspect wrapping, focus, contrast and Windows high-contrast mode. Test a mixed-DPI monitor move if available. |
| all-module-navigation | Confirm the smoke report passed; manually inspect every subview, result-card/details toggle, help dialog and collapsed quick access. No white bands, hidden actions or overlapping intro text. |
| readonly-diagnostics | Compare memory counters with Windows tools; read Event Logs, restricted Defender exclusions and a fixture dump. Missing/denied fields stay unknown; errors remain visible. Confirm no settings change. |
| scan-cancel-history | Scan harmless fixtures; cancel a folder scan; verify skipped/errors/history counts and responsiveness. Corrupt a disposable history copy and verify it is preserved. Do not create live malware. |
| cleanup-recycle-restore | In a disposable Documents subfolder, inventory, filter, multi-select and preview. Cancel once; recycle a fixture and restore via Recycle Bin. Verify network/removable/system/reparse targets are refused and no permanent-delete fallback exists. |
| startup-and-undo | With disposable test entries, exercise current-user and machine scopes, startup-folder moves, UAC denial, undo and conflict refusal after an external change. Restore fixtures and verify startup paths. |
| power-dns-and-undo | Record existing power/DNS settings; preview/cancel/apply/undo on a test adapter. Check invalid/unavailable plans, disconnected adapter and external-change conflicts. Confirm IPv6 unaffected; restore original settings. |
| defender-controls | Compare audit with Windows Security, test definition-update/quick-scan request and explicit cancellation. Observe UAC denial and status errors. Full-scan request and opt-in alerts must be checked; Defender policy may remediate independently. |
| monitor-save-load | Complete 30-second and longer sessions; cancel another; save/load baseline, compare, test malformed files and missing disk/GPU counters. Check idle/loaded behavior without treating differences as causal. |
| ai-consent-cancel | On a test API account with synthetic text only: verify all prior turns appear in review, declining sends nothing, approved request reaches the fixed endpoint, missing/invalid key and timeout are clear, cancellation retains draft. Never put the key in evidence notes. |
| upgrade-data-retention | Upgrade from the previous package with test history/journals. Confirm data remains readable, pending supported changes can be reconciled, and removing the application folder does not remove recovery data. Restore test settings before cleanup. |

| full-system-scan-activation | Start/cancel Community Full System Scan as standard user and administrator. Check partial/unknown/error counts and retained completed results; compare actual Windows states. Verify optional network/KMS contact is consent-bound, Activation remains free, no full product key appears, and global Cancel/close handles the new pages. Validate supported locales or document Unknown fallbacks. No repair runs during scanning. |
| diagnostic-history-privacy | Save/reopen/compare local scans, verify missing findings are not called resolved, inspect that raw paths/network/device IDs and keys are absent, test a corrupt disposable copy and explicit clear. Existing recovery and scanner history must remain intact. |
| community-edition-boundaries | On this exact Release executable set the development-edition environment variable and verify it cannot unlock paid automation. No startup login wall. Existing manual tools, full scan, raw findings, guidance and local history remain available. Confirm this candidate has no production license/identity/cloud/AI-explanation provider configured and makes no hidden upload. |

The 0.17 candidate's public Release scope is Community. Automated repair, scheduled
checks and Technician workflows are additive development paths until a production
entitlement source and their native acceptance are supplied. Before enabling them
in a distributable build, add mandatory action/restore/verification, task lifecycle,
credential-store and customer-export acceptance to the exact-executable package
gate; do not treat the three Community checks above as proof of those features.
See [the roadmap acceptance ledger](docs/ROADMAP-IMPLEMENTATION.md).

## Publisher checks before upload

1. Supply a real publisher identity, support/security/privacy contact and HTTPS download destination. Confirm logo/name/domain usage rights; no legal verification is claimed here.
2. Use a trusted signing identity and RFC3161 timestamp; verify signer details match the intended publisher. Do not ship certificate private keys. Signing does not guarantee SmartScreen reputation.
3. Build with the current supported runtime patch and record the build evidence. For this .NET 8 branch, upgrade before 10 November 2026; final packaging blocks after that date.
4. Complete the acceptance table on the exact signed executable, with real evidence. Do not copy previous results onto a rebuilt executable.
5. Run PACKAGE-RELEASE.ps1, then PUBLISH-RELEASE.ps1 to create a draft GitHub release from the verified ZIP. Independently compare the downloaded ZIP's SHA-256 with the published checksum. Verify extracted package signature on another Windows account.
6. Label this version “release candidate.” Do not market the experimental scanner as a full antivirus or imply guaranteed repair/performance gains. Publish the privacy description and limitations beside the download.
7. Keep the previous signed package and its checksum for rollback. No automatic updater is implemented; provide a clear manual update path and a monitored place for bug reports.
