# Interface desktop

O SonarFrame oferece montagem não destrutiva de projetos em uma interface WPF para Windows, operável por teclado. Este guia inclui a prévia da biblioteca e a reprodução da sequência principal em desenvolvimento após a versão 0.1.0. O escopo publicado da versão anterior está em [release.md](release.md).

## Abrir

Depois da publicação local, executar `artifacts/desktop/SonarFrame.Desktop.exe`. A pasta inteira deve acompanhar o executável: ela inclui o runtime .NET, portanto o usuário não precisa instalar o SDK ou runtime separadamente. Ainda não há instalador.

Para desenvolver com o SDK .NET 10:

```powershell
./run-desktop.ps1
```

Para gerar a distribuição local Windows x64 com runtime incluído:

```powershell
./run-desktop.ps1 -Publish
```

O script prepara o FFprobe local quando necessário, verificando o checksum do download. Veja [origem e licença da ferramenta](media-tools.md). Toda publicação para validação sobrescreve `artifacts/desktop`. Feche o aplicativo antes de publicar. Não criar cópias por feature; amostras e relatórios ficam em `.tools/test-artifacts`.

## Importar arquivos reais

1. Crie um projeto e acione **Adicionar** (Alt+1 leva ao botão).
2. Escolha **Fotos e vídeos** ou **Áudios e músicas**. Ctrl+Tab alterna as guias.
3. Acione **Selecionar arquivos**. O seletor nativo aceita múltiplos arquivos usando Ctrl/Shift.
4. Acompanhe a contagem e consulte a lista de resultados. Escape ou **Cancelar importação** interrompe a leitura; os arquivos já importados permanecem na biblioteca. Depois, Escape fecha o diálogo.
5. Ao concluir, o foco fica em **Fechar**. Ao fechar, retorna à lista de arquivos importados. Selecione um ou mais itens e acione **Adicionar à linha do tempo**.
6. Fotos pedem duração em segundos antes da inserção (padrão de cinco segundos, editável). A duração aceita frações com o separador decimal configurado no Windows. Cancelar esse diálogo não modifica a composição.
7. Use Ctrl+S para salvar a biblioteca e a composição. O aplicativo não modifica os arquivos originais.

Formatos: MP4, MOV, JPG/JPEG, PNG, MP3, WAV, M4A e OGG/Opus. A leitura verifica fluxos, codecs reconhecidos e duração; arquivos inválidos são listados sem impedir os demais. Uma importação não insere nada automaticamente na composição. A inserção múltipla é uma única operação de desfazer/refazer, na ordem da biblioteca.

## Conferir uma mídia antes de inserir

1. Selecione uma única mídia na biblioteca e acione **Abrir prévia da mídia selecionada**. Selecionar itens por si só não inicia nem troca a reprodução. Com vários itens selecionados, o comando fica indisponível.
2. O foco vai para **Consultar prévia** na região 4. A primeira abertura de áudio/vídeo inicializa o motor em segundo plano; a interface permanece utilizável. **Cancelar abertura da prévia** interrompe a abertura, mesmo enquanto o motor inicializa.
3. Áudio e vídeo começam do início do arquivo original. Use **Pausar/Reproduzir**, **Parar** (pausa no início), ou informe segundos em **Posição na mídia** e acione **Buscar e pausar**. Buscar não retoma a reprodução; a posição é anunciada após o comando. A busca é temporal, sem garantia de quadro exato.
4. **Consultar prévia** informa nome, estado, posição e duração. O tempo visual é atualizado sem anúncios contínuos. Alt+4 leva ao botão Reproduzir/Pausar quando disponível; caso contrário, à consulta. Espaço mantém a ativação nativa dos botões e o comportamento normal em listas e campos.
5. Imagens ficam estáticas, sem duração na biblioteca. Reprodução, parada e busca ficam desabilitadas; voltam ao abrir áudio/vídeo. A imagem inserida na composição continua com a duração definida na inserção.

A posição dessa prévia é independente do cursor da composição. Não altera o projeto nem cria entradas de desfazer. Cortes, camadas e volumes da montagem não são aplicados ao arquivo original da biblioteca; para conferir a sequência principal, use os comandos de reprodução na linha do tempo.

Ao abrir outra mídia, a anterior é liberada antes de iniciar a nova. O término é anunciado uma vez; Reproduzir reinicia do início. Editar, desfazer ou refazer pausa a prévia; religar uma mídia encerra a prévia. Sair do projeto ou fechar a janela cancela a abertura e libera os recursos. Salvar não interrompe a reprodução. Se a inicialização nativa ainda estiver em andamento, fechar pode aguardar sua conclusão com a interface responsiva.

Arquivos ausentes ou ilegíveis geram mensagem acessível. Use **Localizar arquivo** quando necessário e abra a prévia novamente. O leitor de telas foi validado pelo usuário no laboratório; a integração ao editor exige novo percurso manual antes da distribuição estável.

## Editar a composição

A reprodução começa pelo cursor ou pelo intervalo selecionado, depois de preparar uma reserva inicial. O restante é preparado durante a reprodução. Segmentos equivalentes são reaproveitados após edições, com caches separados de áudio e vídeo; alterar volume preserva o vídeo. A primeira preparação pode levar mais tempo. A implementação atual copia vídeo e áudio PCM para uma entrada contínua, sem codificação AAC integral. Consulte [a implementação e sua validação](plano-entrada-continua.md).

Se a preparação não acompanhar a reprodução, o vídeo pausa na posição atual e informa “Carregando vídeo. A reprodução continuará desta posição.” Ao recompor a reserva, retoma automaticamente e anuncia “Reproduzindo”, preservando o foco. Acionar Pausar durante essa espera impede a retomada automática. Cancelar encerra a preparação; navegar ou editar passa a usar a nova posição ou composição. A reprodução de uma seleção termina no limite selecionado.

O arquivo `%LOCALAPPDATA%/sonarFrame/diagnostico-reproducao.json` registra a abertura e observações da saída nativa, sem anúncios ao NVDA. As medições anteriores da montagem completa estão em [investigação e benchmarks](resultado-recomposicao-incremental.md); não representam a latência da entrada contínua.

- **Inserir antes do clipe selecionado** usa os itens selecionados na biblioteca e a posição do clipe selecionado na sequência.
- **Dividir no cursor** separa o clipe principal que contém a posição temporal atual. O cursor precisa estar dentro do clipe, não em uma de suas extremidades.
- **Excluir intervalo selecionado** remove a seleção temporal e fecha a lacuna. Camadas que cruzam o intervalo são recortadas, e as posteriores são deslocadas.
- **Ajustar volume** nas propriedades altera o áudio do conteúdo selecionado entre 0 e 200%. Imagens não oferecem esse controle.
- **Adicionar como camada** insere uma mídia da biblioteca em um intervalo independente. O diálogo permite definir início na composição, início na origem e duração. A camada precisa caber na composição e na mídia de origem.
- **Editar intervalo da camada** e **Remover camada** atuam somente na camada selecionada e não encurtam a sequência principal.
- Todas essas operações permitem desfazer/refazer. A lista identifica nome, tipo de mídia, ordem na lista, sequência principal ou camada, início, fim e duração. Mover para a posição atual não cria uma edição no histórico.

Alt+3 leva diretamente à lista da timeline. Enter posiciona no início do item selecionado. Shift+F10, Aplicações ou botão direito abre o menu com Consultar posição e intervalo, Dividir no cursor, Excluir intervalo, Remover conteúdo, reordenação e histórico. Escape retorna à timeline. A consulta informa os limites e a duração da seleção com milissegundos. Selecionar um item com Cima/Baixo não desloca o cursor nem altera o intervalo temporal. Dividir atua no clipe principal sob o cursor, que pode ser diferente do item selecionado.

Na própria timeline, Esquerda/Direita move um quadro, Ctrl+Esquerda/Direita move um segundo e Page Up/Down desloca dez segundos; Shift estende a seleção. Delete após seleção temporal exclui o trecho. Cima/Baixo ativa o contexto de item: Delete abre o menu na ação de remover o conteúdo da composição, sem removê-lo imediatamente, mesmo havendo um intervalo anterior selecionado. A divisão confirma o ponto do corte e as duas partes. A exclusão confirma o intervalo removido, a nova posição e a duração da composição. Desfazer/refazer informa o estado restaurado; Ctrl+Z/Y sem histórico informa que a operação está indisponível.

As propriedades acompanham o conteúdo selecionado na biblioteca ou na composição. Selecionar uma mídia da biblioteca não aplica alterações ao clipe anteriormente selecionado.

## Projetos, salvamento e recuperação

Novos projetos recebem um caminho único na pasta configurada, inicialmente `Documentos/sonarFrame`. **Meus projetos** lista arquivos válidos nessa pasta por nome e última modificação, incluindo projetos que têm recuperação disponível. **Abrir projeto existente** permite abrir arquivos em outras pastas.

**Configurações** permite escolher a pasta para novos projetos. A mudança não move nem exclui arquivos existentes. As preferências são persistidas em `%LOCALAPPDATA%/sonarFrame/settings.json`.

**Salvar** e Ctrl+S gravam o estado SQLite; **Projeto → Salvar como** permite escolher outro destino. Havendo alterações, o salvamento automático grava o projeto após dois minutos, sem deslocar o foco. Novos projetos também recebem salvamento automático antes do primeiro salvamento manual. Ao fechar ou trocar de projeto, alterações pendentes oferecem as opções salvar, descartar ou cancelar.

Após encerramento inesperado, a abertura oferece recuperar o último snapshot disponível ou usar a versão principal. A recuperação nunca é aplicada sem escolha. Se o arquivo principal estiver inválido, a recuperação aceita passa a ter um novo destino, preservando o arquivo inválido. Alterações ainda apenas na memória, posteriores ao último snapshot, não podem ser recuperadas.

Mídias ausentes são informadas ao abrir. Selecione o item na biblioteca e acione **Localizar arquivo**. A atualização valida tipo, duração e taxa de quadros e preserva as operações de edição. Arquivos incompatíveis são rejeitados.

## Primeiro percurso

1. Na tela inicial, use Tab e Shift+Tab para percorrer os botões.
2. Escolha **Explorar projeto de demonstração**. São três vídeos fictícios de dez segundos; nenhum arquivo pessoal é lido.
3. Pressione Alt+1 para a biblioteca e Tab para a lista. As setas percorrem as mídias.
4. Alt+2 leva às propriedades, apresentadas em um campo somente leitura que permite leitura e cópia pelo teclado.
5. Alt+3 leva à timeline. Cima/Baixo seleciona itens e Esquerda/Direita desloca o cursor temporal. Shift seleciona um intervalo, que pode ser excluído com Delete nessa mesma lista.
6. Shift+F10, Aplicações ou botão direito abre o menu de edição, incluindo reordenação. Escape fecha o menu e retorna à timeline.
7. Alt+4 leva à prévia e Alt+5 aos comandos do projeto.
8. F1 apresenta a ajuda. Escape fecha diálogos e menus.

## Atalhos e foco

Na tela inicial, as ações formam uma única lista vertical: Novo projeto, Meus projetos, Abrir projeto existente, Configurações, Explorar demonstração e Ajuda. Cima/Baixo ou Tab/Shift+Tab percorrem os botões; Enter/Espaço ativa a ação. Em Ajuda, Enter/Espaço expande ou recolhe as opções **Atalhos de teclado** e **Sobre o sonarFrame** na própria lista; as setas não expandem. Cima retorna das opções para Ajuda e de Ajuda para a demonstração sem exigir Escape, mesmo com as opções expandidas. O estado expandido/recolhido é informado ao leitor de telas. A barra horizontal Projeto/Ajuda aparece somente no editor. F1 continua abrindo diretamente os atalhos.

Nos menus Projeto e Ajuda, Enter ou Espaço abre/recolhe o cabeçalho. Esquerda/Direita troca o menu mantendo-o fechado; Cima/Baixo só percorre opções de menus já abertos. Escape fecha. Os controles continuam expostos como menus, com as ações nativas nas opções.

| Tecla | Comportamento |
|---|---|
| Tab / Shift+Tab | Percorrer controles e ações de cada área, depois seguir para a próxima |
| Alt+1 a Alt+5 | Biblioteca, propriedades, tempo, prévia, comandos |
| F6 / Shift+F6 | Próxima / anterior região |
| Ctrl+S | Salvar projeto |
| Ctrl+E | Exportar vídeo em MP4 |
| Ctrl+Z / Ctrl+Y | Desfazer / refazer edição fora de campos de texto |
| Ctrl+B | Dividir no cursor fora de campos de texto |
| Delete | Na biblioteca, remover mídias; na timeline, excluir a seleção temporal ou, após Cima/Baixo, abrir a ação de remover o item |
| F1 | Ajuda dos atalhos |
| Shift+F10 | Menu contextual da lista de conteúdos |
| Ctrl+Tab | Alternar guias do diálogo Adicionar |
| Escape | Fechar diálogo ou menu |

Na lista da **timeline**, Esquerda/Direita avança/retrocede um quadro, Ctrl+setas um segundo, Page Up/Down dez segundos e Home/End vão aos extremos. Shift estende a seleção. Campos de texto e a biblioteca conservam seu comportamento nativo. A posição fica visível e as mudanças manuais são anunciadas; o menu permite consultar posição e seleção.

## O que já funciona

- Novo projeto, com nome e composição vazia.
- Importação real com seleção múltipla, progresso consultável, cancelamento e resultados por arquivo.
- Abrir e salvar arquivos SQLite do núcleo, incluindo a demonstração.
- Salvamento automático, recuperação, Salvar como e catálogo de projetos.
- Explorar biblioteca e composição, consultar propriedades, inserir itens da biblioteca, reordenar e desfazer/refazer.
- Dividir clipes, excluir intervalos, ajustar volume e editar camadas independentes.
- Navegar pelo tempo e selecionar intervalos no modelo.
- Consultar mensagens recentes, menus, ajuda e as três guias de Adicionar.
- Exportar a composição final em MP4 (720p ou 1080p, horizontal 16:9 ou vertical 9:16, com opção de permitir bordas ou preencher a tela), com progresso consultável, cancelamento e ações pós-exportação para abrir o vídeo ou a pasta.
- Confirmação antes de abandonar alterações não salvas; cancelamento de diálogos.

## Exportar a composição

Use Ctrl+E ou **Exportar vídeo** na região 5. Escolha a orientação, a resolução, o enquadramento, o nome, para onde enviar o vídeo (**Pasta Downloads (padrão)** ou **Outra pasta selecionada**) e a pasta do MP4. A orientação não vem selecionada; 1080p e a pasta Downloads são os padrões iniciais. A escolha de pasta do usuário é persistida nas preferências (`%LOCALAPPDATA%/sonarFrame/settings.json`). Não é permitido misturar arquivos do projeto (`.sonarframe`) com vídeos exportados na mesma pasta. Substituir um arquivo existente exige confirmação.

Downloads acompanha a localização configurada no Windows, inclusive quando movida para outro disco. Configurações antigas preservam a pasta dos projetos; se ela já for Downloads, a sugestão de exportação será a subpasta `sonarFrame-exportacoes`, criada apenas ao confirmar a exportação.

Durante a exportação, **Consultar progresso da exportação** informa o estado, o percentual e a estimativa de tempo quando disponível. Escape ou **Cancelar exportação** interrompe a operação. A edição e a troca de projeto ficam bloqueadas, mas o salvamento automático e **Salvar projeto**/Ctrl+S continuam disponíveis. Salvar não muda o foco nem interrompe a exportação.

O arquivo é produzido na pasta escolhida e só substitui o destino após a verificação final. Cancelamento ou falha preserva o destino anterior. Ao concluir, use **Abrir vídeo exportado** ou **Abrir pasta do vídeo**. A exportação não acumula cópias intermediárias dos trechos; é necessário espaço para o novo MP4 na pasta de destino.

## Limites desta etapa

- A guia **Legendas e efeitos** continua informativa. SRT e efeitos ainda não estão disponíveis.
- Prévia da biblioteca, reprodução da composição com camadas e exportação em MP4 disponíveis; exportação em 4K permanece fora desta versão.
- FFprobe lê metadados, FFmpeg prepara a prévia temporária da composição e exporta o arquivo MP4 final, e LibVLCSharp reproduz o arquivo original ou a prévia preparada. Não há edição visual de legendas, efeitos ou serviços de IA.
- Na composição, Esquerda/Direita usa os timestamps reais dos vídeos, inclusive VFR e taxas acima de 30 fps. Imagens e áudios usam passos de 1/30 s; Ctrl+setas e Page Up/Down continuam disponíveis. A primeira consulta pode precisar ler o índice de quadros; use Cancelar preparação ou busca para interromper.
- A validação real com NVDA continua pendente. Controles nativos, nomes UI Automation e notificações são a base, não uma certificação de acessibilidade.

## Testes e validação

A interface está em `src/SonarFrame.Desktop` e opera sobre `SessaoEdicao`, preservando os arquivos de mídia originais.

A suíte automatizada inclui testes de domínio, persistência, catálogo, recuperação e ligação de controles WPF ao core, além de integração com os nove formatos/extensões aceitos. Os testes WPF executam sem exibir janelas. Sem FFmpeg/FFprobe preparado localmente, os testes de mídia real são ignorados; executar `./setup-media.ps1` antes de `./build.ps1` para habilitá-los (a CI executa ambos por padrão).

Os testes da prévia usam motores falsos para estados, cancelamento, eventos atrasados e independência da montagem. `./testar-previa-desktop.ps1` executa separadamente o adaptador nativo com MP4, WAV e PNG locais, em janela fora da tela; pode emitir áudio de teste. O script prepara as amostras do laboratório quando ausentes. A suíte normal ignora apenas esse teste nativo optativo, que depende da sessão gráfica e dos dispositivos da máquina.

Validação local do incremento 1 em 01/11/2026: solução compilada sem avisos/erros e 69 testes aprovados, sem ignorados, executando `build.ps1` com `SONARFRAME_MIDIAS_PREVIA` apontando para as amostras. Os testes WPF são serializados porque o carregamento de recursos XAML usa estado compartilhado; cada teste aguarda o descarte da prévia antes de concluir. O artefato de desenvolvimento foi preparado em `artifacts/desktop` com `./run-desktop.ps1 -Publish`.

No editor, Tab/Shift+Tab percorre os controles e ações de cada área antes de passar à próxima, mantendo também acesso ao menu e às mensagens recentes. Cima/Baixo percorre os controles habilitados na ordem de leitura, sem sair da área. Na biblioteca e na timeline, as setas permanecem na lista mesmo nos extremos; Tab acessa as ações. Ctrl/Shift com setas mantém a seleção múltipla da biblioteca. Esquerda/Direita na timeline move o tempo (Ctrl: um segundo; Shift: selecionar intervalo); nos campos de texto mantém a edição do texto. Alt+1 a Alt+5 e F6 continuam disponíveis.

Ao trocar de área com Tab/Shift+Tab, a prévia recebe foco em Reproduzir/Pausar quando disponível; caso contrário, em Consultar prévia. Cima/Baixo acessa as demais ações. Os itens da biblioteca e da timeline apresentam o nome primeiro também ao leitor de telas, sem expor o identificador interno.

A biblioteca e a prévia ficam juntas, lado a lado. Na lista, Cima/Baixo seleciona os arquivos; Enter abre a prévia do arquivo selecionado. Tab sai da lista diretamente para Abrir prévia e depois Adicionar à linha do tempo, sem percorrer os demais arquivos. Shift+Tab retorna à lista preservando a seleção. A prévia é explícita e não insere o arquivo na composição. F6 e Alt+1 a Alt+5 continuam disponíveis para acesso direto às áreas.

Na biblioteca, Cima/Baixo permanece na lista, inclusive nos extremos. Tab acessa as ações. Delete remove as mídias selecionadas da biblioteca, preserva os arquivos originais e permite Ctrl+Z. Mídias usadas na composição precisam ter seus clipes e camadas removidos antes; a remoção múltipla é atômica. No diálogo Adicionar, o foco inicial anuncia Selecione o tipo da importação; as quatro setas selecionam a guia automaticamente e Tab acessa Selecionar arquivos. Ao concluir, o foco fica em Fechar; ao fechar, retorna à lista de importados.

A timeline lista clipes, trechos e camadas por início no vídeo; em inícios iguais, a sequência principal vem antes das camadas, mantendo a ordem de inserção entre camadas simultâneas. Cada rótulo informa nome, início no vídeo, fim, duração e tipo. A ordenação de exibição não altera a montagem. Tab sai do elemento selecionado para suas ações, mantendo a seleção; Shift+Tab retorna. Mover antes/depois usa a posição do clipe na sequência principal, independente das camadas intercaladas na lista.

## Reprodução da sequência principal

Na timeline, Espaço ativa ou pausa a montagem. As ações visíveis incluem **Parar composição**, **Reproduzir intervalo selecionado** e **Cancelar preparação ou busca**. Há um único botão **Reproduzir/Pausar** na área do vídeo, compartilhado com a prévia da biblioteca. A primeira reprodução prepara uma mídia temporária local; começa no cursor e reinicia do início quando o cursor está no fim. Pausar mantém a posição, Parar retorna ao início. A prévia visual aparece na área compartilhada de prévia. Biblioteca e composição não tocam simultaneamente.

A composição respeita ordem, recortes na origem, duração das fotos e volumes individuais. Camadas entram e saem em seus intervalos; a última camada visual adicionada aparece acima. As imagens são centralizadas sem corte, preservando proporção e transparência. O quadro segue a proporção da primeira mídia visual da sequência principal, com lado maior de 640 pixels; somente áudio usa fundo preto de 640×360. A prévia usa 30 quadros/s e áudio estéreo de 48 kHz, para conferir a montagem. Consultar prévia informa também os nomes das camadas ativas, sem anúncios contínuos.

O áudio da sequência e das camadas é somado com os volumes escolhidos, sem normalização automática nem limitador. A soma pode saturar e distorcer; reduza os volumes quando necessário. Alterar o volume de uma camada não deve reduzir automaticamente o volume das demais.

Pausar, parar ou navegar no tempo monta a imagem diretamente dos arquivos originais e das camadas ativas. É possível navegar antes da primeira reprodução, sem preparar o vídeo completo. Vídeos usam os quadros identificados por seus timestamps reais, inclusive entre os quadros de uma prévia de 30 fps. No fim, o cursor conserva a duração e a imagem mostra o último quadro válido. Buscas sucessivas cancelam as anteriores; uma falha não deixa a imagem antiga identificada como se fosse a posição nova.

Reproduzir intervalo selecionado prepara uma entrada progressiva temporária com somente aquele intervalo. O fim natural do arquivo encerra o áudio; o temporizador de 100 ms serve para atualizar a posição na interface. Retomar a composição completa depois disso parte da posição do projeto. O vídeo em movimento continua sendo uma prévia de 30 fps, enquanto a navegação pausada usa os quadros da origem. A codificação e os dispositivos ainda possuem granularidade de quadros e amostras; isso não transforma a prévia em ferramenta de medição do instante audível no hardware.

Retomar revalida a composição e os arquivos de origem por caminho, tamanho e data de modificação. Mudanças invalidam somente os blocos afetados; arquivos ausentes geram erro. O cache mantém blocos de cinco segundos por janela e um arquivo progressivo, com limite total de 2 GB, e é removido ao fechar. A próxima preparação limpa resíduos identificados de sessões encerradas, preservando caches em uso por outra janela.

Os metadados de áudio e vídeo são reutilizados em memória, com até 256 entradas por janela, identificadas por caminho, tamanho, última escrita e tipo da mídia. Leituras canceladas ou arquivos alterados durante a consulta não entram no cache. A consulta de quadros começa com uma janela de cinco segundos antes e depois do cursor, usando fim absoluto no ffprobe. Se faltarem quadros vizinhos, amplia a margem para 20 e 80 segundos e, como recurso de segurança, recorre ao índice completo. A janela só é reutilizada quando cobre o instante solicitado e os vizinhos necessários; seu fim não é interpretado como fim da mídia. O índice de navegação é mantido em memória, invalidado quando a origem muda e limitado a 16 mídias ou um milhão de timestamps no conjunto. Uma mídia com mais de um milhão de quadros exige divisão em arquivos menores para navegação precisa. A preparação completa pode levar tempo em projetos longos; a interface permite cancelar e continuar trabalhando. Consulte as medições da validação antes de assumir abertura ou busca instantânea.

Os blocos equivalentes são reutilizados enquanto seus conteúdos e arquivos de origem não mudarem; navegar ou alterar a seleção pode iniciar outra sessão progressiva sem invalidar esses blocos. A preparação usa blocos de cinco segundos, alinhados à grade da prévia de 30 quadros por segundo. Cada bloco considera os recortes, volumes, camadas e arquivos usados naquele intervalo, além das dimensões globais da composição. Blocos intactos são reutilizados; versões anteriores permanecem disponíveis para desfazer/refazer enquanto houver espaço. Alterações que deslocam os conteúdos no tempo podem invalidar vários blocos seguintes. Fontes somente de áudio são decodificadas com até um segundo anterior ao recorte, descartado por atrim antes da montagem, inclusive nas junções dos blocos. O caminho atual copia vídeo e áudio PCM dos blocos para uma entrada NUT progressiva, sem recodificação AAC integral nem compressão intermediária nas junções. A reprodução começa com reserva inicial e prepara até quinze segundos à frente da posição. O caminho alternativo de arquivo completo continua disponível para contratos sem entrada contínua; a montagem MOV pertence a esse caminho. O cache reserva aproximadamente 1 GB para blocos e menos de 1 GB para o arquivo progressivo; blocos antigos são descartados por uso quando necessário. Consulte [o plano da entrada contínua](plano-entrada-continua.md) para os limites e a recuperação da reprodução. Preparar uma seleção também respeita o limite total de 2 GB. O cache é removido ao fechar normalmente. Edição, desfazer/refazer e troca de projeto interrompem a reprodução; a próxima reprodução prepara somente os blocos necessários e monta a versão atual. Fechar ou cancelar encerra o processo de preparação.

As listas têm ações visíveis **Remover da biblioteca** e **Remover elemento da composição**, além de Delete. Remover um clipe fecha seu intervalo e ajusta/corta as camadas nesse intervalo; remover uma camada preserva a sequência principal. Ctrl+Z desfaz. A mídia continua na biblioteca ao remover da composição; nenhum desses comandos apaga os arquivos originais.
Há um único botão **Reproduzir/Pausar** na área do vídeo. Ele controla a mídia aberta ou a montagem quando não há mídia da biblioteca aberta. Espaço na timeline continua ativando a montagem. O nome acessível acompanha apenas a ação, e os anúncios são Reproduzindo ou Pausado. O botão permanece acessível durante a preparação, que não devolve o foco automaticamente se o usuário navegou para outro controle.

No topo do projeto aberto, **Voltar para a tela inicial** fecha o projeto usando o fluxo de proteção das alterações não salvas e interrompe a prévia. Ao retornar, o foco fica em Novo projeto.
