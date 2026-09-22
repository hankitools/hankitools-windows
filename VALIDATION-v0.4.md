# v0.4 validation

Windows-targeted Release cross-build: **passed, 0 warnings, 0 errors**.

Non-destructive console checks: **23 passed** (11 existing plus 12 new parsing/guidance checks).

New checks cover valid/invalid/missing install dates, estimated-size conversion and unknown handling, unsigned DWORD interpretation, system-managed/custom/unknown pagefile entries, unknown commit limits and the 90% guidance boundary.

Environment: Linux, .NET SDK 8.0.408, Windows Desktop reference pack 8.0.15. No Windows graphical session was available. Windows APIs were compiled but not invoked here. Native app inventory, memory/pagefile readings, Settings activation, display scaling, UAC and Shell recycling remain manual acceptance items. See START-HERE-v0.4.md; previous recycle safety limitations still apply.
