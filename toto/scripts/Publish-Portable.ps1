[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\artifacts\toto-win-x64")
)

$project = Join-Path $PSScriptRoot "..\src\Toto.App\Toto.App.csproj"
if (-not (Test-Path -LiteralPath $project)) {
    throw "未找到项目文件：$project"
}

dotnet publish $project -p:PublishProfile=PortableWinX64 -o $OutputDirectory
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$executable = Join-Path $OutputDirectory "toto.exe"
if (-not (Test-Path -LiteralPath $executable)) {
    throw "发布完成但未生成预期文件：$executable"
}

Write-Output $executable
