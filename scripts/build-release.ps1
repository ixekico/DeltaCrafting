[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts 'DeltaCrafter-win-x64'
$zipName = "DeltaCrafter-win-x64-$Version.zip"
$zipPath = Join-Path $artifacts $zipName
$checksumPath = "$zipPath.sha256"

foreach ($document in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $document))) {
        throw "Missing release document: $document"
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $root 'licenses') -PathType Container)) {
    throw 'Missing third-party license directory: licenses'
}

if (Test-Path -LiteralPath $artifacts) {
    $resolvedArtifacts = [System.IO.Path]::GetFullPath($artifacts)
    if (-not $resolvedArtifacts.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar)) {
        throw "Refusing to clean a directory outside the workspace: $resolvedArtifacts"
    }
    Remove-Item -LiteralPath $resolvedArtifacts -Recurse -Force
}
New-Item -ItemType Directory -Path $artifacts | Out-Null

Push-Location $root
try {
    dotnet restore DeltaCrafter.sln --configfile nuget.config
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }

    dotnet build DeltaCrafter.sln -c Release -p:Platform=x64 --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

    dotnet test tests\DeltaCrafter.Core.Tests\DeltaCrafter.Core.Tests.csproj `
        -c Release -p:Platform=x64 --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed' }

    dotnet publish src\DeltaCrafter.App\DeltaCrafter.App.csproj `
        -c Release -r win-x64 --self-contained true -p:Platform=x64 `
        -p:Version=$Version --no-restore -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

    Copy-Item -LiteralPath README.md, LICENSE, THIRD-PARTY-NOTICES.md -Destination $publishDir
    Copy-Item -LiteralPath licenses -Destination $publishDir -Recurse
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $checksumPath -Value "$hash  $zipName" -Encoding ascii
}
finally {
    Pop-Location
}

Write-Output "Release archive: $zipPath"
Write-Output "Checksum file: $checksumPath"
