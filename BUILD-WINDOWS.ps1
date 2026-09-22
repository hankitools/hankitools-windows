[CmdletBinding()]
param(
    [string]$CertificateThumbprint,
    [uri]$TimestampServer
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'Build this portable Windows package on Windows.' }
$projectRoot = $PSScriptRoot
. (Join-Path $PSScriptRoot 'build\Packaging.ps1')
$projectFile = Join-Path $projectRoot 'src\IgezziGuard\IgezziGuard.csproj'
$checks = Join-Path $projectRoot 'tests\HankiTools.Checks.csproj'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the current .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0 and retry.' }
$sdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or [int]($sdk.Split('.')[0]) -lt 8) { throw 'A .NET SDK version 8 or later is required.' }
if ($CertificateThumbprint -and -not $TimestampServer) { throw 'Signing requires -TimestampServer from your signing provider.' }
if ($TimestampServer -and $TimestampServer.Scheme -notin @('http','https')) { throw 'TimestampServer must be an HTTP(S) RFC3161 endpoint.' }

# Self-contained packages include their runtime: resolve the current supported .NET 8 patch.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$metadata = Invoke-RestMethod 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/8.0/releases.json' -TimeoutSec 30
$runtime = [string]$metadata.'latest-runtime'
if ($runtime -notmatch '^8\.0\.\d+$') { throw 'Could not resolve a stable .NET 8 runtime patch.' }
[xml]$project = Get-Content -LiteralPath $projectFile -Raw
$version = [string]$project.Project.PropertyGroup.Version
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$publishDirectory = Join-Path $projectRoot "dist\HankiTools-$version-win-x64-$stamp"
if (Test-Path -LiteralPath $publishDirectory) { throw 'Output already exists; nothing was deleted.' }
New-Item -ItemType Directory -Path $publishDirectory | Out-Null

Write-Host "Checking Hanki $version using SDK $sdk..." -ForegroundColor Cyan
& dotnet run --project $checks --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Automated checks failed. No package created.' }
& dotnet publish $projectFile --configuration Release --runtime win-x64 --self-contained true --output $publishDirectory `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false `
    "-p:RuntimeFrameworkVersion=$runtime" -p:TargetLatestRuntimePatch=true -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) { throw 'Publish failed. No package created.' }
$exe = Join-Path $publishDirectory 'HankiTools.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Published executable is missing.' }
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'Data\signatures.txt'))) { throw 'Signature data was not copied.' }
foreach ($document in @('LICENSE','README-PORTABLE.md','PRIVACY.md','RELEASE-NOTES.md','RELEASE-CHECKLIST.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $publishDirectory
}
if ($CertificateThumbprint) {
    if ($CertificateThumbprint -notmatch '^[a-fA-F0-9]{40}$') { throw 'Expected a 40-character certificate thumbprint.' }
    $signtool = Get-Command signtool.exe -ErrorAction Stop
    & $signtool.Source sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampServer.AbsoluteUri /td SHA256 $exe
    if ($LASTEXITCODE -ne 0) { throw 'Signing failed.' }
    & $signtool.Source verify /pa /all /v $exe
    if ($LASTEXITCODE -ne 0) { throw 'Signature verification failed.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $exe
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate) { throw 'Valid timestamped signature required.' }
}
$smoke = Join-Path $publishDirectory 'ui-smoke.json'
$process = Start-Process -FilePath $exe -ArgumentList @('--ui-smoke-test', ('"' + $smoke + '"')) -PassThru
if (-not $process.WaitForExit(90000)) { $process.Kill(); throw 'UI smoke check exceeded 90 seconds.' }
$smokeExit = $process.ExitCode
$process.Dispose()
if ($smokeExit -ne 0 -or -not (Test-Path -LiteralPath $smoke)) { throw 'UI smoke check failed.' }
$smokeResult = Get-Content -LiteralPath $smoke -Raw | ConvertFrom-Json
if ($smokeResult.Passed -ne $true -or $smokeResult.Version -ne $version) { throw 'UI smoke result did not pass for this version.' }
$payloadPaths = @('HankiTools.exe','Data\signatures.txt','LICENSE','README-PORTABLE.md','PRIVACY.md','RELEASE-NOTES.md')
$payload = @($payloadPaths | ForEach-Object { @{ Path=$_; SHA256=(Get-FileHash -LiteralPath (Join-Path $publishDirectory $_) -Algorithm SHA256).Hash } })
$exeHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
@{ Version=$version; Runtime=$runtime; Sdk=$sdk; BuiltAt=(Get-Date).ToUniversalTime().ToString('o'); ExeSHA256=$exeHash;
    Signed=[bool]$CertificateThumbprint; UiSmokePassed=$true; PublicReleaseApproved=$false; Payload=$payload } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $publishDirectory 'build-info.json') -Encoding UTF8
$required = @('clean-install-launch','dpi-keyboard-contrast','all-module-navigation','readonly-diagnostics','scan-cancel-history','cleanup-recycle-restore','startup-and-undo','power-dns-and-undo','defender-controls','monitor-save-load','ai-consent-cancel','upgrade-data-retention')
@{ ExeSHA256=$exeHash; Tester=''; TestedAt=''; WindowsVersion=''; Checks=@($required | ForEach-Object { @{Id=$_; Passed=$false; Notes=''} }) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $publishDirectory 'acceptance.json') -Encoding UTF8
$zip = "$publishDirectory-candidate.zip"
New-HankiArchive -SourceDirectory $publishDirectory -DestinationZip $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding ASCII
Write-Host "Candidate built: $zip" -ForegroundColor Green
Write-Host 'Complete the Windows checklist on this exact executable. PACKAGE-RELEASE.ps1 checks signature, runtime and recorded acceptance before final packaging.'
