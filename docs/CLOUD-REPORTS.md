# HANKI-603 — optional cloud-report architecture and API proposal

Status: proposal; no production backend, domain, credentials or upload UI is
configured. HANKI-604 cannot be deployed or accepted end-to-end until a backend
choice and this privacy contract are approved. Local reports remain independent.
`ReviewedReportSharing` supplies a testable provider-neutral client boundary;
it does not pretend a production sharing service exists.

## Minimal payload and consent

`ReviewedContext.Prepare` emits schema version and at most 200 category/outcome/
severity tuples. It does not include timestamps, raw evidence, free-form strings,
finding IDs, machine IDs, user/customer names, device IDs, paths, network data,
software lists, credentials or diagnostic metadata. A future richer report needs
its own schema/privacy review; do not silently expand this payload. Category and
severity are still potentially sensitive and require explicit opt-in.

Before upload show the exact payload, HTTPS destination, retention period and
that anyone possessing the resulting share link may read the report. The approval
binds SHA-256 of those exact bytes and the destination, expires after ten minutes,
and is invalidated by edits. Declining sends nothing. No automatic retry, upload,
account startup requirement or background sharing. A failed upload leaves local
scan/report state unchanged. Do not log payloads or share/revocation tokens.

## Proposed backend contract

- `POST /v1/reports`: authenticated owner session, versioned minimized payload,
  requested expiry (maximum seven days), idempotency key. Validate allowlisted
  schema and bounds. Return random bearer share URL, actual expiry and separate
  owner-only revocation handle. Never put account tokens in the URL.
- `GET /r/{random-token}`: read-only minimized report. Require an unguessable
  256-bit random token; store only token hashes. No indexing (`noindex` and
  `X-Robots-Tag`), third-party scripts, tracking pixels or cross-origin data access.
  Set `Referrer-Policy: no-referrer`, CSP, TLS and `Cache-Control: no-store`.
- `DELETE /v1/reports/{owner-handle}`: require owner authorization; atomically
  invalidate reads, revoke tokens and schedule prompt deletion. Repeated deletion
  is idempotent. Expiry must be enforced on every GET, not only a cleanup job.
- Errors: typed invalid-schema, unauthenticated, unauthorized, rate-limited,
  unavailable, expired/revoked. Do not return diagnostic payloads in errors.

Use short-lived authenticated owner sessions from HANKI-509, independently enforce
entitlements server-side, cap report size and apply rate/abuse controls. Keep
necessary access metadata short-lived; exclude full URLs/token-bearing paths
from access logs. Backups must obey a documented deletion window. Record deletion
limitations in the privacy notice before launch.

## Threat model and acceptance still required

Risks include link forwarding, brute force, IDOR, token leakage via referrers/logs,
XSS from report rendering, malicious payload fields, replay/duplicate uploads,
revocation races, leaked backups and compromised client devices. Payloads are
rendered as text with no HTML/script interpretation. Authorize report ownership
on each mutation; possession of a read link never permits edits or revocation.

A real backend must demonstrate expiry/revocation, owner isolation, logging
redaction, offline behavior, consent cancellation and deletion. Fake-provider
client tests do not satisfy these server acceptance requirements. No production
hosting purchase or deployment is made by this implementation.
