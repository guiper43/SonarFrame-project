param([ValidateSet('antes', 'depois')][string]$Etapa = 'depois')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools/nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $PSScriptRoot '.tools/http-cache'
$env:NUGET_SCRATCH = Join-Path $PSScriptRoot '.tools/scratch'
$local = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path $local) { $local } else { 'dotnet' }
$ffmpeg = Join-Path $PSScriptRoot '.tools/ffmpeg/ffmpeg.exe'
$projeto = Join-Path $PSScriptRoot 'tests/SonarFrame.Desktop.Tests'
$pasta = Join-Path $PSScriptRoot '.tools/test-artifacts/latencia-reproducao'
New-Item -ItemType Directory -Force $pasta | Out-Null
@{ DataUtc = [DateTime]::UtcNow; Sistema = [Environment]::OSVersion.ToString(); ProcessadoresLogicos = [Environment]::ProcessorCount; Etapa = $Etapa;
    Sha256Preparador = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'src/SonarFrame.Infrastructure/CacheBlocosComposicao.cs')).Hash } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $pasta "ambiente-$Etapa.json")
foreach ($tipo in @('cfr', 'vfr')) {
    $arquivo = Join-Path $pasta "$tipo.mp4"
    if (!(Test-Path $arquivo)) {
        $argumentos = @('-v', 'error', '-nostdin', '-y', '-f', 'lavfi', '-i', 'testsrc2=s=160x90:r=30:d=600', '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=48000:duration=600')
        if ($tipo -eq 'vfr') { $argumentos += @('-vf', 'select=if(lt(mod(t\,30)\,15)\,not(mod(n\,2))\,1)', '-fps_mode', 'vfr') }
        $argumentos += @('-c:v', 'libx264', '-threads', '2', '-g', '60', '-c:a', 'aac', $arquivo)
        & $ffmpeg @argumentos
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar vídeo do benchmark.' }
    }
}
if (!(Test-Path (Join-Path $pasta 'audio.wav'))) {
    & $ffmpeg -v error -nostdin -y -f lavfi -i 'sine=frequency=880:sample_rate=48000:duration=3' (Join-Path $pasta 'audio.wav')
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar áudio.' }
}
if (!(Test-Path (Join-Path $pasta 'imagem.png'))) {
    & $ffmpeg -v error -nostdin -y -f lavfi -i 'color=red:s=80x80' -frames:v 1 (Join-Path $pasta 'imagem.png')
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar imagem.' }
}
$anterior = $env:SONARFRAME_BENCHMARK_LATENCIA
$etapaAnterior = $env:SONARFRAME_BENCHMARK_ETAPA
try {
    $env:SONARFRAME_BENCHMARK_LATENCIA = $pasta
    $env:SONARFRAME_BENCHMARK_ETAPA = $Etapa
    & $dotnet build $projeto -c Release -m:1 --no-incremental
    if ($LASTEXITCODE -ne 0) { throw 'Compilação do benchmark falhou.' }
    & $dotnet test $projeto -c Release -m:1 --no-build --filter FullyQualifiedName~BenchmarkLatencia
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark nativo falhou.' }
} finally {
    $env:SONARFRAME_BENCHMARK_LATENCIA = $anterior
    $env:SONARFRAME_BENCHMARK_ETAPA = $etapaAnterior
}
