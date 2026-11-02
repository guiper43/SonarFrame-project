$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools/nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $PSScriptRoot '.tools/http-cache'
$env:NUGET_SCRATCH = Join-Path $PSScriptRoot '.tools/scratch'
$local = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path $local) { $local } else { 'dotnet' }
$pasta = Join-Path $PSScriptRoot '.tools/test-artifacts/prova-reproducao'
if (!(Test-Path (Join-Path $pasta 'origem.mp4')) -or !(Test-Path (Join-Path $pasta 'foto.png')) -or !(Test-Path (Join-Path $pasta 'camada.wav'))) {
    & (Join-Path $PSScriptRoot 'prova-reproducao.ps1')
}
$anterior = $env:SONARFRAME_MIDIAS_PREVIA
$matrizAnterior = $env:SONARFRAME_MIDIAS_MATRIZ
$matriz = Join-Path $PSScriptRoot '.tools/test-artifacts/validacao-reproducao'
if (!(Test-Path (Join-Path $matriz 'mídia de teste.opus.ogg'))) { & (Join-Path $PSScriptRoot 'validar-reproducao.ps1') }
try {
    $env:SONARFRAME_MIDIAS_PREVIA = $pasta
    $env:SONARFRAME_MIDIAS_MATRIZ = $matriz
    & $dotnet test (Join-Path $PSScriptRoot 'tests/SonarFrame.Desktop.Tests') -c Release --filter FullyQualifiedName~TestesPreviaNativa -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Teste da prévia nativa falhou.' }
} finally { $env:SONARFRAME_MIDIAS_PREVIA = $anterior; $env:SONARFRAME_MIDIAS_MATRIZ = $matrizAnterior }
