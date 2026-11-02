param([switch]$Benchmark, [switch]$BenchmarkBuscas)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools/nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $PSScriptRoot '.tools/http-cache'
$env:NUGET_SCRATCH = Join-Path $PSScriptRoot '.tools/scratch'
$local = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path $local) { $local } else { 'dotnet' }
$projeto = Join-Path $PSScriptRoot 'samples/SonarFrame.ValidacaoReproducao/SonarFrame.ValidacaoReproducao.csproj'
& $dotnet build $projeto -c Release -m:1
if ($LASTEXITCODE -ne 0) { throw 'Compilação da validação falhou.' }
$argumentos = @($PSScriptRoot)
if ($Benchmark) { $argumentos += '--benchmark' }
if ($BenchmarkBuscas) { $argumentos += '--benchmark-buscas' }
& $dotnet (Join-Path $PSScriptRoot 'samples/SonarFrame.ValidacaoReproducao/bin/Release/net10.0/SonarFrame.ValidacaoReproducao.dll') @argumentos
if ($LASTEXITCODE -ne 0) { throw 'Validação falhou. Consulte .tools/test-artifacts/validacao-reproducao/falha.txt.' }
