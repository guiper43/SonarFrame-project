$ErrorActionPreference = 'Stop'
# Build linked by ffmpeg.org/download.html. Pinned archive and SHA-256 keep local setup reproducible.
$version = '9.0.2'
$expectedHash = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
$url = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-$version-essentials_build.zip"
$download = Join-Path $PSScriptRoot '.tools/ffmpeg-download'
$destination = Join-Path $PSScriptRoot '.tools/ffmpeg'
New-Item -ItemType Directory -Force $download, $destination | Out-Null
$archive = Join-Path $download 'ffmpeg.zip'
if (!(Test-Path $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
    Invoke-WebRequest -Uri $url -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) { throw 'O checksum do FFmpeg não corresponde à versão fixada.' }
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $download 'extracted') -Force
$source = Join-Path $download "extracted/ffmpeg-$version-essentials_build"
Copy-Item -LiteralPath (Join-Path $source 'bin/ffprobe.exe') -Destination (Join-Path $destination 'ffprobe.exe')
Copy-Item -LiteralPath (Join-Path $source 'bin/ffmpeg.exe') -Destination (Join-Path $destination 'ffmpeg.exe')
Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $destination 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $source 'README.txt') -Destination (Join-Path $destination 'README.txt')
Write-Output "FFprobe $version preparado para uso local em $destination"
