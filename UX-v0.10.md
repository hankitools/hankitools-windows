# Hanki Tools v0.10 • Easier navigation and clearer results

Build with BUILD-WINDOWS.cmd, close the older instance and open the new timestamped dist/HankiTools.exe. The title should show v0.10.

## Changes

- Find a tool in the header, or press Ctrl+K anywhere. Search module and subpage names; use Up/Down, Enter and Escape. Opening a result navigates to the tool without running it.
- Dashboard cards resize into one, two or three columns based on available width.
- Event Log, Wi-Fi/latency and Defender audit checks show an activity strip and an indeterminate progress indicator. Completed reports enable review/export and assistant actions.
- Shield opens Defender audit by default. The experimental scanner is still available in its own tab.
- Defender reports lead with a plain-language summary, followed by raw evidence. Disable-prefixed preferences are translated correctly. Missing values and restricted exclusions stay unknown. Legacy PowerShell dates become local readable dates.
- Explicit pine backgrounds and high-contrast system colors remain in use.

## Validation

Release cross-build: no warnings or errors. All 98 automated checks pass, including eight audit interpretation checks. Linux cannot run Windows Forms or Defender; native appearance and behavior have not been verified here.

On Windows, check:

1. Resize the window and test 100%, 150% and 200% display scaling. Dashboard labels and buttons should remain readable; scroll when needed.
2. Open Ctrl+K, search “DNS”, open the advanced network tab, then try “pagefile” and “duplicates”. Try a query with no matches, keyboard navigation and Escape. Opening a result must not run a task.
3. Run Defender audit without admin access. Restricted exclusions should say administrator access is required, not that no exclusions exist. Confirm its readable summary agrees with the raw evidence.
4. Run and cancel a diagnostic check. The progress indicator must stop; export and assistant actions should only enable after collection completes.
5. Check Windows high-contrast mode and keyboard focus visibility.
