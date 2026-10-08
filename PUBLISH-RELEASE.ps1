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
$acceptance=Get-Content -LiteralPath (Join-Path $directory 'acceptance.json') -Raw | ConvertFrom-Json
if ($acceptance.ExeSHA256 -ne $build.ExeSHA256) { throw 'Acceptance evidence does not match the signed application executable in the release ZIP.' }
$installerChecks=@('installer-per-user','installer-all-users','installer-upgrade-uninstall-retention','installer-no-auto-start-uac')
foreach ($id in $installerChecks) {
    $matches=@($acceptance.Checks | Where-Object { $_.Id -eq $id })
    if ($matches.Count -ne 1 -or $matches[0].Passed -ne $true -or [string]::IsNullOrWhiteSpace($matches[0].Notes)) {
        throw "Installer acceptance needs a passing result and evidence notes before publishing: $id"
    }
}
$setupName="HankiTools-$version-win-x64-setup.exe"
$setup=Join-Path $root "dist\$setupName"
$setupSum="$setup.sha256"
$setupReceipt=Join-Path $root "dist\HankiTools-$version-win-x64-setup-build-info.json"
if (-not (Test-Path -LiteralPath $setup -PathType Leaf) -or -not (Test-Path -LiteralPath $setupSum -PathType Leaf) -or -not (Test-Path -LiteralPath $setupReceipt -PathType Leaf)) {
    throw 'Verified setup installer, checksum and build receipt are required. Run PACKAGE-RELEASE.ps1 to create them from this signed ZIP.'
}
$setupHash=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
if ((((Get-Content -LiteralPath $setupSum -Raw).Trim()) -split '\s+')[0] -ne $setupHash) { throw 'Setup installer does not match its .sha256 file.' }
$receipt=Get-Content -LiteralPath $setupReceipt -Raw | ConvertFrom-Json
if ($receipt.Version -ne $version -or $receipt.CandidateZipSHA256 -ne $zipHash.ToLowerInvariant() -or
    $receipt.SetupSHA256 -ne $setupHash -or $receipt.PackageExeSHA256 -ne $build.ExeSHA256 -or
    $receipt.CompilerVersion -ne '6.7.3' -or $receipt.CompilerSHA256 -ne '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732') {
    throw 'Installer build receipt does not match the verified signed ZIP and setup file.'
}
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
    "1. Download ``$zipName`` and ``$zipName.sha256`` for the portable app, or ``$setupName`` and ``$setupName.sha256`` for setup."
    "2. In PowerShell, compare ``(Get-FileHash .\<downloaded-file>).Hash`` with the matching ``.sha256`` value before running it."
    "3. The setup program is unsigned and may trigger SmartScreen. It offers current-user and all-users scopes; the installed HankiTools.exe > Properties > Digital Signatures must show **$signer** with a valid timestamp."; ''
    'The portable app needs no installer or .NET runtime. Updates are manual. Setup uninstalls the app files but preserves %LOCALAPPDATA%\IgezziGuard history and recovery data. It does not add a service or auto-start entry.'
    'Privacy: see PRIVACY.md in the package. The experimental file scanner is not an antivirus; Microsoft Defender remains your protection.'; ''
    '## Code signing policy'; ''
    $(if ($signer -like '*SignPath Foundation*') { 'Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).' } else { "Signed by $signer." })
    "Team roles, signing rules and the privacy policy: [Code signing policy](https://github.com/$repo#code-signing-policy)."
) -join "`n"
$title="Hanki Tools $version" + $(if ($candidate) { ' (release candidate)' } else { '' })

Write-Host "Verified: $zipName (SHA-256 $zipHash), signed by $signer, commit $head, tag $tag." -ForegroundColor Green
if ($DryRun) { Write-Host "Dry run: would create $(if ($Publish) { 'a public' } else { 'a draft' }) release '$title' on $repo."; Write-Host $notes; return }

$notesFile=Join-Path ([IO.Path]::GetTempPath()) ('Hanki-notes-' + [guid]::NewGuid().ToString('N') + '.md')
try {
    [IO.File]::WriteAllText($notesFile, $notes, [Text.UTF8Encoding]::new($false))
    $arguments=@('release','create',$tag,$zip,$sumFile,$setup,$setupSum,'--repo',$repo,'--target',$head,'--title',$title,'--notes-file',$notesFile)
    if ($candidate) { $arguments+='--prerelease' }
    if (-not $Publish) { $arguments+='--draft' }
    $url=& gh @arguments
    if ($LASTEXITCODE -ne 0) { throw 'gh release create failed; nothing further was changed.' }
} finally { Remove-Item -LiteralPath $notesFile -ErrorAction SilentlyContinue }

# Compare the uploaded asset with the local package. The releases API lists drafts (which have no tag yet)
# and reports each asset's SHA-256 digest; the size is compared when no digest is reported.
$releases=(& gh api "repos/$repo/releases?per_page=30") | ConvertFrom-Json
foreach ($upload in @(@{Path=$zip;Hash=$zipHash},@{Path=$setup;Hash=$setupHash},@{Path=$sumFile;Hash=(Get-FileHash -LiteralPath $sumFile -Algorithm SHA256).Hash},@{Path=$setupSum;Hash=(Get-FileHash -LiteralPath $setupSum -Algorithm SHA256).Hash})) {
    $name=[IO.Path]::GetFileName($upload.Path)
    $asset=@($releases | Where-Object { $_.tag_name -eq $tag } | ForEach-Object { $_.assets } | Where-Object { $_.name -eq $name }) | Select-Object -First 1
    $digest=if ($asset -and $asset.PSObject.Properties['digest']) { [string]$asset.digest } else { '' }
    $expectedHash=([string]$upload.Hash).ToLowerInvariant()
    $matched=if ($digest) { $digest -eq ('sha256:' + $expectedHash) } else { $null -ne $asset -and $asset.size -eq (Get-Item -LiteralPath $upload.Path).Length }
    if (-not $matched) { throw "Uploaded asset $name could not be verified. Inspect or delete the release: $url" }
}
Write-Host "$(if ($Publish) { 'Published' } else { 'Draft created' }): $url" -ForegroundColor Green
Write-Host 'Uploaded ZIP, setup EXE and both checksums verified. Review the notes on GitHub before publishing a draft.'
