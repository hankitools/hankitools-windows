[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'build\Packaging.ps1')
$directory=(Resolve-Path -LiteralPath $CandidateDirectory).Path
$build=Get-Content -LiteralPath (Join-Path $directory 'build-info.json') -Raw | ConvertFrom-Json
$smoke=Get-Content -LiteralPath (Join-Path $directory 'ui-smoke.json') -Raw | ConvertFrom-Json
if ($smoke.Passed -ne $true -or $smoke.Version -ne $build.Version) { throw 'Successful matching UI smoke result required.' }
if ((Get-FileHash -LiteralPath (Join-Path $directory 'HankiTools.exe') -Algorithm SHA256).Hash -ne $build.ExeSHA256) { throw 'Executable changed since the build. Rebuild before packaging.' }
foreach ($entry in $build.Payload) {
    $full=[IO.Path]::GetFullPath((Join-Path $directory $entry.Path))
    if (-not $full.StartsWith($directory.TrimEnd('\') + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid build payload path.' }
    if ((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ne $entry.SHA256) { throw "Payload changed: $($entry.Path)" }
}
$zip=$directory + '-candidate-repacked-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.zip'
New-HankiArchive -SourceDirectory $directory -DestinationZip $zip
"$((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash)  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding ASCII
Write-Host "Candidate repackaged: $zip" -ForegroundColor Green
