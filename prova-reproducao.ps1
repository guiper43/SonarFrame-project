param([switch]$Manual, [switch]$Benchmark, [switch]$Publish)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools/nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $PSScriptRoot '.tools/http-cache'
$env:NUGET_SCRATCH = Join-Path $PSScriptRoot '.tools/scratch'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$local = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path $local) { $local } else { 'dotnet' }
$projeto = Join-Path $PSScriptRoot 'samples/SonarFrame.ProvaReproducao/SonarFrame.ProvaReproducao.csproj'
$pasta = Join-Path $PSScriptRoot '.tools/test-artifacts/prova-reproducao'
if (!(Test-Path (Join-Path $PSScriptRoot '.tools/ffmpeg/ffmpeg.exe'))) { & (Join-Path $PSScriptRoot 'setup-media.ps1') }
if ($Publish) {
    $destino = Join-Path $pasta 'portatil'
    & $dotnet publish $projeto -c Release -r win-x64 --self-contained true -o $destino -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Publicação da prova falhou.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '.tools/ffmpeg/ffmpeg.exe'), (Join-Path $PSScriptRoot '.tools/ffmpeg/ffprobe.exe'), (Join-Path $PSScriptRoot '.tools/ffmpeg/LICENSE.txt'), (Join-Path $PSScriptRoot '.tools/ffmpeg/README.txt') -Destination $destino
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/prova-reproducao.md') -Destination $destino
    Write-Output "Prova portátil em $destino. Execute SonarFrame.ProvaReproducao.exe para abrir o laboratório."
    exit 0
}
$argumentos = @('--pasta', $pasta, '--ffmpeg', (Join-Path $PSScriptRoot '.tools/ffmpeg/ffmpeg.exe'), '--ffprobe', (Join-Path $PSScriptRoot '.tools/ffmpeg/ffprobe.exe'))
if (!$Manual) { $argumentos += '--automatico' }
if ($Benchmark) { $argumentos += '--benchmark' }
& $dotnet run --project $projeto -c Release -- @argumentos
if ($LASTEXITCODE -ne 0) { throw "Prova falhou. Consulte $pasta/resultado.json ou falha.txt." }
if (!$Manual) { Get-Content -LiteralPath (Join-Path $pasta 'resultado.json') }
