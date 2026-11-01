$ErrorActionPreference = 'Stop'
$versao = '9.0.2'
$hashEsperado = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
$url = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-$versao-essentials_build.zip"
$download = Join-Path $PSScriptRoot '.tools/ffmpeg-download'
$destino = Join-Path $PSScriptRoot '.tools/ffmpeg'
New-Item -ItemType Directory -Force $download, $destino | Out-Null
$arquivoCompactado = Join-Path $download 'ffmpeg.zip'
if (!(Test-Path $arquivoCompactado) -or (Get-FileHash -LiteralPath $arquivoCompactado -Algorithm SHA256).Hash -ne $hashEsperado) {
    Invoke-WebRequest -Uri $url -OutFile $arquivoCompactado
}
if ((Get-FileHash -LiteralPath $arquivoCompactado -Algorithm SHA256).Hash -ne $hashEsperado) { throw 'O checksum do FFmpeg não corresponde à versão fixada.' }
Expand-Archive -LiteralPath $arquivoCompactado -DestinationPath (Join-Path $download 'extracted') -Force
$origem = Join-Path $download "extracted/ffmpeg-$versao-essentials_build"
Copy-Item -LiteralPath (Join-Path $origem 'bin/ffprobe.exe') -Destination (Join-Path $destino 'ffprobe.exe')
Copy-Item -LiteralPath (Join-Path $origem 'bin/ffmpeg.exe') -Destination (Join-Path $destino 'ffmpeg.exe')
Copy-Item -LiteralPath (Join-Path $origem 'LICENSE') -Destination (Join-Path $destino 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $origem 'README.txt') -Destination (Join-Path $destino 'README.txt')
Write-Output "FFprobe $versao preparado para uso local em $destino"
