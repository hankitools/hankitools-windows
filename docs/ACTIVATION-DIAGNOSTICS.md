# Windows Activation — issue #47

Community Full System Scan and Diagnose → Windows Activation share
`ActivationDiagnostic`. It queries selected fields from SoftwareLicensingProduct,
SoftwareLicensingService and the Software Protection service. Numeric Windows
LicenseStatus/LicenseStatusReason drive state; no localized slmgr output is parsed.
Product-name/description channel hints have a safe Unknown fallback. No full-key
property is queried, including the firmware OEM key; partial keys are unnecessary
for these checks and are also omitted. Unexpected key-shaped strings are masked
before creating results. The query is limited to Windows application licensing,
not Office or all installed products.

The mapping separates current status, historical event codes, licensing service
availability and time context. Windows-reported edition/hardware/blocked-key/KMS/
network errors have deterministic manual guidance. Unknown codes remain visible.
A stopped trigger-start sppsvc service alone is not a failed license. No trusted
clock comparison is claimed. Nearby network errors and clock context are evidence
to review, not proof of an activation root cause.

KMS probes require an installed KMS-client channel plus explicit network consent.
They use only the Windows-configured host or SRV records from the machine's
organization DNS suffix. Names/ports are validated and query strings are quoted;
TCP is bounded to five seconds. DNS and reachability failures identify their
collection stage. TCP success does not prove protocol/activation success. No
public KMS endpoint, activation request, key replacement, edition conversion or
registry/token change is offered. Failure to discover a host directs the user to
their organization's administrator.

History omits raw evidence and hashes finding identifiers under the common privacy
policy. Optional AI/cloud payloads contain only category/outcome/severity enums,
not licensing state, key material, KMS names or activation identifiers. The app
never uploads an activation report automatically.

Reference semantics verified against Microsoft documentation:

- [SoftwareLicensingProduct properties and numeric states](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/sppwmi/softwarelicensingproduct)
- [Activation error codes](https://learn.microsoft.com/en-us/troubleshoot/windows-server/licensing-and-activation/troubleshoot-activation-error-codes)
- [Windows activation troubleshooting](https://support.microsoft.com/en-us/windows/activation/get-help-with-windows-activation-errors)

Fixture tests cover Retail/OEM/KMS, unlicensed/missing/localization fallback,
known/unknown codes, service state, full-key masking, host validation and network
consent. Real organization DNS/KMS and installed licensing-provider behavior still
require Windows acceptance. No Microsoft account sign-in is automated.
