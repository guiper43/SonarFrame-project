param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$versao = ([xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if ($versao -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') { throw 'A versão do instalador deve conter três ou quatro componentes numéricos.' }
if (!$SkipPublish) { & (Join-Path $PSScriptRoot 'run-desktop.ps1') -Publish }
$publicacao = Join-Path $PSScriptRoot 'artifacts/desktop'
foreach ($arquivo in @('SonarFrame.Desktop.exe', 'SonarFrame.Desktop.dll', 'SonarFrame.Desktop.runtimeconfig.json', 'coreclr.dll',
    'tools/ffmpeg.exe', 'tools/ffprobe.exe', 'tools/FFmpeg-LICENSE.txt', 'tools/FFmpeg-README.txt', 'tools/NOTICE.md',
    'libvlc/win-x64/libvlc.dll', 'libvlc/win-x64/libvlccore.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $publicacao $arquivo) -PathType Leaf)) { throw "Publicação incompleta: falta $arquivo. Execute ./run-desktop.ps1 -Publish." }
}
if (!(Get-ChildItem -LiteralPath (Join-Path $publicacao 'libvlc/win-x64/plugins') -Filter '*.dll' -Recurse -File)) { throw 'A publicação não contém os plugins do LibVLC.' }
if (Get-ChildItem -LiteralPath $publicacao -Recurse -File | Where-Object {
    $relativo = [IO.Path]::GetRelativePath($publicacao, $_.FullName)
    $_.Extension -in '.sonarframe', '.recovery', '.mp4', '.mov', '.mp3', '.wav', '.m4a', '.ogg', '.jpg', '.jpeg' -or
        ($_.Extension -eq '.png' -and !$relativo.StartsWith('libvlc\win-x64\lua\http\', [StringComparison]::OrdinalIgnoreCase))
}) {
    throw 'A publicação contém projetos ou mídias. Preserve esses arquivos fora de artifacts/desktop antes de empacotar.'
}
$versaoPublicada = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publicacao 'SonarFrame.Desktop.dll')).ProductVersion.Split('+')[0]
if ($versaoPublicada -ne $versao) { throw "Publicação da versão $versaoPublicada incompatível com a versão $versao." }
$compilador = & (Join-Path $PSScriptRoot 'setup-inno.ps1')
$destino = Join-Path $PSScriptRoot ".tools/installers/$versao"
New-Item -ItemType Directory -Force -Path $destino | Out-Null
& $compilador "/DPublicacao=$publicacao" "/DDestino=$destino" "/DVersao=$versao" (Join-Path $PSScriptRoot 'installer/sonarFrame.iss')
if ($LASTEXITCODE -ne 0) { throw 'A compilação do instalador falhou.' }
$instalador = Join-Path $destino "sonarFrame-$versao-win-x64-setup.exe"
$hash = (Get-FileHash -LiteralPath $instalador -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $destino 'SHA256SUMS.txt') -Value "$hash  $([IO.Path]::GetFileName($instalador))" -Encoding utf8
Write-Output "Instalador preparado em $instalador"
