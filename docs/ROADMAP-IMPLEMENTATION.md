# Roadmap implementation and acceptance ledger

This branch builds on the HANKI-101 architecture audit. Existing Community tools,
including their manual setting changes and undo, remain available. No main-branch
merge or public release is implied by this ledger.

## Implementation map

| Issues | Implementation | Validation boundary |
| --- | --- | --- |
| #2–#4 / HANKI-102–104 | Validated results with independent outcome/severity; read-only module contract; explicit sequential orchestrator | Model round trips, fake-module cancellation, partial/unavailable/failure, progress and deterministic ordering |
| #5 / HANKI-105 | Community Full system scan with progress, cancellation, counts and per-finding details | Windows UI smoke; manual keyboard/DPI/visual acceptance remains |
| #6, #8–#15 / HANKI-201–209 | DISM ScanHealth, SFC verify-only, Update, capacity/storage, local/external network, devices, repeated events, Security, performance/startup | Fixture mappings, safe unknown states and PowerShell syntax parsing; native providers and locales require acceptance |
| #16–#20 / HANKI-301–305 | Deterministic normalization, priority factors, recommendation rules, explanations and result UI | Related/independent evidence, ties, no-match fallback, evidence retention |
| #21–#27 / HANKI-401–407 | Declarative actions; approval, prerequisites, pending audit, optional restore point, execution, matched diagnostic verification and final reports | Fake action/environment/restore/audit tests; real mutations are not run by automated tests |
| #28–#30 / HANKI-501–503 | Central capability checks, additive automation, contextual explanation | Community capability tests; no startup/login/checkout wall |
| #31 / HANKI-504 | Versioned minimized local scan history and separate repair audit | Round trips and corrupt-file preservation; existing undo data untouched |
| #32–#33 / HANKI-505–506 | Debug-only development edition, provider-neutral validated-license and lifecycle abstractions | Release ignores environment toggle; active/revoked/expired/grace tests; no production license provider |
| #34–#35 / HANKI-507–508 | Privacy policy, minimal payloads, hardened consent/unknown/cancellation/journal behavior | Regression fixtures plus explicit native acceptance checklist below |
| #36 / HANKI-509 | Anonymous default, provider-neutral browser-auth/session lifecycle, per-user Windows Credential Manager implementation | Fake-provider/store lifecycle tests; native credential storage and future provider integration require acceptance |
| #37 / HANKI-601 | Local history browser, comparison and linked repair outcomes | Stable IDs; missing finding is not called resolved; offline operation |
| #38 / HANKI-602 | Opt-in daily/weekly current-user Task Scheduler registration; no repair/elevation/network probes in headless checks | XML tests; real registration/removal/logon/battery/portable-path behavior requires Windows acceptance |
| #39 / HANKI-603 | Minimized cloud API/privacy/threat-model proposal | Documented in CLOUD-REPORTS.md; no production service |
| #40 / HANKI-604 | Exact-consent provider-neutral sharing client, expiry/revoke validation | **Blocked for end-to-end completion:** issue explicitly requires approved backend/privacy architecture. No backend has been selected/approved or deployed. Fake client tests do not satisfy server expiry/revocation acceptance. |
| #41–#42 / HANKI-605–606 | Optional provider-neutral explanations with exact-payload consent, allowlist minimization, timeouts and deterministic fallback | Fake provider tests; no production AI provider or hidden upload. Existing optional Assistant remains separate. |
| #43–#45 / HANKI-701–703 | Local technician session, configurable customer-report export, independent seat/workstation/customer-session domain | Reuses shared scan/repair data; entitlement/offline-grace/redaction tests. No production seat server or organization portal. |
| #47 / Windows activation | Community local licensing diagnostic, dedicated view, known error mapping and conditional organization KMS probes | Numeric/localization-safe fixtures, privacy and consent tests; real licensing/KMS acceptance required |

## Selected repair scope

DNS cache refresh is the initial low-risk action. It changes no persistent DNS
configuration and must be followed by fresh disclosed network probes. A failed
lookup alone is not a proven cache fault: the UI presents a bounded troubleshooting
attempt, and only diagnostic verification can indicate improvement.

DISM RestoreHealth and SFC scannow are also supported candidates, explicitly
classified **Moderate** risk. They require current matching corruption evidence,
administrator access, enabled servicing components, no pending restart/conflicting
servicing process, explicit approval and journal persistence. DISM network repair
sources require separate consent. A restore point is attempted; unavailable
optional protection blocks unless explicitly acknowledged. No automatic reboot,
Update-component reset, Winsock reset, driver removal, registry cleaning or broad
service remediation is added. These broader candidate repairs are intentionally
not selected where the current evidence/rollback story is insufficient.

Failed/cancelled/pending attempts require manual inspection before another same-
action attempt. There is no automatic retry or claim of rollback. Existing
Recovery journals cover their original settings; a restore point is not a full
backup or guaranteed undo of servicing. A command that succeeds but leaves the
finding unchanged is reported Unchanged, not Fixed.

## Native acceptance still required before a release

Use a disposable Windows VM/test PC, real recorded evidence and the exact built
executable. Do not mark these passed from fixture tests:

- Standard-user and administrator scans; restricted/missing CIM providers;
  non-English Windows; missing device/storage/security fields; third-party AV;
  no matching events and capped event logs.
- SFC/DISM on healthy and deliberately prepared damaged test images; permission,
  timeout and cancellation behavior; servicing processes that outlive the parent.
- Reviewed DNS/SFC/DISM repairs with approval declined, no restore point, failed
  restore creation, pending restart, conflicting servicing and verification that
  stays unknown/unchanged. Restore the test system before disposal.
- Task Scheduler creation/removal, user logon/offline/battery behavior and moved
  portable app. Paid paths require Debug development profile until licensing is
  connected; Release must remain Community.
- Credential Manager write/read/logout across app restarts on the same account;
  store failures and remote revocation failures with a test identity provider.
- Activation Retail/OEM/organization KMS, VPN/DNS conditions, legitimate activation
  errors and full-key absence in display/export/history.
- UI keyboard navigation, high contrast and 100/150/200% scaling for new pages and
  repair-review dialogs; existing tools remain reachable.

Automated checks execute fixed scripts only through the PowerShell parser, never
through their diagnostic/repair commands. New native repair actions are tested
with fake backends. Public packaging retains the repository's existing signature,
runtime and exact-executable acceptance gates.
