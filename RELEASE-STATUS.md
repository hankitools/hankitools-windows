> **Historical:** this describes 0.16.0-rc.3. For the current release, see README.md, RELEASE-NOTES.md and VALIDATION-v0.17.md.

# Release status • 0.16.0-rc.3

Prepared 22 September 2026. **Ready for Windows release-candidate testing; not yet cleared for public distribution.** No public upload, certificate signing or native Windows action was performed in this environment.

RC2 addresses the Windows test failure reported after “pending action restores removed startup value.” Both deliberate directory-as-file backup failures now handle the two expected platform exception types, while retaining the no-mutation assertions. A fresh Windows run is still required.

RC3 adds four archive checks: a held source reader, exact bytes, no overwrite and rejection of an output ZIP inside its source tree. ArchiveBuilder.cs is compiled by the test harness and also used by Windows PowerShell. Its Windows PowerShell integration and actual executable-loading locks remain unverified here.

## Verified here

| Check | Result and scope |
| --- | --- |
| Clean Release cross-build | Passed; 0 warnings, 0 errors with the normal analyzer set and warnings-as-errors. Linux SDK 8.0.408 / Windows Desktop reference pack 8.0.15. This is not the self-contained distribution binary. |
| Automated checks | 123 passed. Pure logic, temporary file fixtures, mock HTTP/settings/startup backends and child-process timeout/cancellation. See validation/checks.txt. |
| Data preservation | Tests cover corrupt scan-history preservation, 100-entry retention, journal-before-write, interrupted changes and external-state undo conflicts. |
| Result interpretation | Missing/disabled Defender fields, restricted exclusions, legacy dates, missing memory counters, localized values and warning preservation covered. |
| Saved monitoring import | Tests reject invalid dates, negative/nonfinite/out-of-range metrics, missing machine identity and inconsistent mean/peak values. |
| Scanner | Harmless fixture, missing target and cancellation tested. Experimental heuristics are not a malware-detection efficacy evaluation. |
| Extended analyzer audit | Recommended .NET analyzers were run as a separate advisory audit. 88 warnings before follow-up fixes, mainly parameter naming, implicit display culture and allocations. Relevant explicit-comparison/cancellation/fallback-exception items were corrected; this report does not claim the full Recommended rule set is warning-free. |
| Static palette contrast | Main text/report, secondary text/report, raised controls, primary label/blue, intro/workspace and sidebar text pairs pass 4.5:1; lowest tested ratio 5.18:1. Not a full accessibility or native-rendering audit. See validation/text-contrast.json. |
| Source package | Source-only archive excludes bin/obj/dist and private signing/key material; ZIP integrity checked before delivery. |

## Still unverified / required before public release

- Windows Forms rendering and interaction, DPI/high contrast, screen-reader behavior and the newly added UI smoke mode. Its structural pass is useful but cannot substitute for visual inspection.
- PowerShell build/sign/package scripts on a real Windows SDK environment. They were reviewed but PowerShell is unavailable in this Linux workspace.
- Fresh self-contained Windows x64 publish against the current runtime patch; runtime downloads could not be reached from this environment. Do not distribute the local cross-build as a portable executable.
- Native Defender controls, registry startup, Recycle Bin, power/DNS apply/undo, WMI GPU/disk counters and installed debugger behavior. Use the recorded acceptance checklist on disposable fixtures/test settings.
- Live DNS/transfer services and a reviewed OpenAI API request. Automated AI tests use mocks, not your API key or billing.
- Timestamped publisher signature, accurate public publisher/support/security/privacy contact, final download destination and final downloaded ZIP checksum verification.
- Runtime maintenance: Hanki targets .NET 10, supported until 10 November 2028 (migrated from .NET 8, whose support ends 10 November 2026). The release script resolves a current .NET 10 patch before packaging and blocks this branch after the .NET 10 deadline.

## Release artifacts supplied

- Source project and 123-check harness.
- BUILD-WINDOWS.cmd / BUILD-WINDOWS.ps1: checks → current-runtime self-contained publish → optional signing → structural UI smoke → candidate ZIP and SHA-256.
- PACKAGE-RELEASE.ps1: signature/runtime/smoke/acceptance/content-hash gates → public ZIP and SHA-256, excluding tester notes.
- Portable user guide, privacy behavior, release notes, security guidance and concrete acceptance IDs.

The acceptance record is operator-supplied evidence, not a tamper-proof attestation or certification. An RC label must remain visible in public download copy until the release owner deliberately promotes a tested build. No automatic updater, installer, crash upload or telemetry service has been added.
