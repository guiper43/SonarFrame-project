param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
if (!$SkipPublish) { & (Join-Path $PSScriptRoot 'run-desktop.ps1') -Publish }
$publicacao = Join-Path $PSScriptRoot 'artifacts/desktop'
if (!(Test-Path -LiteralPath (Join-Path $publicacao 'SonarFrame.Desktop.exe'))) { throw 'Publique o Desktop antes de testar o instalador.' }
$compilador = & (Join-Path $PSScriptRoot 'setup-inno.ps1')
$id = [Guid]::NewGuid().ToString('N')
$nome = "sonarFrame teste $id"
$tipoProjeto = "sonarFrame.Teste.$id"
$extensao = ".sonarframe-teste-$id"
$mutex = "sonarFrame.Teste.$id.Active"
$raiz = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '.tools/test-artifacts/instalador'))
$pasta = [IO.Path]::GetFullPath((Join-Path $raiz $id))
if (!$pasta.StartsWith($raiz + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Pasta de teste inválida.' }
$instalacao = Join-Path $pasta 'Aplicativo com espaços'
New-Item -ItemType Directory -Force -Path $pasta | Out-Null
$registroExtensao = "HKCU:\Software\Classes\$extensao"
$registroTipo = "HKCU:\Software\Classes\$tipoProjeto"
$registroDesinstalacao = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${tipoProjeto}_is1"
foreach ($chave in @($registroExtensao, $registroTipo, $registroDesinstalacao)) {
    if (Test-Path -LiteralPath $chave) { throw 'A identidade de teste já existe. Nenhuma instalação foi alterada.' }
}
$programas = Join-Path ([Environment]::GetFolderPath('Programs')) $nome
$atalhoDesktop = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) "$nome.lnk"
$desinstalador = Join-Path $instalacao 'unins000.exe'
function Confirmar([bool]$condicao, [string]$mensagem) { if (!$condicao) { throw $mensagem } }
function ExecutarInstalacao([string]$arquivo, [string[]]$parametros) {
    $processo = Start-Process -FilePath $arquivo -ArgumentList $parametros -WindowStyle Hidden -Wait -PassThru
    return $processo.ExitCode
}
function CompilarInstalador([string]$versao) {
    & $compilador '/Q' "/DPublicacao=$publicacao" "/DDestino=$pasta" "/DVersao=$versao" "/DIdentificadorApp=$tipoProjeto" "/DNomeApp=$nome" "/DExtensaoProjeto=$extensao" "/DTipoProjeto=$tipoProjeto" "/DMutexAplicativo=$mutex" (Join-Path $PSScriptRoot 'installer/sonarFrame.iss') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador de teste.' }
    return Join-Path $pasta "sonarFrame-$versao-win-x64-setup.exe"
}
function ConferirPayload {
    foreach ($arquivo in Get-ChildItem -LiteralPath $publicacao -File -Recurse) {
        $relativo = [IO.Path]::GetRelativePath($publicacao, $arquivo.FullName)
        $instalado = Join-Path $instalacao $relativo
        Confirmar (Test-Path -LiteralPath $instalado -PathType Leaf) "Arquivo não instalado: $relativo"
        Confirmar ((Get-FileHash -LiteralPath $instalado).Hash -eq (Get-FileHash -LiteralPath $arquivo.FullName).Hash) "Arquivo instalado divergente: $relativo"
    }
}
try {
    $primeiro = CompilarInstalador '0.0.1'
    $argumentos = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$instalacao`"", '/TASKS=')
    Confirmar ((ExecutarInstalacao $primeiro ($argumentos + "/LOG=`"$(Join-Path $pasta 'sem-associacao.log')`"")) -eq 0) 'Falha na instalação sem associação.'
    Confirmar (!(Test-Path -LiteralPath $registroTipo)) 'A associação foi registrada sem seleção da tarefa.'
    Confirmar (!(Test-Path -LiteralPath $atalhoDesktop)) 'O atalho da Área de Trabalho foi criado sem seleção.'
    Confirmar (Test-Path -LiteralPath (Join-Path $programas "$nome.lnk")) 'Atalho do Menu Iniciar ausente.'
    Confirmar ((ExecutarInstalacao $desinstalador @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')) -eq 0) 'Falha na primeira desinstalação.'
    New-Item -Path $registroExtensao -Force | Out-Null
    Set-Item -LiteralPath $registroExtensao -Value 'Outro.Editor'
    $argumentos = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$instalacao`"", '/TASKS=atalhodesktop,associarprojetos')
    Confirmar ((ExecutarInstalacao $primeiro ($argumentos + "/LOG=`"$(Join-Path $pasta 'instalacao.log')`"")) -eq 0) 'Falha na instalação com associação.'
    ConferirPayload
    Confirmar (Test-Path -LiteralPath $atalhoDesktop) 'Atalho da Área de Trabalho ausente.'
    $shell = New-Object -ComObject WScript.Shell
    try {
        $atalho = $shell.CreateShortcut($atalhoDesktop)
        Confirmar ($atalho.TargetPath -eq (Join-Path $instalacao 'SonarFrame.Desktop.exe')) 'O atalho não aponta para o executável instalado.'
    } finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null }
    Confirmar ((Get-Item -LiteralPath $registroExtensao).GetValue('') -eq 'Outro.Editor') 'A instalação alterou o aplicativo padrão anterior.'
    Confirmar ((Get-Item -LiteralPath "$registroExtensao\OpenWithProgids").GetValueNames() -contains $tipoProjeto) 'Associação OpenWith ausente.'
    $comando = (Get-Item -LiteralPath "$registroTipo\shell\open\command").GetValue('')
    Confirmar ($comando -eq "`"$(Join-Path $instalacao 'SonarFrame.Desktop.exe')`" `"%1`"") 'O comando de abertura não protege caminhos com espaços.'
    $projetoUsuario = Join-Path $instalacao 'projeto do usuário.sonarframe'
    $midiaUsuario = Join-Path $instalacao 'vídeo original.mp4'
    Set-Content -LiteralPath $projetoUsuario -Value 'Projeto de teste preservado'
    Set-Content -LiteralPath $midiaUsuario -Value 'Mídia de teste preservada'
    $hashProjeto = (Get-FileHash -LiteralPath $projetoUsuario).Hash
    $hashMidia = (Get-FileHash -LiteralPath $midiaUsuario).Hash
    $segundo = CompilarInstalador '0.0.2'
    $emUso = [Threading.Mutex]::new($false, $mutex)
    try {
        Confirmar ((ExecutarInstalacao $segundo ($argumentos + "/LOG=`"$(Join-Path $pasta 'em-uso.log')`"")) -ne 0) 'O instalador não bloqueou a atualização com o editor em uso.'
        Confirmar ((Get-ItemProperty -LiteralPath $registroDesinstalacao).DisplayVersion -eq '0.0.1') 'A instalação em uso foi alterada.'
        Confirmar ((ExecutarInstalacao $desinstalador @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')) -ne 0) 'O desinstalador não bloqueou a remoção com o editor em uso.'
        Confirmar (Test-Path -LiteralPath (Join-Path $instalacao 'SonarFrame.Desktop.exe')) 'O executável em uso foi removido.'
    } finally { $emUso.Dispose() }
    Set-Content -LiteralPath (Join-Path $instalacao 'tools/NOTICE.md') -Value 'Conteúdo antigo para validar atualização'
    Confirmar ((ExecutarInstalacao $segundo ($argumentos + "/LOG=`"$(Join-Path $pasta 'atualizacao.log')`"")) -eq 0) 'Falha na atualização.'
    Confirmar ((Get-ItemProperty -LiteralPath $registroDesinstalacao).DisplayVersion -eq '0.0.2') 'Versão instalada não atualizada.'
    ConferirPayload
    Confirmar ((ExecutarInstalacao $desinstalador @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$(Join-Path $pasta 'desinstalacao.log')`"")) -eq 0) 'Falha na desinstalação.'
    Confirmar (!(Test-Path -LiteralPath $registroTipo)) 'Registro do aplicativo permaneceu após desinstalação.'
    if (Test-Path -LiteralPath "$registroExtensao\OpenWithProgids") {
        Confirmar ((Get-Item -LiteralPath "$registroExtensao\OpenWithProgids").GetValueNames() -notcontains $tipoProjeto) 'A associação OpenWith permaneceu após desinstalação.'
    }
    Confirmar (!(Test-Path -LiteralPath $registroDesinstalacao)) 'Entrada de desinstalação permaneceu.'
    Confirmar (!(Test-Path -LiteralPath $atalhoDesktop)) 'Atalho da Área de Trabalho permaneceu.'
    Confirmar (!(Test-Path -LiteralPath (Join-Path $programas "$nome.lnk"))) 'Atalho do Menu Iniciar permaneceu.'
    Confirmar ((Get-Item -LiteralPath $registroExtensao).GetValue('') -eq 'Outro.Editor') 'O aplicativo padrão anterior não foi preservado.'
    Confirmar ((Get-FileHash -LiteralPath $projetoUsuario).Hash -eq $hashProjeto) 'O projeto do usuário foi alterado ou apagado.'
    Confirmar ((Get-FileHash -LiteralPath $midiaUsuario).Hash -eq $hashMidia) 'A mídia do usuário foi alterada ou apagada.'
    Set-Content -LiteralPath (Join-Path $pasta 'resultado.txt') -Value 'Aprovado: instalação opcional, payload, atalhos, associação, bloqueio em uso, atualização e desinstalação sem perda de dados.'
    Write-Output "Validação aprovada. Relatório em $pasta"
} finally {
    if ((Test-Path -LiteralPath $registroDesinstalacao) -and (Test-Path -LiteralPath $desinstalador)) {
        $codigoLimpeza = ExecutarInstalacao $desinstalador @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
        if ($codigoLimpeza -ne 0) { Write-Warning "Não foi possível desinstalar a instância de teste $nome." }
    }
    if (Test-Path -LiteralPath $registroExtensao) { Remove-Item -LiteralPath $registroExtensao -Recurse }
}
