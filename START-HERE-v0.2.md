# Hanki Tools v0.2 preview

This document supersedes the v0.1 README. The old README and legacy source are retained for reference; their quarantine feature descriptions do not apply to v0.2.

## Build and run

On Windows with .NET SDK 8 or later, run BUILD-WINDOWS.cmd. The timestamped dist folder contains HankiTools.exe and its Data directory. Keep these together. The build downloads required Microsoft runtime/build components and publishes a self-contained Windows x64 app. No administrator access is requested.

## Available

- Home: honest module status; no overall protection score.
- Shield: experimental read-only file/folder scanner, one EICAR test hash and basic heuristics; scan summaries persisted locally.
- Connect: local adapter inventory, IPv4/IPv6 addresses, gateways, DNS servers; bounded DNS resolution and TCP reachability tests.
- Cancel: interrupts checks. Cancelled scans are incomplete and not persisted as completed scans.

QuarantineService.cs and the legacy MainForm.cs are explicitly excluded from compilation. No quarantine, restore or deletion is offered. Do not delete legacy quarantine data or its key; recovery hardening is deferred. Local scan history remains in %LOCALAPPDATA%\IgezziGuard for compatibility.

Connect runs only when clicked. It queries example.com through the system resolver and connects to example.com:443 and 1.1.1.1:443. The resolver and remote endpoints can see ordinary request/connection metadata. Reports are not uploaded. It does not change settings, repair adapters, test bandwidth, or measure packet loss. VPN/proxy/firewall conditions may affect findings.

Diagnose, Maintain and Toolkit are planned, not implemented. Keep Defender enabled. Never use this experimental scanner as proof that a file or computer is safe.

## Validation status

Source/configuration checks and ZIP integrity verified in the Linux authoring environment. No .NET compiler is installed there: Windows compilation, runtime behavior and visual layout have NOT been verified. This is a source preview, not a tested binary release.

## Windows acceptance checks

1. Build with BUILD-WINDOWS.cmd; confirm HankiTools.exe launches as a normal user.
2. Confirm Home clearly marks experimental and planned modules; no protection-ready claim.
3. Scan a folder of disposable harmless text files. Confirm completed counts and scan history.
4. Scan a harmless text file named invoice.pdf.exe (do not execute it): heuristic double-extension finding expected. Verify its bytes and path remain unchanged.
5. Cancel a large scan and close during scanning: completion must not be reported as clean; close again after cancellation finishes.
6. Run Connect online and offline. Confirm errors are shown as inconclusive rather than definitive diagnoses. Confirm adapters/DNS settings remain unchanged.
7. Confirm no quarantine/restore/delete buttons; preserve any old quarantine files unchanged.

Before any production release: compile and run these tests on Windows, add automated regression tests, validate accessibility/DPI layout, strengthen detection coverage and complete brand clearance.
