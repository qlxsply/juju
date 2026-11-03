#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Juju.sln'
if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
    throw "Solution not found; refusing to clean outside the repository: $solution"
}

$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
if (-not (Test-Path -LiteralPath $artifactRoot -PathType Container)) {
    Write-Host 'No artifacts directory exists; nothing to clean.'
    return
}

if ($PSCmdlet.ShouldProcess($artifactRoot, 'Delete all local build, publish, installer, and update-package outputs')) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
    Write-Host "Removed $artifactRoot"
}
