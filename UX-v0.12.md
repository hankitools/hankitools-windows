# Hanki v0.12 • Results and contextual actions

Defender audit opens with cards for reported protection, security intelligence, exclusions/access and next steps. Performance snapshots show memory figures, existing pagefile guidance and next steps. Both start with a helpful empty state and show explicit running, cancelled and unavailable states.

“View technical details” opens the full original report; “Back to summary” returns to the cards. Report preparation/export still uses the complete report. The summary does not infer a health score or hide unknown values behind a success badge. Defender configured preferences and raw evidence remain in technical details.

In Maintain → Files & storage, selecting files reveals a contextual toolbar with selected count and logical size. Open location appears for a single selection. Cleanup preview names the selected count. Clearing the selection hides the toolbar. Existing cleanup review and filesystem safety checks still apply.

Validation: Release cross-build succeeded with zero warnings/errors; all 104 automated checks passed. Six new checks cover missing counters, missing guidance, localized values, urgent guidance, restricted exclusions and raw-evidence separation. Native Windows UI has not been run in this Linux workspace.

Windows acceptance checks:

1. Run Defender audit and a performance snapshot. Compare the cards with technical details, including denied/missing fields.
2. Switch repeatedly between summary and technical details; refresh and cancel a check. Old cards should not remain after a new collection starts.
3. Resize at 100%, 150% and 200% display scaling. Long paths and reports should wrap/scroll without hiding the details toggle.
4. Select one file, multiple files, then clear selection; verify the contextual toolbar. Apply a filter and verify stale selections disappear.
5. Check keyboard focus and Windows high-contrast mode.

Extract, close the old instance, run BUILD-WINDOWS.cmd and launch HankiTools.exe from the new timestamped dist directory. Confirm v0.12 in the title.
