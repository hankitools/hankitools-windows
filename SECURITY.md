# Security and maintenance

The app starts asInvoker and does not run AI-generated commands. Cleanup uses Windows recycling with refusal paths and no permanent-delete fallback. Supported setting changes keep a recovery journal before mutation and refuse conflicting current state. These controls are not a formal security certification or a guarantee against all races or failures.

Do not post API keys, private crash dumps, unredacted logs or credentials in public bug reports. A public release needs a monitored security-reporting contact; the publisher has not supplied one yet. Add the actual channel before publication. Report the version, affected action, minimal synthetic reproduction and impact privately.

Self-contained releases bundle .NET, so installed system-runtime updates do not update the bundled copy. Monitor Microsoft's servicing releases, rebuild, sign and repeat acceptance as needed. This branch targets .NET 8; migrate to a supported LTS before 10 November 2026. No automatic updater is shipped.

Release signing reference: https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool
Runtime support reference: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
Single-file deployment reference: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview

Dependencies: app source currently declares no third-party NuGet PackageReference. It depends on Microsoft .NET Windows Desktop and native Windows APIs/tools. This is not proof of absence of platform vulnerabilities. Microsoft debugger integration requires an independently installed, valid Microsoft-signed CDB/KD. Windows Defender remains authoritative for its own operations.
