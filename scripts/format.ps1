#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Juju.sln'
$toolManifest = Join-Path $repoRoot '.config\dotnet-tools.json'
$xamlSettings = Join-Path $repoRoot 'Settings.XamlStyler'
$requiredSdkVersion = '10.0.400'

function Assert-DotNetSdkVersion {
    $sdkVersion = & dotnet --version
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "Could not determine the .NET SDK version (exit code $exitCode)." }
    $sdkVersion = ($sdkVersion -join '').Trim()
    if ($sdkVersion -ne $requiredSdkVersion) {
        throw "This repository's formatting tools require .NET SDK $requiredSdkVersion; found '$sdkVersion'."
    }
    Write-Host ".NET SDK $sdkVersion"
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    Write-Host "`n== $Description =="
    & $Command @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$Description failed with exit code $exitCode."
    }
}

function Get-SourceXamlFiles {
    $roots = @(
        (Join-Path $repoRoot 'src'),
        (Join-Path $repoRoot 'tests')
    )

    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        Get-ChildItem -LiteralPath $root -Filter '*.xaml' -File -Recurse |
            Where-Object {
                $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
                $_.Name -notmatch '(\.g(\.i)?|\.generated)\.xaml$'
            }
    }
}

try {
    if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) { throw "Solution not found: $solution" }
    if (-not (Test-Path -LiteralPath $toolManifest -PathType Leaf)) { throw "Tool manifest not found: $toolManifest" }
    if (-not (Test-Path -LiteralPath $xamlSettings -PathType Leaf)) { throw "XAML Styler settings not found: $xamlSettings" }

    Push-Location $repoRoot
    try {
        Assert-DotNetSdkVersion
        Invoke-NativeCommand 'Restore local formatting tools' 'dotnet' @('tool', 'restore', '--tool-manifest', $toolManifest)
        Invoke-NativeCommand 'Restore solution projects' 'dotnet' @('restore', $solution)
        Invoke-NativeCommand 'Format C# in solution projects' 'dotnet' @('format', $solution, '--no-restore')

        $xamlFiles = @(Get-SourceXamlFiles | Sort-Object -Property FullName)
        Write-Host "`n== Format XAML ($($xamlFiles.Count) files) =="
        foreach ($file in $xamlFiles) {
            Write-Host "Formatting $($file.FullName.Substring($repoRoot.Length).TrimStart('\', '/'))"
            Invoke-NativeCommand "Format $($file.Name)" 'dotnet' @(
                'tool', 'run', 'xstyler', '--',
                '--file', $file.FullName,
                '--config', $xamlSettings
            )
        }
    }
    finally {
        Pop-Location
    }

    Write-Host "`nFormatting completed successfully."
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
