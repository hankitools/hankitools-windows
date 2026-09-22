# v0.9 acceptance and limitations

Completed locally: .NET 8 Release cross-build, zero warnings/errors; 90 automated assertions pass. New tests cover duplicate matching and changed-file refusal, bytewise equality, journal-before-change and interrupted writes, external-change refusal, exact restore, malformed/truncated dumps and unknown/disabled Defender flags. Native Windows operations are not exercised by these tests.

## Windows acceptance

1. Rebuild and inspect all new tabs at common display scales. Sidebar Recovery must be accessible by scrolling on a short display. Existing functions and review gates must still work.
2. Duplicate tests: create two disposable copies and one same-sized different file in a personal folder. Confirm only matching contents group. No deletion is preselected; recycle one copy and restore via Recycle Bin. Modify one file after scan and confirm refusal. Test hard links, inaccessible paths, cloud placeholders and cancellation without permanent-delete fallback.
3. Review filters: inspect age/type/size subsets and verify nothing is selected automatically. A timestamp is not treated as usage evidence.
4. Startup: use harmless test values in each applicable Run scope, preserve type/command exactly, check permission failures and conflict refusal. Test a disposable startup-folder shortcut with disable/Recovery restore; do not delete real backups. Scheduled tasks/services must remain untouched.
5. Power/DNS: use a disposable VM or test adapter. Verify previews and recorded originals, applied state, restart persistence and undo. Modify the setting externally before undo and confirm refusal. Test unavailable plans, disconnected adapters, administrator denial, static DNS and automatic DNS; independently verify IPv6 and unrelated adapters are unchanged.
6. Performance: compare with Task Manager/Performance Monitor allowing sampling differences. Test 30s/1m first, then longer runs; save/load baseline and same-workload comparison. Check unavailable GPU counters, multiple GPUs, cancellation and device-query timeout. Missing values must not become zero.
7. Network: validate trace hop limits and blocked ICMP handling, DNS answer differences/timeouts and rejected input with paths/ports. Review speed-test data disclosure, cancellation, blocked endpoints and metered-network implications. It must not run on app launch.
8. Dumps: use known benign user/kernel dumps and corresponding Microsoft CDB/KD installations. Confirm refusal of unsigned/wrong debugger names, missing symbols, missing tool, malformed input and timeout. Symbol downloads must require the checkbox. Compare code/stack output against manually running the trusted debugger; do not infer culprit from a module name alone.
9. Defender: verify UAC cancellation and policy/passive-mode behavior; quick scan first, then full scan only if appropriate. Verify start/end timestamps in Windows Security, cancellation scope and definition update status. Enable/disable polling, inspect footer alerts, exit/reopen and confirm polling is off. Never disable protections just to test alerts; use fixture tests for disabled states.
10. Recovery: keep originals, exercise pending state and external conflicts. Close during asynchronous commands; verify no unexpected changes/retries. Undo does not cover Defender remediation, manual Windows configuration or file recycling.

## Primary references

- Microsoft Defender command tool: https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus
- DNS setting semantics: https://learn.microsoft.com/en-us/powershell/module/dnsclient/set-dnsclientserveraddress
- Debugger switches: https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/kd-command-line-options
- Minidump structures: https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/ns-minidumpapiset-minidump_header
- Cloudflare measurement endpoints: https://github.com/cloudflare/speedtest

Scope is deliberately explicit: startup coverage now includes Run entries and Startup files, not every autostart mechanism; tuning currently covers power plans; reversible network repair covers IPv4 DNS; dump analysis depends on separately installed Microsoft tools for symbolized stacks. No automatic root-cause guarantee, service optimizer, protection disabling, blind registry cleaner or permanent cleanup is provided.
