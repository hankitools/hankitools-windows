# HANKI-507 — diagnostic privacy and telemetry boundary

Default full scan, reports and history are local. There is no new telemetry
endpoint, automatic report upload, account requirement or machine fingerprint.
Existing optional Assistant behavior remains separately reviewed by its user.

| Data | Local collection | Saved scan history | Optional external use |
| --- | --- | --- | --- |
| Usernames, machine names, paths | May occur in native evidence | Raw evidence omitted | Excluded from minimized payload |
| Installed software/startup commands | Existing individual tools may inspect | Not copied into new scan history | Excluded |
| IP/MAC/DNS/proxy/network identifiers | Network evidence may contain them | Evidence omitted, finding IDs hashed | Excluded; explicit probes still expose source IP to target/resolver |
| Device/hardware IDs | Needed to identify problem device locally | Stable finding ID hash, no raw ID | Excluded |
| Event text | Existing timeline tools retain bounded text | New history omits raw text | Excluded |
| Security state | Local findings explain measured state | Category/severity/outcome retained | Only reviewed minimized summaries, never exclusions/raw provider data |
| Authentication/API secrets | Not diagnostic inputs | Never stored in logs/history | Only dedicated authentication transport to an approved provider |
| Repair attempts | Action ID, approval outcome, protection, verification | Separate local journal, no command output | Reviewed summary only |

New scan history uses schema version 1, at most 30 scans and 90 days retained on
next successful save. Repair audit retains up to 1,000 attempts without automatic
deletion; reaching that cap blocks further journaled repairs. Pending attempts
must not be silently removed or interpreted as successful. Existing undo journals
and startup backups are not migrated or deleted. Corruption is surfaced and the
original file is preserved. UI offers explicit scan-history clearing.

`DiagnosticPrivacy.Minimize` drops evidence/metadata, replaces arbitrary finding
IDs with stable hashes, and replaces result explanations with safe history text.
Hashes are local comparison keys, not anonymous identifiers suitable for upload.
They must not become device tracking IDs. `Redact` is only a best-effort display
helper; it is not permission to send arbitrary text. New cloud/AI payloads must
use an allowlist and omit raw fields before any human review.

No access/refresh tokens, Authorization headers, passwords, provider keys or
credential objects may be logged. Error summaries must not include response
bodies or exception text that can carry secrets. Providers must return typed
failures; locally stored session credentials belong in Windows Credential
Manager, not diagnostic JSON. A malicious process under the same user can still
access that user's credentials; secure storage does not protect a compromised OS.

Future telemetry must have a separate opt-in decision and must not silently
reuse AI/report consent. Default is off. Consent must bind the exact payload,
destination and operation, expire on edits, and allow the user to decline without
losing local functionality. Revoking a share must invalidate server access;
client-side hiding alone is insufficient. No source machine name or stable
hardware identifier is required for cloud reporting.
