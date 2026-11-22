$ErrorActionPreference = 'Stop'
$versao = '7.1.1'
$hashEsperado = '8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0'
$pasta = Join-Path $PSScriptRoot ".tools/inno-setup/$versao"
$compilador = Join-Path $pasta 'ISCC.exe'
if (Test-Path -LiteralPath $compilador) {
    $versaoCompilador = & $compilador --version
    if ($LASTEXITCODE -eq 0 -and "$versaoCompilador".Trim() -eq $versao) {
        Write-Output $compilador
        return
    }
    throw 'O compilador local não corresponde à versão fixada do Inno Setup.'
}
$download = Join-Path $PSScriptRoot '.tools/inno-download'
New-Item -ItemType Directory -Force -Path $download | Out-Null
$arquivo = Join-Path $download "innosetup-$versao-x64.exe"
if (!(Test-Path -LiteralPath $arquivo) -or (Get-FileHash -LiteralPath $arquivo -Algorithm SHA256).Hash -ne $hashEsperado) {
    Invoke-WebRequest -Uri "https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-$versao-x64.exe" -OutFile $arquivo
}
if ((Get-FileHash -LiteralPath $arquivo -Algorithm SHA256).Hash -ne $hashEsperado) {
    throw 'O checksum do Inno Setup não corresponde ao release oficial fixado.'
}
$processo = Start-Process -FilePath $arquivo -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/PORTABLE=1', '/NOICONS', "/DIR=`"$pasta`"") -WindowStyle Hidden -Wait -PassThru
if ($processo.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compilador)) { throw 'Não foi possível preparar o compilador portátil Inno Setup.' }
Write-Output $compilador
