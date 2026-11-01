param([string]$ExpectedTag)
$ErrorActionPreference = 'Stop'
$versao = ([xml](Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if ($ExpectedTag -and $ExpectedTag -ne "v$versao") { throw "Tag $ExpectedTag incompatível com a versão $versao." }
Push-Location $PSScriptRoot
try {
    & ./setup-media.ps1
    & ./build.ps1 -Pack
    $destino = Join-Path $PSScriptRoot "artifacts/releases/$versao"
    & ./run-desktop.ps1 -Publish -OutputDirectory (Join-Path $destino 'win-x64')
    $pacotes = Join-Path $destino 'packages'
    New-Item -ItemType Directory -Force $pacotes | Out-Null
    foreach ($nome in @('SonarFrame.Core', 'SonarFrame.Application', 'SonarFrame.Infrastructure')) {
        Copy-Item -LiteralPath "artifacts/packages/$nome.$versao.nupkg" -Destination $pacotes
    }
    Copy-Item -LiteralPath 'CHANGELOG.md', 'docs/release.md' -Destination $destino
    Compress-Archive -Path (Join-Path $destino 'win-x64/*') -DestinationPath (Join-Path $destino "sonarFrame-$versao-win-x64.zip") -Force
    $arquivos = @((Join-Path $destino "sonarFrame-$versao-win-x64.zip")) + @(Get-ChildItem -LiteralPath $pacotes -Filter '*.nupkg' | ForEach-Object FullName)
    $linhas = foreach ($arquivo in $arquivos) { $hash = Get-FileHash -LiteralPath $arquivo -Algorithm SHA256; "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetRelativePath($destino, $arquivo))" }
    $linhas | Set-Content -LiteralPath (Join-Path $destino 'SHA256SUMS.txt') -Encoding utf8
    Write-Output "Artefatos preparados em $destino"
} finally { Pop-Location }
