# v0.7 verification

Completed: .NET 8 Release cross-build with zero errors and zero warnings; all 63 automated checks pass.

Automated checks exercise startup backup/undo through an in-memory backend and temporary JSON files: stale previews, conflicts, registry failure, interruption before/after removal, failed backup and corrupt history. Additional checks cover usage-evidence thresholds, pagefile uncertainty, AI payload boundaries, text parsing, auth failure/no retry and cancellation. API tests use a fake key and local mock HTTP handler only. They neither contact OpenAI nor modify the Windows registry.

Required Windows acceptance checks:

1. Build/launch the v0.7 executable; inspect every tab, disabled controls and review dialogs for readable colors/layout. Try a second instance and confirm it does not open a competing writer.
2. In a disposable Windows user profile, create a harmless test Run entry. Refresh, inspect the exact review, disable, restart Hanki, restore and compare its original value/type. Do not sign out with an untrusted command. Test cancellation of both confirmation dialogs.
3. Disable that test entry, recreate it with a different value externally, then try undo: Hanki must refuse overwrite. Test missing/read-only/corrupt journal files without deleting genuine backups. Pending entries must remain visible/recoverable. Startup Windows approval flags should remain unchanged.
4. Map a known app executable, start observation, run/stop that app and check sightings. Stop observation and close/reopen Hanki: tracking must remain off and mappings persist. Test forgetting a mapping, sleep/resume, process access failures and save failures. An unmapped app must never become an unused-app verdict.
5. Compare pagefile advisor values with the existing snapshot. Opening settings must not change configuration; changes made manually there must not be represented as undoable by Hanki.
6. With your own API test credentials/model, review a non-sensitive request first. Validate a successful answer and follow-up, wrong key/model, quota errors, cancellation and timeout. Confirm every send shows all transmitted turns; nothing sends merely by loading a report or opening the tab. Confirm clear removes key/chat locally, redirects are refused, and responses cannot execute commands.

Linux cross-compilation and pure/mock tests do not validate the Windows registry backend, live API contract or native UI appearance. Existing native file-recycling and collector acceptance checks remain applicable.
