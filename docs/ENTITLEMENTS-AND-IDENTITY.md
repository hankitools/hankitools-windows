# HANKI-501–506 / HANKI-509 — capabilities and identity

Community remains anonymous. Existing tools, Full System Scan, raw findings,
manual guidance and local diagnostic history are always available. History is
left in Community in this candidate; it is not necessary to restrict it to prove
the additive automation boundary. Existing standalone setting changes and undo
remain where they are and are never wrapped in paid checks.

Automatic repair and scheduling are additive Pro capabilities. Technician
sessions/customer exports require Technician capabilities. Checks use
`IEntitlements`, including the repair service boundary, rather than UI-only
`IsPro` flags. Release composition currently returns Community: no production
licensing or identity provider is configured. Upgrade copy appears only when the
user asks for automation; no startup prompt, checkout or login form exists.

Debug builds alone accept `HANKI_DEVELOPMENT_EDITION=Community|Pro|Technician`.
Release removes the lookup at compile time; Release tests assert the variable
cannot unlock automation. Open-source compilation can of course modify local
code; this is not DRM or a claim of tamper-resistant client enforcement.

`ILicenseProvider` is the trust boundary. A future provider must verify issuer,
audience, device binding, signature, expiry and revocation before returning
`ValidatedLicense`. Do not deserialize mutable JSON directly into trusted claims.
There is deliberately no production license cache until verification keys and
provider protocol exist. Offline grace is bounded by provider-validated claims;
expired/revoked/unavailable states retain Community. Offline caches must be
signed, encrypted where appropriate, and protected against rollback/replay and
clock rollback. Server services must independently check entitlements.

`IIdentityProvider` defines browser-based authentication, refresh and revocation.
A provider must implement PKCE/state/nonce/callback validation and TLS. No embedded
password form or real provider integration is added. `IdentitySession` starts
anonymous; expired/revoked results clear usable local credentials. Logout clears
local credentials before attempting remote revocation. A remote revocation
failure must be reported; server tokens rely on expiry/revocation enforcement.
`WindowsSessionSecretStore` uses per-user Windows Credential Manager. Tests use a
fake secret store and never contact an identity service. Authentication and
payment are separate; device/organization/seat fields are optional context, not
proof of entitlement. No tokens are included in diagnostic evidence.
