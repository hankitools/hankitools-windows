[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CandidateDirectory,
    # Default is a draft release for review on GitHub; -Publish makes it public immediately.
    [switch]$Publish,
    # Run every check without creating anything on GitHub.
    [switch]$DryRun
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'Run on Windows to verify Authenticode.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=$PSScriptRoot
function Git { $output=& git -C $root @args; if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed." }; $output }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Install GitHub CLI (winget install --id GitHub.cli) and run gh auth login.' }

# The verified ZIP and checksum come only from PACKAGE-RELEASE.ps1, which enforces signing and acceptance.
$directory=(Resolve-Path -LiteralPath $CandidateDirectory).Path.TrimEnd('\')
$zip=$directory + '-release.zip'; $sumFile="$zip.sha256"
if (-not (Test-Path -LiteralPath $zip) -or -not (Test-Path -LiteralPath $sumFile)) { throw 'Verified release ZIP not found. Run PACKAGE-RELEASE.ps1 first; it refuses unsigned or unaccepted candidates.' }
$zipHash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
if ((((Get-Content -LiteralPath $sumFile -Raw).Trim()) -split '\s+')[0] -ne $zipHash) { throw 'ZIP does not match its .sha256 file.' }

# Re-verify the package from its own contents, as a downloader would.
$check=Join-Path ([IO.Path]::GetTempPath()) ('Hanki-publish-' + [guid]::NewGuid().ToString('N'))
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $check)
    $build=Get-Content -LiteralPath (Join-Path $check 'build-info.json') -Raw | ConvertFrom-Json
    if ($build.Signed -ne $true -or $build.PublicReleaseApproved -ne $true) { throw 'Package is not marked signed and approved by PACKAGE-RELEASE.ps1.' }
    $exe=Join-Path $check 'HankiTools.exe'
    if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $build.ExeSHA256) { throw 'Executable does not match build-info.json.' }
    $signature=Get-AuthenticodeSignature -LiteralPath $exe
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate) { throw "Executable signature is not valid and timestamped ($($signature.Status))." }
    $signer=$signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
    $listed=@{}
    foreach ($line in Get-Content -LiteralPath (Join-Path $check 'SHA256SUMS.txt')) {
        if ($line -notmatch '^([0-9A-F]{64})\s+(.+)$') { throw "Malformed SHA256SUMS.txt line: $line" }
        $listed[$Matches[2]]=$Matches[1]
    }
    foreach ($file in Get-ChildItem -LiteralPath $check -File -Recurse) {
        $relative=$file.FullName.Substring($check.Length+1).Replace('\','/')
        if ($relative -eq 'SHA256SUMS.txt') { continue }
        if (-not $listed.ContainsKey($relative) -or $listed[$relative] -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { throw "SHA256SUMS.txt does not match $relative." }
    }
} finally { if (Test-Path -LiteralPath $check) { Remove-Item -LiteralPath $check -Recurse -Force } }

# The release must describe a pushed, clean commit whose project version matches the package.
[xml]$project=Get-Content -LiteralPath (Join-Path $root 'src\IgezziGuard\IgezziGuard.csproj') -Raw
$version=[string]$build.Version
if ([string]$project.Project.PropertyGroup.Version -ne $version) { throw "Package version $version does not match the project version." }
if (Git status --porcelain) { throw 'Working tree has uncommitted changes. Commit and push first.' }
Git fetch --quiet origin | Out-Null
$head=(Git rev-parse HEAD).Trim()
if ($head -ne (Git rev-parse '@{u}').Trim()) { throw 'HEAD is not pushed to its upstream branch.' }
$tag="v$version"
if (Git tag --list $tag) { throw "Tag $tag already exists locally." }
if (Git ls-remote --tags origin "refs/tags/$tag") { throw "Tag $tag already exists on GitHub." }
$repo=((Git remote get-url origin) -replace '^https://github.com/','' -replace '^git@github.com:','' -replace '\.git$','').Trim()
& gh auth status --hostname github.com *> $null
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI is not signed in. Run: gh auth login' }
& gh release view $tag --repo $repo *> $null
if ($LASTEXITCODE -eq 0) { throw "A GitHub release named $tag already exists." }

# Notes: this version's section of RELEASE-NOTES.md plus verification and scope statements.
$candidate=$version.Contains('-')
$section=@(); $inSection=$false
foreach ($line in Get-Content -LiteralPath (Join-Path $root 'RELEASE-NOTES.md') -Encoding UTF8) {
    if ($line -match '^# Hanki Tools (\S+)') { if ($inSection) { break }; $inSection=($Matches[1] -eq $version); if ($inSection) { continue } }
    if ($inSection) { $section+=$line }
}
if ($section.Count -eq 0) { throw "RELEASE-NOTES.md has no section for $version." }
$zipName=[IO.Path]::GetFileName($zip)
$notes=@(
    $(if ($candidate) { '> **Release candidate.** Please report problems through GitHub issues before a final release.'; '' })
    ($section -join "`n").Trim(); ''
    '## Download and verify'; ''
    "1. Download ``$zipName`` and ``$zipName.sha256``."
    "2. In PowerShell: ``(Get-FileHash .\$zipName).Hash`` must equal the value in the ``.sha256`` file."
    "3. After extracting, HankiTools.exe > Properties > Digital Signatures must show **$signer** with a valid timestamp."; ''
    'Portable app: extract and run HankiTools.exe; no installer or .NET runtime needed. Updates are manual.'
    'Privacy: see PRIVACY.md in the package. The experimental file scanner is not an antivirus; Microsoft Defender remains your protection.'
) -join "`n"
$title="Hanki Tools $version" + $(if ($candidate) { ' (release candidate)' } else { '' })

Write-Host "Verified: $zipName (SHA-256 $zipHash), signed by $signer, commit $head, tag $tag." -ForegroundColor Green
if ($DryRun) { Write-Host "Dry run: would create $(if ($Publish) { 'a public' } else { 'a draft' }) release '$title' on $repo."; Write-Host $notes; return }

$notesFile=Join-Path ([IO.Path]::GetTempPath()) ('Hanki-notes-' + [guid]::NewGuid().ToString('N') + '.md')
try {
    [IO.File]::WriteAllText($notesFile, $notes, [Text.UTF8Encoding]::new($false))
    $arguments=@('release','create',$tag,$zip,$sumFile,'--repo',$repo,'--target',$head,'--title',$title,'--notes-file',$notesFile)
    if ($candidate) { $arguments+='--prerelease' }
    if (-not $Publish) { $arguments+='--draft' }
    $url=& gh @arguments
    if ($LASTEXITCODE -ne 0) { throw 'gh release create failed; nothing further was changed.' }
} finally { Remove-Item -LiteralPath $notesFile -ErrorAction SilentlyContinue }

# Compare the uploaded asset with the local package. The releases API lists drafts (which have no tag yet)
# and reports each asset's SHA-256 digest; the size is compared when no digest is reported.
$releases=(& gh api "repos/$repo/releases?per_page=30") | ConvertFrom-Json
$asset=@($releases | Where-Object { $_.tag_name -eq $tag } | ForEach-Object { $_.assets } | Where-Object { $_.name -eq $zipName }) | Select-Object -First 1
$digest=if ($asset -and $asset.PSObject.Properties['digest']) { [string]$asset.digest } else { '' }
$matched=if ($digest) { $digest -eq ('sha256:' + $zipHash.ToLowerInvariant()) } else { $null -ne $asset -and $asset.size -eq (Get-Item -LiteralPath $zip).Length }
if (-not $matched) { throw "Uploaded ZIP could not be verified. Inspect or delete the release: $url" }
Write-Host "$(if ($Publish) { 'Published' } else { 'Draft created' }): $url" -ForegroundColor Green
Write-Host ("Uploaded ZIP verified by " + $(if ($digest) { 'SHA-256 digest' } else { 'size' }) + '. Review the notes on GitHub before publishing a draft.')
