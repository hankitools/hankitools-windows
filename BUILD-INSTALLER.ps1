[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CandidateZip,
    [string]$OutputDirectory,
    [switch]$SmokeInstall
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'Build the Windows installer on Windows.' }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$compilerVersion = '6.7.3'
$compilerInstallerSha256 = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
$compilerInstallerUrl = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$root = $PSScriptRoot
$script:bootstrapCompilerHash = ''
$script:compilerRoot = $null
function Get-ByteSha256 {
    param([byte[]]$Bytes)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($Bytes)).Replace('-','') }
    finally { $algorithm.Dispose() }
}
function ConvertFrom-Utf8JsonBytes {
    param([byte[]]$Bytes)
    $json = [Text.Encoding]::UTF8.GetString($Bytes).TrimStart([char]0xFEFF)
    return ConvertFrom-Json -InputObject $json
}
function Remove-TemporaryCompiler {
    if (-not $script:compilerRoot) { return }
    $uninstaller = Join-Path $script:compilerRoot 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
        $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Temporary Inno Setup compiler uninstall exited with code $($process.ExitCode)." }
    }
    $tempRoot = Split-Path -Parent $script:compilerRoot
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
    $script:compilerRoot = $null
}
$zipPath = (Resolve-Path -LiteralPath $CandidateZip).Path
$zipChecksumPath = "$zipPath.sha256"
if (-not (Test-Path -LiteralPath $zipChecksumPath -PathType Leaf)) { throw 'Candidate ZIP checksum is missing.' }
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumLine = (Get-Content -LiteralPath $zipChecksumPath -Raw).Trim()
if ($checksumLine -notmatch ('^([0-9a-fA-F]{64})\s+' + [regex]::Escape([IO.Path]::GetFileName($zipPath)) + '$') -or
    $Matches[1].ToLowerInvariant() -ne $zipHash) { throw 'Candidate ZIP does not match its checksum file.' }

function Get-VerifiedCompiler {
    $temp = Join-Path ([IO.Path]::GetTempPath()) ('Hanki-InnoSetup-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp | Out-Null
    try {
        $installer = Join-Path $temp 'innosetup.exe'
        Invoke-WebRequest -Uri $compilerInstallerUrl -OutFile $installer -TimeoutSec 120
        if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() -ne $compilerInstallerSha256) {
            throw "Inno Setup $compilerVersion installer SHA-256 did not match the pinned upstream release."
        }
        $signature = Get-AuthenticodeSignature -LiteralPath $installer
        if ($signature.Status -ne 'Valid' -or
            $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -ne 'Pyrsys B.V.') {
            throw 'The pinned Inno Setup installer does not have a valid Pyrsys B.V. Authenticode signature.'
        }
        $installRoot = Join-Path $temp 'compiler'
        $script:compilerRoot = $installRoot
        $installLog = Join-Path $temp 'compiler-install.log'
        $process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $installRoot + '"'),('/LOG="' + $installLog + '"')) -Wait -PassThru
        if ($process.ExitCode -ne 0) {
            $detail = if (Test-Path -LiteralPath $installLog) { (Get-Content -LiteralPath $installLog -Tail 12) -join ' | ' } else { 'No installer log was produced.' }
            throw "Inno Setup installer exited with code $($process.ExitCode): $detail"
        }
        $compiler = Join-Path $installRoot 'ISCC.exe'
        if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'Pinned Inno Setup installation did not provide ISCC.exe.' }
        $compilerSignature = Get-AuthenticodeSignature -LiteralPath $compiler
        if ($compilerSignature.Status -ne 'Valid' -or
            $compilerSignature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -ne 'Pyrsys B.V.') {
            throw 'The extracted Inno Setup compiler does not have a valid Pyrsys B.V. Authenticode signature.'
        }
        $script:bootstrapCompilerHash = $compilerInstallerSha256
        return $compiler
    } catch {
        $installError = $_.Exception.Message
        try { Remove-TemporaryCompiler } catch { throw "Inno Setup setup failed: $installError Cleanup also failed: $($_.Exception.Message)" }
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
        throw
    }
}

$compiler = Get-VerifiedCompiler

$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
$stage = Join-Path ([IO.Path]::GetTempPath()) ('Hanki-Installer-' + [guid]::NewGuid().ToString('N'))
try {
    $normalized = @{}
    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName.Replace('\','/')
        if ($name.StartsWith('/') -or $name -match '^[a-zA-Z]:' -or $name.Contains(':') -or
            @($name.Split('/')) -contains '..' -or @($name.Split('/')) -contains '.') {
            throw "Unsafe path in candidate ZIP: $($entry.FullName)"
        }
        $key = $name.TrimEnd('/').ToLowerInvariant()
        if ($key -and $normalized.ContainsKey($key)) { throw "Duplicate normalized path in candidate ZIP: $name" }
        if ($key) { $normalized[$key] = $entry }
    }
    if ($zip.Entries.Count -eq 0) { throw 'Candidate ZIP is empty.' }
    $readEntry = {
        param([string]$Name)
        $key = $Name.ToLowerInvariant()
        if (-not $normalized.ContainsKey($key)) { throw "Candidate ZIP is missing $Name." }
        $entry = $normalized[$key]
        $stream = $entry.Open()
        try {
            $memory = [IO.MemoryStream]::new()
            try { $stream.CopyTo($memory); return ,$memory.ToArray() } finally { $memory.Dispose() }
        } finally { $stream.Dispose() }
    }
    $buildBytes = & $readEntry 'build-info.json'
    $build = ConvertFrom-Utf8JsonBytes -Bytes $buildBytes
    if ($build.Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$' -or
        $build.UiSmokePassed -ne $true -or $build.Signed -isnot [bool]) {
        throw 'build-info.json has an invalid version, signing state or UI smoke result.'
    }
    [xml]$project = Get-Content -LiteralPath (Join-Path $root 'src\IgezziGuard\IgezziGuard.csproj') -Raw
    if ([string]$project.Project.PropertyGroup.Version -ne [string]$build.Version) {
        throw "ZIP version $($build.Version) does not match the project version."
    }
    if ($normalized.ContainsKey('ui-smoke.json')) {
        $smoke = ConvertFrom-Utf8JsonBytes -Bytes (& $readEntry 'ui-smoke.json')
        if ($smoke.Passed -ne $true -or $smoke.Version -ne $build.Version) { throw 'Candidate ZIP has no matching passed UI smoke report.' }
    } elseif ($build.Signed -ne $true -or $build.PublicReleaseApproved -ne $true -or -not $normalized.ContainsKey('sha256sums.txt')) {
        throw 'ZIP must be a passed CI candidate or a signed and approved PACKAGE-RELEASE archive.'
    }

    $verifiedFiles = @{}
    foreach ($item in $build.Payload) {
        $relative = ([string]$item.Path).Replace('\','/')
        if ([string]::IsNullOrWhiteSpace($relative) -or $relative.StartsWith('/') -or $relative -match '^[a-zA-Z]:' -or
            @($relative.Split('/')) -contains '..' -or @($relative.Split('/')) -contains '.') {
            throw "Invalid payload path in build-info.json: $relative"
        }
        $bytes = & $readEntry $relative
        $hash = Get-ByteSha256 -Bytes $bytes
        if ($hash -ne ([string]$item.SHA256).ToUpperInvariant()) { throw "Payload hash does not match build-info.json: $relative" }
        $verifiedFiles[$relative.ToLowerInvariant()] = $bytes
    }
    $exe = & $readEntry 'HankiTools.exe'
    $exeHash = Get-ByteSha256 -Bytes $exe
    if ($exeHash -ne ([string]$build.ExeSHA256).ToUpperInvariant()) { throw 'HankiTools.exe does not match build-info.json.' }
    foreach ($required in @('HankiTools.exe','Data/signatures.txt','LICENSE','README-PORTABLE.md','PRIVACY.md','RELEASE-NOTES.md')) {
        if (-not $verifiedFiles.ContainsKey($required.ToLowerInvariant())) { throw "build-info.json does not validate required installer payload $required." }
    }

    $out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'dist' }
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    $installerName = "HankiTools-$($build.Version)-win-x64-setup.exe"
    $installerPath = Join-Path $out $installerName
    $checksumPath = "$installerPath.sha256"
    $receiptPath = Join-Path $out "HankiTools-$($build.Version)-win-x64-setup-build-info.json"
    foreach ($existing in @($installerPath,$checksumPath,$receiptPath)) {
        if (Test-Path -LiteralPath $existing) { throw "Output already exists; nothing overwritten: $existing" }
    }
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($required in @('HankiTools.exe','Data/signatures.txt','LICENSE','README-PORTABLE.md','PRIVACY.md','RELEASE-NOTES.md')) {
        $destination = Join-Path $stage ($required.Replace('/', '\'))
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        [IO.File]::WriteAllBytes($destination, $verifiedFiles[$required.ToLowerInvariant()])
    }
    $fileVersion = [string]$build.Version -replace '-.*$',''
    $fileVersion += '.0'
    $outputBase = [IO.Path]::GetFileNameWithoutExtension($installerName)
    $compilerOutput = Join-Path $stage 'compiler-output'
    New-Item -ItemType Directory -Path $compilerOutput | Out-Null
    $args = @(
        "/DAppVersion=$($build.Version)",
        "/DFileVersion=$fileVersion",
        "/DPayloadDir=$stage",
        "/DProjectRoot=$root",
        "/DOutputDir=$compilerOutput",
        "/DOutputBaseName=$outputBase",
        (Join-Path $root 'installer\HankiTools.iss')
    )
    & $compiler @args
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compiler failed with exit code $LASTEXITCODE." }
    $compiledInstaller = Join-Path $compilerOutput $installerName
    if (-not (Test-Path -LiteralPath $compiledInstaller -PathType Leaf)) { throw 'Inno Setup did not produce the expected installer.' }
    if ($SmokeInstall) {
        foreach ($scope in @('CURRENTUSER','ALLUSERS')) {
            $installDirectory = Join-Path $stage "installed-$scope"
            $installArguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/$scope",('/DIR="' + $installDirectory + '"'))
            $installProcess = Start-Process -FilePath $compiledInstaller -ArgumentList $installArguments -Wait -PassThru
            if ($installProcess.ExitCode -ne 0) { throw "Setup smoke install for $scope exited with code $($installProcess.ExitCode)." }
            foreach ($required in @('HankiTools.exe','Data\signatures.txt','LICENSE','README-PORTABLE.md','PRIVACY.md','RELEASE-NOTES.md')) {
                $installedFile = Join-Path $installDirectory $required
                if (-not (Test-Path -LiteralPath $installedFile -PathType Leaf) -or
                    (Get-FileHash -LiteralPath $installedFile -Algorithm SHA256).Hash -ne (Get-ByteSha256 -Bytes $verifiedFiles[$required.Replace('\','/').ToLowerInvariant()])) {
                    throw "Setup smoke install for $scope did not preserve the verified file $required."
                }
            }
            $uninstaller = Join-Path $installDirectory 'unins000.exe'
            if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) { throw "Setup smoke install for $scope did not create its uninstaller." }
            $uninstallProcess = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
            if ($uninstallProcess.ExitCode -ne 0 -or (Test-Path -LiteralPath (Join-Path $installDirectory 'HankiTools.exe'))) {
                throw "Setup smoke uninstall for $scope did not remove the installed app files."
            }
        }
    }
    Copy-Item -LiteralPath $compiledInstaller -Destination $installerPath
    $installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
    "$installerHash  $installerName" | Set-Content -LiteralPath $checksumPath -Encoding ASCII
    @{
        Version = $build.Version
        CandidateZipSHA256 = $zipHash
        PackageExeSHA256 = $exeHash
        SetupSHA256 = $installerHash
        CompilerVersion = $compilerVersion
        CompilerSHA256 = $script:bootstrapCompilerHash
    } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8
    Write-Host "Installer created from verified package: $installerPath" -ForegroundColor Green
    Write-Host "SHA-256: $installerHash"
} finally {
    $zip.Dispose()
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    Remove-TemporaryCompiler
}
