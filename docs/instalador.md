# Instalador Windows

O instalador usa o assistente padrão do Inno Setup em português brasileiro, com controles nativos e navegação por teclado. Instala por usuário em `%LOCALAPPDATA%\Programs\sonarFrame`, sem solicitar privilégios administrativos. O pacote inclui o runtime .NET, FFmpeg/FFprobe e LibVLC publicados pelo projeto.

## Gerar e validar

```powershell
./setup-media.ps1
./build.ps1
./gerar-instalador.ps1
./testar-instalador.ps1 -SkipPublish
```

`gerar-instalador.ps1` executa `./run-desktop.ps1 -Publish`, valida as dependências e gera `.tools/installers/0.1.0/sonarFrame-0.1.0-win-x64-setup.exe` e `SHA256SUMS.txt`. A versão vem de `Directory.Build.props`. `-SkipPublish` reutiliza a publicação em `artifacts/desktop`. Projetos e mídias encontrados nessa publicação impedem o empacotamento; mova seus arquivos para um local seguro antes de publicar.

`setup-inno.ps1` baixa o compilador Inno Setup 7.1.1 x64, verifica SHA-256 e o extrai em modo portátil dentro de `.tools`. Internet é necessária na preparação, não na instalação do aplicativo. O SDK .NET 10 é necessário para compilar.

O teste cria uma identidade exclusiva para instalação, atalhos e registro, sem usar a associação real `.sonarframe`. Verifica instalação com e sem tarefas opcionais, hashes de todos os arquivos, caminhos com espaços, preservação do aplicativo padrão anterior, bloqueio de atualização em uso, atualização e desinstalação preservando arquivos de usuário. Relatórios e amostras ficam em `.tools/test-artifacts/instalador`.

## Uso

O Menu Iniciar recebe atalhos para abrir e desinstalar. O atalho na Área de Trabalho pode ser desmarcado. A associação de projetos é opcional e desmarcada inicialmente: registra sonarFrame em **Abrir com**, preservando o aplicativo padrão já escolhido no Windows. Para torná-lo padrão, use **Abrir com → Escolher outro aplicativo** no Explorer.

O aplicativo recebe um caminho `.sonarframe`, inclusive com espaços e acentos, e usa o fluxo normal de abertura. A recuperação continua exigindo escolha explícita do usuário. Arquivos ausentes, inválidos ou corrompidos são anunciados na interface.

Feche sonarFrame antes de atualizar ou desinstalar. O instalador não encerra processos automaticamente. A desinstalação remove arquivos instalados, atalhos e registros próprios; preserva projetos, mídias e preferências do usuário. Desmarcar a tarefa de associação durante uma atualização não remove um registro instalado anteriormente; a desinstalação o remove.

`release.ps1` inclui o instalador e seu checksum junto da distribuição ZIP. O pacote local não tem assinatura digital; assinatura de distribuição não faz parte desta entrega.

## Homologação manual

Em Windows x64 limpo, sem SDK: instalar, abrir, importar mídia e reproduzir; confirmar que as dependências incluídas bastam. Com NVDA ativo e somente teclado, percorrer o assistente com Tab, Shift+Tab, setas, Espaço e Enter; verificar leitura dos nomes, estado das opções, foco e mensagens. Validar cancelamento, atualização com editor aberto, associação pelo Explorer, recuperação de projeto e desinstalação. Esses critérios exigem homologação manual e não são substituídos pelo teste silencioso.

## Referências

- [Inno Setup 7.1.1](https://github.com/jrsoftware/issrc/releases/tag/is-7_1_1)
- [AppMutex](https://jrsoftware.org/inno-setup/documentation/topic_setup_appmutex.htm)
- [ChangesAssociations](https://jrsoftware.org/inno-setup/documentation/topic_setup_changesassociations.htm)
