# Guided troubleshooting: first increment

The full scan now opens a short summary of findings that need attention. Other
completed checks and checks with missing evidence expand separately. Selecting a
finding shows its explanation, a manual next step and coverage; raw evidence
remains under Technical details. Incomplete or cancelled checks never produce a
clean-bill-of-health headline. History-save failures remain visible.

Review repairs is shown only for a recent scan with a supported proposal, an
eligible edition and administrator access. Existing repair review, consent,
precondition checks and audit behavior remain in place. Manual guidance is always
available. Starting another scan clears proposals from the previous scan.

Back and Alt+Left follow actual visits, including nested tool tabs. Returning
does not add another history entry. The history is bounded to 100 previous
locations and kept only for the current app session. Existing page instances keep
their findings and selected troubleshooting state. Landing pages no longer reset
their scrolling every time they become visible.

Connect starts with guided troubleshooting. Home's internet symptom and the
existing guided-check list link to it. The flow reuses the connection collector:

1. Show the probe destinations before the user starts.
2. Collect read-only facts and suggest one next step.
3. Let the user repeat the check, open additional help or confirm the symptom is
   resolved. Passing probes alone never mark the journey resolved.
4. Keep the result when visiting a detailed network tool, with Back returning to
   that exact journey. Link to the website knowledge base without attaching data.

No automatic repairs, DNS changes, extra network probes, telemetry or background
uploads were added. The journey is retained in memory, not persisted across app
restarts. New core controls and summary counts are translated into all twelve
existing languages. Detailed diagnostic explanations continue to use English
fallback where a translation is unavailable.

## Validation

`dotnet run --project tests/HankiTools.Checks.csproj -- --usability-checks`
checks navigation history, scan coverage and evidence-based network branching.
The full check runner includes these checks as well.

The existing UI smoke runner now exercises expanding scan results, selecting a
manual next step, contextual repair visibility, returning from a network tool to
the guided tab, retaining its state and confirming resolution. It uses fixed
fixtures; it does not run network probes or repairs. Screenshot output includes
`scan-results.png` and `internet-guide.png` with example data.

`dotnet run --project tests/HankiTools.Checks.csproj -- --localization-checks`
checks catalog parity and placeholders. The existing Windows CI visits the UI in
all twelve languages. UI smoke coverage is structural, not a substitute for
screen-reader testing or usability sessions with first-time users.

During local validation, an existing app-hang collector emitted a JSON object for
an absent module name. The collector now casts that value to a nullable string;
a synthetic Application Hang event exercises the exact PowerShell collection
path before the existing live event-log checks.

The separate Home redesign, fuzzy/error-code search, unified undo timeline,
operation-specific elevation and broader responsive layout work remain future
increments. Included in the 0.19.0-rc.4 preview.
