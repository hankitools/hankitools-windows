# Hanki Tools

A Windows toolkit for understanding your PC, reviewing maintenance and investigating problems. Hanki diagnoses common Windows problems, explains results in plain language and offers reviewed, reversible fixes. It is not a vulnerability scanner.

**Status: 0.17 release candidate.** GitHub Actions builds every commit on Windows, runs the automated checks and a UI smoke test. Pre-release testing on Windows 11 is recorded in [VALIDATION-v0.17.md](VALIDATION-v0.17.md). Publisher signing and final acceptance are still required before a verified release; see [RELEASE-STATUS.md](RELEASE-STATUS.md).

## Download

Builds are published on [GitHub Releases](https://github.com/hankitools/hankitools-windows/releases). Until code signing is in place, releases are **unsigned previews** and are labelled that way: Windows SmartScreen will warn before the first run. Each release lists a SHA-256 checksum; compare it before running. Hanki is a portable app: extract the ZIP and run `HankiTools.exe`.

## Code signing policy

Windows releases are intended to be signed through the [SignPath Foundation](https://signpath.org) program for open-source projects; the application is pending. Once approved, this section will read: *Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).*

Team roles:

- Committers and reviewers: [@hankitools](https://github.com/hankitools)
- Approvers: [@hankitools](https://github.com/hankitools)

Only release builds produced by this repository's GitHub Actions workflow from the public source are submitted for signing, and an approver reviews every signing request. Local or modified builds are never signed.

Privacy policy: this program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. Each optional network feature (connection checks, speed test, DNS comparison, Defender definition updates and the optional OpenAI chat) and the third parties it contacts are described in [PRIVACY.md](PRIVACY.md). Hanki shows a review before any change to system configuration and records supported changes for undo.

## Build and try

On Windows, install the current .NET 8 SDK, close any running Hanki instance, extract the source ZIP and run `BUILD-WINDOWS.cmd`. Internet is required for Microsoft runtime metadata and package restore. The script runs the logic checks, publishes a self-contained Windows x64 candidate and runs a structural UI smoke check. Launch `HankiTools.exe` from the new timestamped `dist` folder. End users of the portable package do not need the SDK.

For a signing-enabled candidate, run `BUILD-WINDOWS.ps1 -CertificateThumbprint YOUR_CERTIFICATE_THUMBPRINT -TimestampServer YOUR_PROVIDER_RFC3161_URL` from a Windows SDK shell with SignTool available. Private signing material stays in your certificate store; it is not included in source or arguments.

After testing that exact signed executable and recording evidence in its generated `acceptance.json`, run `PACKAGE-RELEASE.ps1 -CandidateDirectory PATH_TO_CANDIDATE`. This verifies the executable hash, signature, current runtime patch, smoke result and recorded acceptance before producing a release ZIP and checksum. It does not replace human testing. Details: [RELEASE-CHECKLIST.md](RELEASE-CHECKLIST.md).

To publish on GitHub Releases, install the GitHub CLI (`winget install --id GitHub.cli`), sign in once with `gh auth login`, commit and push, then run `PUBLISH-RELEASE.ps1 -CandidateDirectory PATH_TO_CANDIDATE`. It re-verifies the packaged ZIP, checksum list and signature, requires a clean pushed commit matching the project version, and creates a **draft** release tagged `vVERSION` (marked pre-release for `-rc` versions) with notes from RELEASE-NOTES.md, then checks the uploaded ZIP against the local one. Review and publish the draft on GitHub, or pass `-Publish`. Use `-DryRun` to run every check without creating anything.

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
