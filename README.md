# Hanki Tools • 0.16.0-rc.3

A Windows toolkit for understanding your PC, reviewing maintenance and investigating problems. Blue-accented charcoal workspaces, icon-led cards, clear module introductions and a searchable tool launcher keep the main workflows accessible.

**Status: release candidate, not yet approved for public distribution.** The source cross-builds and automated checks run on Linux. Native Windows acceptance, the final self-contained package and publisher signing remain required. See [RELEASE-STATUS.md](RELEASE-STATUS.md).

## Build and try

On Windows, install the current .NET 8 SDK, close any running Hanki instance, extract the source ZIP and run `BUILD-WINDOWS.cmd`. Internet is required for Microsoft runtime metadata and package restore. The script runs the logic checks, publishes a self-contained Windows x64 candidate and runs a structural UI smoke check. Launch `HankiTools.exe` from the new timestamped `dist` folder. End users of the portable package do not need the SDK.

For a signing-enabled candidate, run `BUILD-WINDOWS.ps1 -CertificateThumbprint YOUR_CERTIFICATE_THUMBPRINT -TimestampServer YOUR_PROVIDER_RFC3161_URL` from a Windows SDK shell with SignTool available. Private signing material stays in your certificate store; it is not included in source or arguments.

After testing that exact signed executable and recording evidence in its generated `acceptance.json`, run `PACKAGE-RELEASE.ps1 -CandidateDirectory PATH_TO_CANDIDATE`. This verifies the executable hash, signature, current runtime patch, smoke result and recorded acceptance before producing a release ZIP and checksum. It does not replace human testing. Details: [RELEASE-CHECKLIST.md](RELEASE-CHECKLIST.md).

## Recover from an earlier ZIP failure

If RC2 compiled and passed its smoke check but ZIP creation failed, use this package's helper without rebuilding the older candidate:

```powershell
.\REPACKAGE-WINDOWS.ps1 -CandidateDirectory 'C:\path\to\existing\dist\HankiTools-0.16.0-rc.2-win-x64-TIMESTAMP'
```

It verifies the existing executable/payload hashes and smoke report before creating a uniquely named candidate ZIP. To get the new blue UI, run the normal RC3 build instead. Repackaging an RC2 binary does not change its version or layout.

## Included workspaces

| Workspace | Purpose |
| --- | --- |
| Diagnose | Crash timeline, Event Logs, local dump triage and guided checks. |
| Performance | Memory snapshots, pagefile guidance, short/long monitoring, saved comparisons and reversible power-plan changes. |
| Maintain | File inventory, duplicate finder, reviewed recycling, installed-app and startup review. |
| Connect | Adapter/Wi-Fi checks, ICMP, traceroute, DNS comparison, bounded transfers and reversible IPv4 DNS changes. |
| Shield | Defender status/scan controls and opt-in alerts; separate experimental file scanner. |
| Assistant | Local redaction/report preparation and optional reviewed OpenAI API chat. |
| Recovery | Undo supported power-plan, IPv4 DNS and startup-folder changes. Registry startup undo is in Maintain. |

The experimental file scanner has a bundled EICAR test signature and simple heuristics, not a maintained malware feed. Hanki is not a replacement antivirus. AI never executes commands. A passing check, quiet Event Log or empty detection list does not prove a machine is healthy.

## Release documents

- [Portable user guide](README-PORTABLE.md)
- [Release notes](RELEASE-NOTES.md)
- [Privacy and data behavior](PRIVACY.md)
- [Release status and validation evidence](RELEASE-STATUS.md)
- [Windows acceptance checklist](RELEASE-CHECKLIST.md)
- [Security reporting and release operations](SECURITY.md)

The .NET 8 target reaches end of support on 10 November 2026. Build against the current servicing patch; migrate to a supported LTS before that date. The release script enforces this branch's deadline. A .NET 10 migration has not been validated here. Official reference: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core

The project namespace/data directory retains the original IgezziGuard name for compatibility. Previous UX/version notes describe history, not current release validation. MIT license; see LICENSE.

## Diagnostic roadmap candidate (0.17)

Full System Scan, Windows Activation and Diagnostic history are Community tools.
New automation is additive; Release currently stays Community until production
licensing is connected. Existing manual tools and recovery remain available.

Implementation and acceptance details: [roadmap ledger](docs/ROADMAP-IMPLEMENTATION.md),
[privacy](docs/DIAGNOSTIC-PRIVACY.md), [capabilities and identity](docs/ENTITLEMENTS-AND-IDENTITY.md),
and [activation diagnostics](docs/ACTIVATION-DIAGNOSTICS.md).
