#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')]
    [string]$Version,
    [ValidatePattern('^[A-Za-z0-9_-]*$')]
    [string]$InstallerOutputSuffix = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Juju.sln'
$editorDirectory = Join-Path $repoRoot 'editor'
$installerScript = Join-Path $repoRoot 'installer\juju.iss'
$innoCompiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'

if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
    throw "Solution not found; run this script from a checkout containing Juju.sln: $solution"
}
if (-not (Test-Path -LiteralPath $editorDirectory -PathType Container)) {
    throw "Editor directory not found: $editorDirectory"
}
if (-not (Test-Path -LiteralPath (Join-Path $editorDirectory 'package-lock.json') -PathType Leaf)) {
    throw 'editor/package-lock.json is missing.'
}
if (-not (Test-Path -LiteralPath $installerScript -PathType Leaf)) {
    throw "Inno Setup script not found: $installerScript"
}
if (-not (Test-Path -LiteralPath $innoCompiler -PathType Leaf)) {
    throw "Inno Setup 6 compiler not found: $innoCompiler"
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

try {
    Push-Location $repoRoot
    try {
        $sdkVersion = (& dotnet --version | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Could not determine the .NET SDK version.' }
        if ($sdkVersion -ne '10.0.400') {
            throw "This release build requires .NET SDK 10.0.400; found '$sdkVersion'."
        }
        $nodeVersion = (& node --version | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $nodeVersion -notmatch '^v(?<major>\d+)\.(?<minor>\d+)\.') {
            throw "Could not parse the installed Node.js version '$nodeVersion'."
        }
        $nodeMajor = [int]$Matches.major
        $nodeMinor = [int]$Matches.minor
        $supportedNode = ($nodeMajor -eq 22 -and $nodeMinor -ge 12) -or $nodeMajor -ge 24
        if (-not $supportedNode) {
            throw "Release build requires Node.js 22.12+ or 24+ (Vite 7 requirement); found '$nodeVersion'."
        }

        # Prevent stale publish files or local data from leaking into the new package.
        # A suffix permits rebuilding while an older installer EXE is still open; preserve
        # that locked installer while replacing all generated payload and manifest inputs.
        if ([string]::IsNullOrEmpty($InstallerOutputSuffix)) {
            & (Join-Path $PSScriptRoot 'clean-artifacts.ps1') -Confirm:$false
        }
        else {
            foreach ($relativeOutput in @('artifacts\win-x64', 'artifacts\bootstrap', 'artifacts\release')) {
                $outputPath = Join-Path $repoRoot $relativeOutput
                if (Test-Path -LiteralPath $outputPath) {
                    Remove-Item -LiteralPath $outputPath -Recurse -Force
                }
            }
        }

        Invoke-NativeCommand 'Restore .NET solution' 'dotnet' @('restore', $solution)
        Invoke-NativeCommand 'Install locked editor dependencies' 'npm' @('ci', '--prefix', 'editor')
        Invoke-NativeCommand 'Publish self-contained Windows x64 application' 'dotnet' @(
            'publish', 'src/Juju.App/Juju.App.csproj', '-c', 'Release', '-r', 'win-x64',
            '--self-contained', 'true', "-p:Version=$Version", '-o', 'artifacts/win-x64'
        )
        Invoke-NativeCommand 'Publish stable Launcher' 'dotnet' @(
            'publish', 'src/Juju.Launcher/Juju.Launcher.csproj', '-c', 'Release', '-r', 'win-x64',
            '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:Version=1.0.0', '-o', 'artifacts/bootstrap'
        )
        Invoke-NativeCommand 'Publish standalone Updater' 'dotnet' @(
            'publish', 'src/Juju.Updater/Juju.Updater.csproj', '-c', 'Release', '-r', 'win-x64',
            '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:Version=1.0.0', '-o', 'artifacts/bootstrap'
        )

        $appExecutable = Join-Path $repoRoot 'artifacts\win-x64\Juju.App.exe'
        $launcherExecutable = Join-Path $repoRoot 'artifacts\bootstrap\Juju.Launcher.exe'
        $updaterExecutable = Join-Path $repoRoot 'artifacts\bootstrap\Juju.Updater.exe'
        foreach ($requiredFile in @($appExecutable, $launcherExecutable, $updaterExecutable)) {
            if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
                throw "Publish output is incomplete: $requiredFile"
            }
        }
        if (Test-Path -LiteralPath (Join-Path $repoRoot 'artifacts\win-x64\data')) {
            throw 'App publish output unexpectedly contains a data directory; refusing to package it.'
        }

        $manifestPath = Join-Path $repoRoot 'artifacts\installer\juju-payload-manifest.json'
        $manifestDirectory = Split-Path -Parent $manifestPath
        if (-not (Test-Path -LiteralPath $manifestDirectory -PathType Container)) {
            New-Item -ItemType Directory -Path $manifestDirectory | Out-Null
        }
        $entries = [System.Collections.Generic.List[object]]::new()
        $payloadBytes = [long]0
        $bootstrapRoot = (Get-Item -LiteralPath (Join-Path $repoRoot 'artifacts\bootstrap')).FullName.TrimEnd('\')
        $appPayloadRoot = (Get-Item -LiteralPath (Join-Path $repoRoot 'artifacts\win-x64')).FullName.TrimEnd('\')
        foreach ($fileName in @('Juju.Launcher.exe', 'Juju.Updater.exe')) {
            $file = Get-Item -LiteralPath (Join-Path $bootstrapRoot $fileName)
            $relative = $file.FullName.Substring($bootstrapRoot.Length).TrimStart([char[]]@('\', '/')).Replace('\', '/')
            $entries.Add([ordered]@{
                path = $relative
                size = [long]$file.Length
                sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            })
            $payloadBytes += [long]$file.Length
        }
        foreach ($file in Get-ChildItem -LiteralPath $appPayloadRoot -File -Recurse) {
            $relative = $file.FullName.Substring($appPayloadRoot.Length).TrimStart([char[]]@('\', '/')).Replace('\', '/')
            $entries.Add([ordered]@{
                path = "versions/$Version/$relative"
                size = [long]$file.Length
                sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            })
            $payloadBytes += [long]$file.Length
        }
        $orderedEntries = @($entries | Sort-Object -Property path)
        $manifest = [ordered]@{ schemaVersion = 1; version = $Version; entries = $orderedEntries }
        $manifestJson = ConvertTo-Json -InputObject $manifest -Depth 8 -Compress
        [System.IO.File]::WriteAllText($manifestPath, $manifestJson, [System.Text.UTF8Encoding]::new($false))
        $manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Host "Payload manifest: $manifestPath"
        Write-Host "Uncompressed payload: $payloadBytes bytes"

        Write-Host "`n== Compile Inno Setup installer =="
        $installerBaseFilename = "juju_v${Version}_setup_win64${InstallerOutputSuffix}"
        $compilerArguments = @("/DAppVersion=$Version", "/DPayloadBytes=$payloadBytes",
            "/DManifestSha256=$manifestSha256")
        if (-not [string]::IsNullOrEmpty($InstallerOutputSuffix)) {
            $compilerArguments += "/F$installerBaseFilename"
        }
        $compilerArguments += $installerScript
        & $innoCompiler @compilerArguments
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) { throw "Inno Setup failed with exit code $exitCode." }

        $installer = Join-Path $repoRoot "artifacts\installer\$installerBaseFilename.exe"
        if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
            throw "Installer was not created: $installer"
        }
        Write-Host "`nRelease installer created: $installer"
        Write-Host 'The GitHub Release workflow additionally creates the version ZIP and SHA-256 manifest.'
    }
    finally {
        Pop-Location
    }
}
catch {
    Write-Error $_
    exit 1
}
