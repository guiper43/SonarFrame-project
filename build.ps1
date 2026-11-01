param([switch]$Pack)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools/nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $PSScriptRoot '.tools/http-cache'
$env:NUGET_SCRATCH = Join-Path $PSScriptRoot '.tools/scratch'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (Test-Path (Join-Path $PSScriptRoot '.tools/ffmpeg/ffprobe.exe')) {
    $env:SONARFRAME_FFPROBE = Join-Path $PSScriptRoot '.tools/ffmpeg/ffprobe.exe'
    $env:SONARFRAME_FFMPEG = Join-Path $PSScriptRoot '.tools/ffmpeg/ffmpeg.exe'
}
$localDotnet = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { 'dotnet' }
Push-Location $PSScriptRoot
try {
    & $dotnet build SonarFrame.slnx -c Release -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Compilação falhou.' }
    & $dotnet test SonarFrame.slnx -c Release --no-build -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Compilação ou testes falharam.' }
    if ($Pack) {
        & $dotnet pack SonarFrame.slnx -c Release --no-build -o artifacts/packages -m:1
        if ($LASTEXITCODE -ne 0) { throw 'Empacotamento falhou.' }
    }
} finally { Pop-Location }
