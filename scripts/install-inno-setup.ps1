# Installs the exact, signed Inno Setup compiler used by local and CI releases.
[CmdletBinding()]
param(
    [Parameter()]
    [string]$Destination = (Join-Path $env:TEMP 'DeltaCrafter-Inno-6.7.3')
)

$ErrorActionPreference = 'Stop'
$version = '6.7.3'
$installerHash = '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732'
$isccHash = '0A8757031B33777E4C9CBFFEE40F11A5062B36D25CBE144C1DB73B6102B80AD7'
$installerUrl = "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-$version.exe"
$destinationFullPath = [System.IO.Path]::GetFullPath($Destination)
$isccPath = Join-Path $destinationFullPath 'ISCC.exe'

if (Test-Path -LiteralPath $isccPath -PathType Leaf) {
    $actualIsccHash = (Get-FileHash -LiteralPath $isccPath -Algorithm SHA256).Hash
    if ($actualIsccHash -eq $isccHash) {
        Write-Output $isccPath
        exit 0
    }
    throw "Unexpected ISCC binary at $isccPath"
}

$downloadDir = Join-Path $env:TEMP 'DeltaCrafter-Inno-Download'
$installerPath = Join-Path $downloadDir "innosetup-$version.exe"
New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null
Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath -UseBasicParsing

$actualHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
if ($actualHash -ne $installerHash) {
    throw "Inno Setup installer hash mismatch. Expected $installerHash, got $actualHash"
}

$signature = Get-AuthenticodeSignature -LiteralPath $installerPath
if (($signature.Status -ne 'Valid') -or
    ($signature.SignerCertificate.Subject -notmatch '(^|, )CN=Pyrsys B\.V\.(,|$)')) {
    throw "Inno Setup installer signature is not valid for Pyrsys B.V.: $($signature.Status)"
}

New-Item -ItemType Directory -Force -Path $destinationFullPath | Out-Null
$bootstrapArguments = @(
    '/VERYSILENT',
    '/SUPPRESSMSGBOXES',
    '/NORESTART',
    '/PORTABLE=1',
    "/DIR=`"$destinationFullPath`""
)
# PowerShell 7 does not reliably wait for GUI executables invoked with "&".
# Waiting explicitly makes the compiler installation result identical locally and in CI.
$bootstrapProcess = Start-Process -FilePath $installerPath `
    -ArgumentList $bootstrapArguments -Wait -PassThru -WindowStyle Hidden
if ($bootstrapProcess.ExitCode -ne 0) {
    throw "Inno Setup bootstrap installer failed with exit code $($bootstrapProcess.ExitCode)"
}
if (-not (Test-Path -LiteralPath $isccPath -PathType Leaf)) {
    throw "ISCC.exe was not installed to $destinationFullPath"
}

$actualIsccHash = (Get-FileHash -LiteralPath $isccPath -Algorithm SHA256).Hash
if ($actualIsccHash -ne $isccHash) {
    throw "Installed ISCC hash does not match Inno Setup $version"
}

Write-Output $isccPath
