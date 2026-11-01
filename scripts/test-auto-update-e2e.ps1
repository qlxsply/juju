#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$AppPublishDirectory = (Join-Path $PSScriptRoot '..\artifacts\e2e-win-x64'),
    [string]$BootstrapDirectory = (Join-Path $PSScriptRoot '..\artifacts\e2e-bootstrap'),
    [switch]$KeepOnFailure
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$appPublish = [IO.Path]::GetFullPath($AppPublishDirectory)
$bootstrap = [IO.Path]::GetFullPath($BootstrapDirectory)
foreach ($path in @(
    (Join-Path $appPublish 'Juju.App.exe'),
    (Join-Path $bootstrap 'Juju.Launcher.exe'),
    (Join-Path $bootstrap 'Juju.Updater.exe')
)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required published file is missing: $path" }
}

$root = Join-Path $env:TEMP ('juju auto update e2e ' + [Guid]::NewGuid().ToString('N'))
$oldVersion = '1.0.0'
$newVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $appPublish 'Juju.App.exe')).ProductVersion.Split('+')[0]
if ([string]::IsNullOrWhiteSpace($newVersion)) { throw 'Could not read the published app version.' }
$packageName = "juju_v${newVersion}_win-x64.zip"
$manifestName = "juju_v${newVersion}_manifest.json"
$appProcessPath = Join-Path $root "versions\$newVersion\Juju.App.exe"
$updaterProcess = $null
$oldProcess = $null
$succeeded = $false

function Quote-Argument([string]$Value) {
    return '"' + $Value.Replace('"', '\"') + '"'
}

function Stop-TestApplication {
    Get-Process -Name 'Juju.App', 'Juju.Launcher', 'Juju.Updater' -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $executablePath = [IO.Path]::GetFullPath($_.MainModule.FileName)
            if ($executablePath.StartsWith([IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar,
                    [StringComparison]::OrdinalIgnoreCase)) {
                Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            }
        }
        catch { }
    }
}

try {
    $stateDirectory = Join-Path $root 'state'
    $dataDirectory = Join-Path $root 'data'
    $versionsDirectory = Join-Path $root 'versions'
    $stagingDirectory = Join-Path $root 'staging'
    $oldDirectory = Join-Path $versionsDirectory $oldVersion
    New-Item -ItemType Directory -Force -Path $stateDirectory, $dataDirectory, $oldDirectory, $stagingDirectory | Out-Null
    Copy-Item -Path (Join-Path $appPublish '*') -Destination $oldDirectory -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $bootstrap 'Juju.Launcher.exe') -Destination (Join-Path $root 'Juju.Launcher.exe')
    Copy-Item -LiteralPath (Join-Path $bootstrap 'Juju.Updater.exe') -Destination (Join-Path $root 'Juju.Updater.exe')
    [IO.File]::WriteAllText((Join-Path $stateDirectory 'active.json'), '{"activeVersion":"1.0.0"}', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $dataDirectory 'juju.json'), '{"schemaVersion":1}', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $dataDirectory 'config.json'), '{"startMinimized":true}', [Text.UTF8Encoding]::new($false))

    $packagePath = Join-Path $stagingDirectory $packageName
    $manifestPath = Join-Path $stagingDirectory $manifestName
    Compress-Archive -Path (Join-Path $appPublish '*') -DestinationPath $packagePath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{
        schemaVersion = 1
        version = $newVersion
        channel = 'stable'
        rid = 'win-x64'
        packageAsset = $packageName
        sha256 = $hash
        minimumLauncherVersion = '1.0.0'
        releaseNotesUrl = "https://github.com/qlxsply/juju/releases/tag/juju_v$newVersion"
    } | ConvertTo-Json
    [IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))

    $oldProcess = Start-Process -FilePath $env:ComSpec -ArgumentList '/c ping -n 4 127.0.0.1 > nul' -WindowStyle Hidden -PassThru
    $gatePath = Join-Path $stagingDirectory 'continue-update'
    [IO.File]::WriteAllText($gatePath, 'GO', [Text.UTF8Encoding]::new($false))
    $arguments = @('--root', $root, '--package', $packagePath, '--manifest', $manifestPath,
        '--version', $newVersion, '--pid', $oldProcess.Id.ToString(), '--gate', $gatePath)
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = Join-Path $root 'Juju.Updater.exe'
    $startInfo.WorkingDirectory = $root
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = (($arguments | ForEach-Object { Quote-Argument $_ }) -join ' ')
    $updaterProcess = [Diagnostics.Process]::Start($startInfo)
    if (-not $updaterProcess.WaitForExit(180000)) {
        $updaterProcess.Kill()
        throw 'Updater did not finish within three minutes.'
    }
    if ($updaterProcess.ExitCode -ne 0) { throw "Updater exited with code $($updaterProcess.ExitCode)." }

    $state = Get-Content -LiteralPath (Join-Path $stateDirectory 'active.json') -Raw | ConvertFrom-Json
    if ($state.activeVersion -ne $newVersion -or $state.previousVersion -ne $oldVersion -or $state.pendingVersion) {
        throw "Version state did not commit the candidate: $($state | ConvertTo-Json -Compress)"
    }
    if (-not (Test-Path -LiteralPath $appProcessPath -PathType Leaf)) { throw 'Candidate application was not staged.' }
    if (Test-Path -LiteralPath $packagePath) { throw 'Updater did not clean the consumed package.' }
    $succeeded = $true
    Write-Host "End-to-end update succeeded: $oldVersion -> $newVersion; previous version retained for rollback."
}
finally {
    if ($oldProcess -and -not $oldProcess.HasExited) { Stop-Process -Id $oldProcess.Id -Force -ErrorAction SilentlyContinue }
    if ($updaterProcess -and -not $updaterProcess.HasExited) { Stop-Process -Id $updaterProcess.Id -Force -ErrorAction SilentlyContinue }
    if ($updaterProcess) { $updaterProcess.Dispose() }
    Stop-TestApplication
    if ($succeeded -or -not $KeepOnFailure) {
        if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
    }
    else {
        Write-Host "Preserved failed test root for diagnosis: $root" -ForegroundColor Yellow
    }
}
