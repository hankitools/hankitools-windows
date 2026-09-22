# v0.3 validation record

## Executed

- Windows-targeted Release build: succeeded, 0 warnings, 0 errors.
- SDK: .NET 8.0.408; Windows Desktop reference pack: 8.0.15.
- Execution environment: Linux; no Windows GUI/session available.
- Non-destructive tests: 11 passed.

Checks: child path accepted; prefix sibling rejected; root itself excluded; recursive inventory counts and logical bytes; numeric largest-file order; complete fixture inventory; temporary application-data cleanup blocked; non-recycling callback veto; invalid shell-target veto; unsuccessful operation not reported as recycled; scan cancellation.

Commands (SDK path depends on machine):

    dotnet build src/IgezziGuard/IgezziGuard.csproj -p:EnableWindowsTargeting=true --configuration Release
    dotnet run --project tests/HankiTools.Checks.csproj --configuration Release

## Not executed

- Windows Forms rendering, display scaling, keyboard navigation.
- UAC approve/deny and Explorer launches.
- Actual Shell recycling, Recycle Bin full/disabled behavior, restore, locked files and cancellation during Shell dialogs.
- Adversarial filesystem race testing.

Do not confuse compilation and callback unit checks with end-to-end deletion safety verification. START-HERE-v0.3.md contains the required manual Windows tests. Use disposable copies in a Windows test account/VM first.
