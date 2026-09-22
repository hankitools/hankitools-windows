# Hanki Tools v0.4 — Apps & storage + Performance

This is a source preview. On Windows 11 with .NET SDK 8 or later, run BUILD-WINDOWS.cmd, then open HankiTools.exe in the new timestamped dist folder. Keep its Data directory beside it. Read this guide first; earlier guides describe older versions.

## Maintain > Apps & storage

Click Refresh installed apps to read machine/current-user uninstall registrations in both registry views. Search names/publishers and click columns to sort by reported size, date, name or version. Missing sizes and dates remain Unknown; size sorting is numeric and missing values sort last. Installation dates may reflect servicing. Registry views are deduplicated for identical registrations within the same user/machine scope.

Reported size is the installer's estimate, not a folder scan or a promise of reclaimable space. Usage is not measured: no app is labelled unused, unwanted or safe to remove. Store/MSIX and portable apps may not appear. Other user accounts are not inventoried.

Review / uninstall in Windows opens the standard Windows Installed apps page after confirmation. For a selected app, the prompt tells you its name to find there. It does not deep-link to or uninstall that specific app. Hanki does not run registry-supplied uninstall commands or delete app files. All uninstall decisions happen in Windows Settings. Cancelling the Hanki confirmation launches nothing.

## Performance > Memory & processes

Take / refresh snapshot explicitly collects:

- Usable physical RAM and available RAM.
- System committed memory, current commit limit and peak commit since boot.
- Active pagefile allocated size, current usage and peak usage from Windows.
- Configured PagingFiles registry entries, labelled separately from active allocation. A 0/0 entry means system-managed sizing for that entry, not confirmation of the global automatic-management checkbox.
- Configured crash-dump mode, with an explicit warning that full dump readiness is not validated.
- Available space on local fixed drives.
- Top 20 process working sets. Protected or exited processes can be unavailable; shared memory means working sets cannot simply be added together.

Failures are reported as unknown/unavailable instead of inventing values. Values are snapshots, not continuously sampled measurements. RAM commit is not disk pagefile usage. CPU percentages, disk I/O, startup impact and sustained memory pressure are not measured here.

The 90% commit message is a review prompt for this snapshot, not a diagnosis or automatic sizing instruction. No fixed custom pagefile size is recommended. The explainer discusses workload peaks, system-managed sizing, disk headroom and crash-dump considerations. Data is local and not uploaded or automatically saved.

## Not implemented

Pagefile/power/startup/service settings changes, automatic tuning, unused-app detection, app removal within Hanki, before/after baselines and sustained monitoring. Shield quarantine remains disabled. Existing v0.3 personal-file Recycle Bin cleanup remains available with its previous restrictions; read START-HERE-v0.3.md before testing it.

## Validation and Windows acceptance checks

The Windows-targeted Release project cross-compiled with zero warnings/errors using .NET SDK 8.0.408 and Desktop reference pack 8.0.15. The expanded automated checks exercise pure parsing/guidance and earlier inventory/recycle-guard logic. They do not validate live Windows registry/counter reads, UAC, Settings launches or UI layout.

Run non-destructive checks:

    dotnet run --project tests/HankiTools.Checks.csproj --configuration Release

On a Windows test machine:

1. Build and launch as a normal user. Confirm no collection until Refresh is clicked.
2. Compare several desktop-app rows with Windows Installed apps. Check unknown size/date, duplicate names, both registry views, filters and sorting. Confirm usage always says unknown.
3. Cancel the review/uninstall prompt; Settings must not open. Confirm it; the Installed apps page should open without automatically removing anything.
4. Compare available RAM and committed memory against Task Manager close in time. Refresh during changing workloads; timestamps must update. Do not equate commit to pagefile usage.
5. Compare active pagefile sizes and configuration with Windows tools. Test disabled/system-managed/custom configurations only in a disposable VM, including pending-restart differences. No Hanki pagefile-change controls should exist.
6. Compare free drive space with Explorer. Confirm process exits/access denials are tolerated without claiming a complete process inventory.
7. Attempt close during app/performance refresh; cancellation should complete before closing on a second attempt.
8. Check 100%, 150% and 200% display scaling, minimum window size, scrolling and keyboard focus.

## Technical references

- https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/how-to-determine-the-appropriate-page-file-size-for-64-bit-versions-of-windows
- https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-performance_information
- https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-enum_page_file_information

Microsoft's pagefile guidance was checked during this implementation. It supports workload/dump-dependent sizing, not a universal RAM multiplier.
