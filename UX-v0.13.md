# Hanki v0.13 • Cleaner workspace navigation

The screenshot showed native white tab-header backgrounds and stacked Performance tabs. The shared HankiTabs control now hides the native strip and gives each page a themed navigation row. This applies to Maintain, Performance, Diagnose, Connect, Shield and Assistant. Home, Recovery and Scan history retain their existing single-page layouts.

Navigation uses shorter visible labels, dark backgrounds, a mint active marker and keyboard-focus feedback. Rows wrap when space is limited. Full workspace names remain available to accessibility and Ctrl+K search. Navigation buttons switch views without running actions.

Performance no longer has a nested “Memory & processes / Pagefile explained” tab row. Its views are Overview, Quick sample, Monitoring and Power tuning. Pagefile explained is a quiet help button that opens the existing guide in a separate dialog; Escape closes it.

Validation: release cross-build succeeds with zero warnings/errors. This is a navigation/layout change; Windows Forms cannot be executed in this Linux workspace. Prior 104 logic checks are unchanged and were not rerun for this visual change.

Windows checks:

- Visit every module and switch every view; verify active styling and no native white header bands.
- Test Ctrl+K navigation into nested views and normal Tab/Enter keyboard operation.
- Resize at 100%, 150% and 200% scaling; ensure wrapping rows leave content visible.
- Open Pagefile explained, close with Escape, and confirm the snapshot remains intact.
- Check high-contrast mode and keyboard focus visibility.

Build with BUILD-WINDOWS.cmd and launch the new timestamped dist/HankiTools.exe. Confirm v0.13 in the title.
