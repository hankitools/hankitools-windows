# Hanki Tools v0.7

Extract the source, close any running Hanki instance, and run BUILD-WINDOWS.cmd on Windows with the .NET 8 SDK. Launch HankiTools.exe in the new timestamped dist directory; the title shows v0.7. One instance per local data folder can run at a time.

## Maintain: Startup / undo

Click Refresh. Hanki lists string values in the current user's Software\Microsoft\Windows\CurrentVersion\Run key. This is a limited inventory: machine-wide entries, Startup folders, services, scheduled tasks and packaged startup tasks are not included. A registration can also be disabled by Windows Startup settings; Hanki does not claim listed entries are enabled there. The Windows shortcut opens its broader startup settings.

Review / disable removes only the selected current-user Run value, after saving its exact unexpanded command and registry type in a local journal. The running app is not stopped. Review the actual command before confirming; Hanki does not infer publisher trust or startup performance impact.

Action history / undo restores a saved registration if no conflicting value exists. It does not directly launch the command or alter Windows Startup approval flags. The app may run on a later sign-in. Pending disable entries preserve recovery information after interruption; if the original value is already present, undo reconciles the journal. A newer/different current value blocks undo. Refresh and review instead of overwriting it.

The journal is written before changes and atomic file replacement protects complete JSON writes. Registry and filesystem changes are not one OS transaction, and unrelated apps can still race registry changes. Hanki rechecks state and preserves pending records on failure; Windows validation remains required. Undo covers these startup actions only, not manual Windows settings, uninstalls or file recycling.

## Maintain: Usage review

Refresh Apps & storage, select an app and choose Map selected app for usage review. Select its main executable yourself: Hanki does not guess mappings from installer commands, and never launches that file. Start local observation in Usage review. Observation is off at every app launch and runs only while Hanki is open and enabled, sampling process paths about every 30 seconds.

Recorded data: mapped app name/path, mapping date, last observed running time and observation seconds since the last sighting. Sleep/stall intervals longer than 45 seconds do not count as coverage. Each sighting resets the coverage counter. Protected processes, short-lived launches, different executables and activity while Hanki is closed can be missed. A running process is not proof of active use.

A review candidate requires at least 30 calendar days since mapping or last sighting, plus 20 observed hours since that point. These are conservative product heuristics, not proof of non-use or a recommendation to uninstall. Others remain unknown or show their last sighting. Review the app's role before opening Windows Installed apps. Forget selected mapping removes its local history.

## Performance: Pagefile advisor

Take a snapshot to see current commit headroom, pressure guidance and whether peak commit since boot exceeded RAM. The advisor explains why one sample cannot establish an exact custom size. System-managed sizing is the starting point unless a workload/administrator requires a specific configuration; crash-dump readiness remains unverified.

Windows pagefile settings opens Performance Options. Choose Advanced → Virtual memory → Change for manual review. Changes made there are outside Hanki's undo history and may require restart. Hanki does not apply pagefile sizing or certify an optimal size. Existing 30-second sampling/comparison remains available.

## Assistant: In-app AI (optional)

Prepare/redact a report locally, check the review box and choose Use reviewed prompt in in-app AI. Alternatively type into the AI draft box. Supply your own OpenAI API key in the masked key field and a Responses-compatible model ID available to your API project. No default model or key is bundled. ChatGPT subscriptions are separate from API credentials/billing.

Each send presents the exact JSON request, including instructions and all prior successful turns. Check the review box and Send to transmit it to the fixed HTTPS endpoint https://api.openai.com/v1/responses. Redirects and automatic retries are disabled. The request uses store=false and a 2,000-output-token limit, but that does not guarantee zero provider retention or a cost limit. Cancellation/timeout may not stop server processing or billing.

Keys and chat are kept in app memory, not written to Hanki's data files; clear chat/key or close the app to release them. This is not a secure-memory erasure guarantee. Reports remain sensitive even after pattern masking. Do not place keys or other secrets in report text. AI responses are plain text; Hanki provides no command-execution tools. Check suggestions before manually acting on them.

Live API compatibility, model access, billing and end-to-end service behavior have NOT been tested here. Official documentation retrieval was unavailable during this build. Request construction, parsing, error handling and cancellation were checked using local mock HTTP responses. HTTP errors retain the draft and do not retry.

## Local data and validation

Startup backups are in %LOCALAPPDATA%\IgezziGuard\startup-actions.json. Preserve them while any startup entries are disabled. Observation history is in app-observations.json in the same directory. Both contain app names/paths; startup commands may themselves contain sensitive arguments. Normal user-profile access controls apply; these files are not encrypted.

The user confirmed v0.6 builds/launches on Windows and supplied a working performance sample. v0.7 adds native registry mutations and new UI requiring Windows acceptance tests; see VALIDATION-v0.7.md. No executable signing, installer, automatic updates, general repair engine or automatic tuning is included.
