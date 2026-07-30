# Exports one version section from CHANGELOG.md as the GitHub Release body.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$changelogPath = Join-Path $root 'CHANGELOG.md'
$targetPath = [System.IO.Path]::GetFullPath(
    $(if ([System.IO.Path]::IsPathRooted($OutputPath)) {
        $OutputPath
    } else {
        Join-Path $root $OutputPath
    }))

if (-not $targetPath.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar)) {
    throw "Release notes output must stay inside the repository: $targetPath"
}

$markdown = Get-Content -LiteralPath $changelogPath -Raw -Encoding UTF8
$escapedVersion = [regex]::Escape($Version)
$pattern = "(?ms)^## \[$escapedVersion\](?: - [^\r\n]+)?\r?\n(?<body>.*?)(?=^## \[|\z)"
$match = [regex]::Match($markdown, $pattern)
if (-not $match.Success) {
    throw "CHANGELOG.md does not contain a [$Version] release section."
}

$body = $match.Groups['body'].Value.Trim()
if ([string]::IsNullOrWhiteSpace($body) -or
    $body -notmatch '(?m)^### ' -or
    $body -notmatch '(?m)^- ') {
    throw "CHANGELOG.md [$Version] release section has no publishable content."
}

$targetDir = Split-Path -Parent $targetPath
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
[System.IO.File]::WriteAllText(
    $targetPath,
    $body + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))
Write-Output "Release notes: $targetPath"
