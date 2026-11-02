param([switch]$Publish, [string]$OutputDirectory = 'artifacts/desktop')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools/nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $PSScriptRoot '.tools/http-cache'
$env:NUGET_SCRATCH = Join-Path $PSScriptRoot '.tools/scratch'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$local = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path $local) { $local } else { 'dotnet' }
Push-Location $PSScriptRoot
try {
    if (!(Test-Path (Join-Path $PSScriptRoot '.tools/ffmpeg/ffprobe.exe'))) { & (Join-Path $PSScriptRoot 'setup-media.ps1') }
    if ($Publish) {
        $destino = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts/desktop'))
        $solicitado = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $OutputDirectory)) }
        if ($solicitado -ne $destino) { throw 'Publique sempre em artifacts/desktop. A versão anterior será substituída.' }
        if (Test-Path -LiteralPath $destino) {
            foreach ($arquivo in Get-ChildItem -LiteralPath $destino -File -Recurse | Where-Object Extension -In '.exe', '.dll') {
                try { $teste = [IO.File]::Open($arquivo.FullName, 'Open', 'ReadWrite', 'None'); $teste.Dispose() }
                catch { throw 'Feche o sonarFrame antes de publicar novamente. A versão anterior foi preservada.' }
            }
        }
        & $dotnet publish src/SonarFrame.Desktop/SonarFrame.Desktop.csproj -c Release -r win-x64 --self-contained true -o $destino -m:1
    } else {
        & $dotnet run --project src/SonarFrame.Desktop/SonarFrame.Desktop.csproj -c Release
    }
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível preparar a interface.' }
} finally { Pop-Location }
