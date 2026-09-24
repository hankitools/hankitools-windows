[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'build\Packaging.ps1')
if ($env:OS -ne 'Windows_NT') { throw 'Run on Windows to validate Authenticode.' }
$directory=(Resolve-Path -LiteralPath $CandidateDirectory).Path
$exe=Join-Path $directory 'HankiTools.exe'
$build=Get-Content -LiteralPath (Join-Path $directory 'build-info.json') -Raw | ConvertFrom-Json
$acceptance=Get-Content -LiteralPath (Join-Path $directory 'acceptance.json') -Raw | ConvertFrom-Json
$smoke=Get-Content -LiteralPath (Join-Path $directory 'ui-smoke.json') -Raw | ConvertFrom-Json
$hash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
if ($hash -ne $build.ExeSHA256 -or $hash -ne $acceptance.ExeSHA256) { throw 'Executable changed since build/acceptance. Rebuild and repeat checks.' }
if ($smoke.Passed -ne $true -or $smoke.Version -ne $build.Version) { throw 'Matching UI smoke result required.' }
$signature=Get-AuthenticodeSignature -LiteralPath $exe
if ($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate) { throw 'Build with a valid timestamped publisher signature before acceptance testing.' }
if ((Get-Date).ToUniversalTime() -ge [datetime]'2028-11-10T00:00:00Z') { throw 'This branch targets .NET 10, now outside its release window. Upgrade to a supported .NET LTS and revalidate before publishing.' }
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$metadata=Invoke-RestMethod 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json' -TimeoutSec 30
if ($build.Runtime -ne $metadata.'latest-runtime') { throw 'Bundled runtime is no longer current. Rebuild, sign and repeat acceptance.' }
$required=@('clean-install-launch','dpi-keyboard-contrast','all-module-navigation','readonly-diagnostics','scan-cancel-history','cleanup-recycle-restore','startup-and-undo','power-dns-and-undo','defender-controls','monitor-save-load','ai-consent-cancel','upgrade-data-retention','full-system-scan-activation','diagnostic-history-privacy','community-edition-boundaries')
if ([string]::IsNullOrWhiteSpace($acceptance.Tester) -or [string]::IsNullOrWhiteSpace($acceptance.WindowsVersion) -or [string]::IsNullOrWhiteSpace($acceptance.TestedAt)) { throw 'Record the tester, Windows version and test date.' }
foreach ($id in $required) {
    $matches=@($acceptance.Checks | Where-Object { $_.Id -eq $id })
    if ($matches.Count -ne 1 -or $matches[0].Passed -ne $true -or [string]::IsNullOrWhiteSpace($matches[0].Notes)) { throw "Acceptance check needs a passing result and evidence notes: $id" }
}
# Verify every shipped file, not just the executable. Keep tester notes and local smoke logs out of the public ZIP.
$payloadPaths=@('HankiTools.exe','Data\signatures.txt','LICENSE','README-PORTABLE.md','PRIVACY.md','RELEASE-NOTES.md')
foreach ($relative in $payloadPaths) {
    $entries=@($build.Payload | Where-Object { $_.Path -eq $relative })
    if ($entries.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $directory $relative) -Algorithm SHA256).Hash -ne $entries[0].SHA256) {
        throw "Shipped file changed since build: $relative"
    }
}
$zip=$directory + '-release.zip'
if (Test-Path -LiteralPath $zip) { throw 'Release ZIP exists. Nothing overwritten.' }
$staging=Join-Path ([IO.Path]::GetTempPath()) ('Hanki-release-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    New-Item -ItemType Directory -Path (Join-Path $staging 'Data') | Out-Null
    foreach ($relative in $payloadPaths) { Copy-Item -LiteralPath (Join-Path $directory $relative) -Destination (Join-Path $staging $relative) }
    @{ Version=$build.Version; Runtime=$build.Runtime; Sdk=$build.Sdk; ExeSHA256=$hash; Signed=$true; PublicReleaseApproved=$true;
       ApprovedAt=(Get-Date).ToUniversalTime().ToString('o'); Payload=$build.Payload } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $staging 'build-info.json') -Encoding UTF8
    $manifest=Join-Path $staging 'SHA256SUMS.txt'
    $lines=@(Get-ChildItem -LiteralPath $staging -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative=$_.FullName.Substring($staging.Length+1).Replace('\','/')
        "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)  $relative"
    })
    $lines | Set-Content -LiteralPath $manifest -Encoding UTF8
    New-HankiArchive -SourceDirectory $staging -DestinationZip $zip
} finally { Remove-Item -LiteralPath $staging -Recurse -Force }
"$((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash)  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding ASCII
Write-Host "Verified release package: $zip" -ForegroundColor Green
Write-Host 'Upload the ZIP and SHA256 file together. Keep signing keys private. An RC version must be labelled as a release candidate on the download page.'
