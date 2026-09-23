# HANKI-605 / HANKI-606 — optional explanation boundary

`IExplanationProvider` is provider-neutral; no provider key or production endpoint
is embedded or selected. `OptionalExplanations` accepts only the minimized
structured payload from `ReviewedContext`, a current exact-payload/destination
approval, and an explicit enabled flag. Decline/disabled/edited/expired approval
makes no provider call. Requests have a 30-second bound and no automatic retry;
errors, rate limits and invalid responses return deterministic local guidance.
There is no cost-bearing provider integration in this candidate.

Responses are bounded explanatory strings, never executable commands, repair
identifiers, privileges, entitlements or policy. Even if text recommends a repair,
only local deterministic rules and the independent approval/safety workflow can
make that action eligible. Logs contain no prompts, payloads, keys or response
bodies. Turning this path off has no effect on local diagnostics.

Minimization uses a field allowlist rather than trying to perfectly redact
arbitrary logs: only enum category/outcome/severity tuples leave this boundary.
The existing optional Assistant remains a separate user-reviewed workflow.
A production provider/consent UI still needs integration and Windows acceptance;
these tickets add the reusable foundation, not an undisclosed upload path.
