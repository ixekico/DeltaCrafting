# Builds the verified Windows Setup executable and SHA-256 checksum.
[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0',

    [Parameter()]
    [switch]$SkipBuild,

    [Parameter()]
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
$requiredIsccHash = '0A8757031B33777E4C9CBFFEE40F11A5062B36D25CBE144C1DB73B6102B80AD7'
$requiredLanguageHash = '7D544B9BB1D142CFA11F2E5D3CC8ABE2E55F8E066C5124E3772675AA236E1278'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishDir = Join-Path $root 'artifacts\DeltaCrafter-win-x64'
$issPath = Join-Path $root 'installer\DeltaCrafter.iss'
$languagePath = Join-Path $root 'installer\Languages\ChineseSimplified.isl'

function Get-NormalizedProductVersion([string]$Path) {
    $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path).ProductVersion
    if ([string]::IsNullOrWhiteSpace($productVersion)) {
        throw "File has no product version: $Path"
    }
    return $productVersion.Split('+')[0].Trim()
}

if (-not (Test-Path -LiteralPath $issPath -PathType Leaf)) {
    throw "Missing installer definition: $issPath"
}
if (-not (Test-Path -LiteralPath $languagePath -PathType Leaf)) {
    throw "Missing required Simplified Chinese language file: $languagePath"
}
$actualLanguageHash = (Get-FileHash -LiteralPath $languagePath -Algorithm SHA256).Hash
if ($actualLanguageHash -ne $requiredLanguageHash) {
    throw "Simplified Chinese language file hash mismatch. Expected $requiredLanguageHash, got $actualLanguageHash"
}

if ($SkipBuild) {
    $payloadExe = Join-Path $publishDir 'DeltaCrafter.exe'
    if (-not (Test-Path -LiteralPath $payloadExe -PathType Leaf)) {
        throw "-SkipBuild requires an existing release payload: $publishDir"
    }
    $payloadVersion = Get-NormalizedProductVersion $payloadExe
    if ($payloadVersion -ne $Version) {
        throw "-SkipBuild payload version mismatch. Requested $Version, found $payloadVersion"
    }
}
else {
    & (Join-Path $PSScriptRoot 'build-release.ps1') -Version $Version
}

$isccCandidates = @(
    $IsccPath,
    $env:ISCC_PATH,
    (Join-Path $env:TEMP 'DeltaCrafter-Inno-6.7.3\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) }
$iscc = $isccCandidates | Select-Object -First 1
if (-not $iscc) {
    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) { $iscc = $command.Source }
}
if (-not $iscc) {
    throw 'Inno Setup 6.7.3 was not found. Run scripts\install-inno-setup.ps1 first.'
}

$actualIsccHash = (Get-FileHash -LiteralPath $iscc -Algorithm SHA256).Hash
if ($actualIsccHash -ne $requiredIsccHash) {
    throw "ISCC hash mismatch. The release requires the pinned Inno Setup 6.7.3 compiler."
}
$compilerSignature = Get-AuthenticodeSignature -LiteralPath $iscc
if (($compilerSignature.Status -ne 'Valid') -or
    ($compilerSignature.SignerCertificate.Subject -notmatch '(^|, )CN=Pyrsys B\.V\.(,|$)')) {
    throw "ISCC signature is not valid for Pyrsys B.V.: $($compilerSignature.Status)"
}

$fileVersion = $Version.Split('-')[0]
$setupName = "DeltaCrafter-Setup-$Version.exe"
$setupPath = Join-Path $root "artifacts\$setupName"
$checksumPath = "$setupPath.sha256"
Remove-Item -LiteralPath $setupPath, $checksumPath -Force -ErrorAction SilentlyContinue

$isccArgs = @(
    "/DMyAppVersion=$Version",
    "/DMyFileVersion=$fileVersion",
    "/DPayloadDir=$publishDir",
    "/DChineseIsl=$languagePath",
    $issPath
)
& $iscc @isccArgs
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Installer output was not created: $setupPath"
}

$setupVersion = Get-NormalizedProductVersion $setupPath
if ($setupVersion -ne $fileVersion) {
    throw "Installer version mismatch. Expected $fileVersion, found $setupVersion"
}
$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  $setupName" -Encoding ascii

Write-Output "Setup: $setupPath"
Write-Output "Checksum file: $checksumPath"
