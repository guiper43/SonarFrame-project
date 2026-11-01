# Primeira interface navegável

O SonarFrame tem uma aplicação WPF para Windows com importação real de mídias. Esta entrega mantém a prioridade de navegação por teclado. Não é ainda um editor audiovisual completo.

## Abrir

Depois da publicação local, execute `artifacts/desktop/SonarFrame.Desktop.exe`. A pasta inteira deve acompanhar o executável: ela inclui o runtime .NET, portanto o usuário não precisa instalar o SDK ou runtime separadamente. Ainda não há instalador.

Para desenvolver com o SDK .NET 10:

```powershell
./run-desktop.ps1
```

Para gerar a distribuição local Windows x64 com runtime incluído:

```powershell
./run-desktop.ps1 -Publish
```

O script prepara o FFprobe local quando necessário, verificando o checksum do download. Veja [origem e licença da ferramenta](media-tools.md). Para publicar em outro diretório, informe `-OutputDirectory <caminho>`.

## Importar arquivos reais

1. Crie um projeto e acione **Adicionar** (Alt+1 leva ao botão).
2. Escolha **Fotos e vídeos** ou **Áudios e músicas**. Ctrl+Tab alterna as guias.
3. Acione **Selecionar arquivos**. O seletor nativo aceita múltiplos arquivos usando Ctrl/Shift.
4. Acompanhe a contagem e consulte a lista de resultados. Escape ou **Cancelar importação** interrompe a leitura; os arquivos já importados permanecem na biblioteca. Depois, Escape fecha o diálogo.
5. Ao fechar, o foco retorna a **Adicionar**. Tab leva à biblioteca. Selecione um ou mais itens e acione **Adicionar à linha do tempo**.
6. Fotos pedem duração em segundos antes da inserção (padrão de cinco segundos, editável). A duração aceita frações com o separador decimal configurado no Windows. Cancelar esse diálogo não modifica a composição.
7. Use Ctrl+S para salvar a biblioteca e a composição. O aplicativo não modifica os arquivos originais.

Formatos: MP4, MOV, JPG/JPEG, PNG, MP3, WAV, M4A e OGG/Opus. A leitura verifica streams, codecs reconhecidos e duração; arquivos inválidos são listados sem impedir os demais. Uma importação não insere nada automaticamente na composição. A inserção múltipla é uma única operação de desfazer/refazer, na ordem da biblioteca.

## Primeiro percurso

1. Na tela inicial, use Tab e Shift+Tab para percorrer os botões.
2. Escolha **Explorar projeto de demonstração**. São três vídeos fictícios de dez segundos; nenhum arquivo pessoal é lido.
3. Pressione Alt+1 para a biblioteca e Tab para a lista. As setas percorrem as mídias.
4. Alt+2 leva às propriedades, apresentadas em um campo somente leitura que permite leitura e cópia pelo teclado.
5. Alt+3 leva ao controle temporal; Tab leva à lista de conteúdos. Na lista, as setas selecionam itens sem reproduzir ou deslocar o tempo.
6. Shift+F10 abre o menu de reordenação da lista. Também há botões para mover antes/depois.
7. Alt+4 leva à prévia e Alt+5 aos comandos do projeto.
8. F1 apresenta a ajuda. Escape fecha diálogos e menus.

## Atalhos e foco

| Tecla | Comportamento |
|---|---|
| Tab / Shift+Tab | Próximo / anterior controle |
| Alt+1 a Alt+5 | Biblioteca, propriedades, tempo, prévia, comandos |
| F6 / Shift+F6 | Próxima / anterior região |
| Ctrl+S | Salvar projeto |
| F1 | Ajuda dos atalhos |
| Shift+F10 | Menu contextual da lista de conteúdos |
| Ctrl+Tab | Alternar guias do diálogo Adicionar |
| Escape | Fechar diálogo ou menu |

Somente no controle **Navegação temporal**, setas avançam/retrocedem um quadro, Ctrl+setas um segundo, Page Up/Down dez segundos e Home/End vão aos extremos. Shift estende a seleção. Campos de texto e listas conservam seu comportamento nativo. O controle temporal informa a posição por UI Automation e pela região de mensagens.

## O que já funciona

- Novo projeto, com nome e composição vazia.
- Importação real com seleção múltipla, progresso consultável, cancelamento e resultados por arquivo.
- Abrir e salvar arquivos SQLite do núcleo, incluindo a demonstração.
- Explorar biblioteca e composição, consultar propriedades, inserir itens da biblioteca, reordenar e desfazer/refazer.
- Navegar pelo tempo e selecionar intervalos no modelo.
- Consultar mensagens recentes, menus, ajuda e as três guias de Adicionar.
- Confirmação antes de abandonar alterações não salvas; cancelamento de diálogos.

## Limites desta etapa

- A guia **Legendas e efeitos** continua informativa. SRT e efeitos ainda não estão disponíveis.
- **Meus projetos** abre o seletor de arquivos nesta versão. O catálogo por diretório ainda será implementado.
- **Configurações** é uma tela informativa; não altera preferências.
- Prévia e exportação informam que ainda não estão disponíveis, sem simular reprodução ou exportação.
- Autosave e recuperação existem na camada de aplicação, mas ainda não estão conectados ao ciclo de vida da interface. Nesta versão, use Ctrl+S.
- A integração atual usa FFprobe somente para ler metadados. Não há reprodução/renderização FFmpeg, edição visual de legendas, efeitos ou serviços de IA.
- A taxa média e nominal do vídeo permitem detectar possíveis arquivos VFR, sem garantir precisão quadro a quadro nesses arquivos. Quando indicada taxa variável ou ausente, use Ctrl+setas e Page Up/Down; navegação por timestamps ainda será integrada. Imagens e áudios também podem ser navegados por segundos.
- A validação real com NVDA continua pendente. Controles nativos, nomes UI Automation e notificações são a base, não uma certificação de acessibilidade.

## Testes e validação

A interface está em `src/SonarFrame.Desktop` e opera sobre `EditorSession`, preservando os arquivos de mídia originais.

A suíte automatizada inclui testes de integração para os nove formatos/extensões aceitos, falha isolada de arquivo corrompido, precisão de metadados, inserção múltipla atômica, cancelamento, timeout e persistência SQLite. Sem o FFmpeg/FFprobe preparado localmente, os dois testes de mídia real são ignorados; execute `./setup-media.ps1` antes de `./build.ps1` para habilitá-los localmente (a CI executa ambos por padrão).
