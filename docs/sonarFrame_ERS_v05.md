# sonarFrame — Especificação de Requisitos de Software (ERS)

| Campo | Informação |
|---|---|
| Versão | 0.5 — identidade do produto e proposta visual |
| Data | 18/10/2026 |
| Nome aprovado do produto | **sonarFrame** |
| Estado | Requisitos RF01–RF06, gerenciamento de projetos e RNF01 aprovados funcionalmente; marca visual proposta, ainda não aprovada; pendências identificadas |
| Plataforma-alvo | Windows; aplicativo desktop instalável |
| Leitor de telas de referência | NVDA |
| Documento de origem | *Editor de Vídeo Acessível*, v0.2 (14/10/2026) e consolidação v0.3 (18/10/2026) |

> **Natureza deste documento:** registro normativo das decisões de produto tomadas durante o refinamento. Ele não constitui prova de conformidade ou certificação ISO. Requisitos aprovados podem depender de validação técnica e testes com usuários; decisões não tomadas são declaradas em aberto. A redação foi revisada com base em práticas de especificação de requisitos da ISO/IEC/IEEE 29148:2018 e do *INCOSE Guide to Writing Requirements*, v4.

## 1. Objetivo, escopo e convenções

### 1.1 Objetivo do produto

Disponibilizar o **sonarFrame**, um editor de vídeo acessível, inicialmente para uso pessoal e posteriormente para avaliação por outras pessoas cegas. A edição deverá ser executável integralmente pelo teclado e compatível com o NVDA. O produto deverá privilegiar tarefas comuns de edição audiovisual, evitando configurações técnicas que não sejam necessárias para o usuário.

O sonarFrame deverá oferecer importação de mídias, prévia, organização de uma linha do tempo, cortes, tratamento de áudio, legendas, elementos visuais, transições, salvamento de projetos e exportação. A inteligência artificial (IA) será opcional, sem substituir o controle do usuário sobre operações de edição.

### 1.2 Escopo inicial

- Aplicativo desktop Windows com C# e WPF.
- Processamento audiovisual local por FFmpeg, sem dependência de IA ou serviços de nuvem para edição convencional.
- Edição não destrutiva, preservando as mídias originais.
- Projetos persistidos em SQLite.
- Exportação em MP4 com resoluções 720p e 1080p e orientações 16:9 e 9:16.
- Importação de legendas SRT e geração opcional por IA configurada pelo usuário.

**Fora do escopo inicial:** criação manual de legendas do zero; efeitos visuais além do conjunto aprovado; criação automática de vinhetas; exportação 4K; obrigação de alertas de distorção sonora; exigência de criptografia do projeto.

### 1.3 Convenções de escrita e rastreabilidade

- **“Deverá”** identifica comportamento exigido e verificável.
- **RF** designa requisito funcional; **GP**, gerenciamento de projetos; **RNF**, requisito não funcional/experiência de acessibilidade; **DT**, diretriz técnica.
- Cada enunciado possui identificador estável. Uma regra pode apontar para outro requisito, evitando duplicação e divergências.
- **Aprovado** significa decisão de comportamento aceita durante o refinamento, não implementação ou teste concluídos.
- **Pendente** identifica uma decisão não confirmada. Recomendações técnicas não são apresentadas como decisões aprovadas.

## 2. Fluxo principal de utilização

1. Na tela inicial, o usuário deverá poder iniciar um projeto, consultar **Meus projetos**, abrir um projeto existente ou acessar configurações.
2. No projeto, o botão **Adicionar** deverá oferecer as guias **Fotos e vídeos**, **Áudios e músicas** e **Legendas e efeitos**.
3. A importação de uma mídia deverá colocá-la na **biblioteca de mídias**, não automaticamente na linha do tempo.
4. O usuário deverá poder selecionar mídias na biblioteca, ouvir ou visualizar a prévia e inseri-las explicitamente na linha do tempo.
5. O usuário deverá poder organizar os conteúdos e editar por comandos de teclado, utilizando a navegação temporal precisa e uma lista estruturada sincronizada com a linha do tempo.
6. O usuário deverá salvar um projeto independentemente da exportação.
7. O usuário deverá selecionar configurações de saída e exportar o vídeo, acompanhando o progresso de forma acessível.

## 3. Requisitos funcionais

### RF01 — Importação e biblioteca de mídias

**Estado: aprovado.**

- **RF01.01 — Ponto único de adição.** O aplicativo deverá disponibilizar o botão **Adicionar** como entrada para inserir novos conteúdos no projeto.
- **RF01.02 — Guias.** Ao acionar **Adicionar**, o aplicativo deverá apresentar as guias **Fotos e vídeos**, **Áudios e músicas** e **Legendas e efeitos**, com nomes identificáveis pelo NVDA.
- **RF01.03 — Vídeos.** A guia **Fotos e vídeos** deverá aceitar arquivos `.mp4` e `.mov`.
- **RF01.04 — Imagens.** A mesma guia deverá aceitar `.jpg`, `.jpeg` e `.png`.
- **RF01.05 — Áudios.** A guia **Áudios e músicas** deverá aceitar `.mp3`, `.wav`, `.m4a` e `.ogg`, incluindo arquivos OGG com áudio Opus, comuns em mensagens de voz. O conteúdo efetivo e seus codecs deverão ser validados pelo aplicativo.
- **RF01.06 — Seleção múltipla.** O usuário deverá poder importar vários arquivos em uma operação, utilizando comandos convencionais de seleção múltipla do Windows.
- **RF01.07 — Biblioteca antes da montagem.** Após a importação, cada arquivo deverá ser apresentado na biblioteca, com identificação de tipo, nome e demais informações disponíveis. **Nenhum arquivo deverá ser inserido automaticamente na linha do tempo.**
- **RF01.08 — Prévia.** O usuário deverá poder pré-visualizar ou ouvir um arquivo importado antes de decidir adicioná-lo à composição, conforme o tipo de mídia.
- **RF01.09 — Inserção explícita.** O usuário deverá poder selecionar um ou mais arquivos da biblioteca e adicioná-los à linha do tempo por comando explícito e acessível.
- **RF01.10 — Erros de importação.** Arquivos incompatíveis ou ilegíveis deverão produzir mensagem acessível, sem comprometer os demais arquivos importados.

**Critérios de aceitação — RF01**

- **CA-RF01-01:** ao importar dois vídeos simultaneamente, ambos constam na biblioteca e a linha do tempo permanece inalterada.
- **CA-RF01-02:** após selecionar um item na biblioteca e executar **Adicionar à linha do tempo**, o item passa a integrar a composição.
- **CA-RF01-03:** a seleção múltipla, prévia e inserção podem ser realizadas sem mouse e são identificadas pelo NVDA.
- **CA-RF01-04:** um arquivo não suportado gera erro consultável sem impedir a importação dos arquivos válidos.

### RF02 — Corte, divisão, junção e organização

**Estado: aprovado.** Depende dos comandos temporais do RF05.

- **RF02.01 — Seleção para corte.** O usuário deverá poder delimitar um intervalo da composição utilizando o RF05.
- **RF02.02 — Remoção.** Ao excluir um intervalo selecionado, o aplicativo deverá removê-lo da composição e unir automaticamente os conteúdos restantes, sem deixar lacuna temporal decorrente dessa operação.
- **RF02.03 — Cursor após remoção.** Após a exclusão, o cursor deverá ser posicionado no conteúdo seguinte ao corte, quando existir.
- **RF02.04 — Divisão não destrutiva.** O aplicativo deverá permitir dividir um conteúdo em partes sem descartar nenhuma delas.
- **RF02.05 — Identificação de partes.** Partes originadas de uma divisão deverão possuir identificação acessível, como “Nome do vídeo — Parte 1” e “Nome do vídeo — Parte 2”.
- **RF02.06 — Reorganização.** O usuário deverá poder alterar a ordem dos vídeos e das partes divididas por teclado, inclusive movendo um item para antes, depois, início ou fim da sequência.
- **RF02.07 — Inserção intermediária.** O usuário deverá poder inserir um novo conteúdo entre partes ou clipes existentes.
- **RF02.08 — Histórico.** Operações de edição deverão permitir **desfazer** e **refazer**.
- **RF02.09 — Preservação da origem.** Divisões, cortes, junções e reordenações deverão alterar a composição do projeto, não os arquivos originais.

**Critérios de aceitação — RF02**

- **CA-RF02-01:** dividir um clipe em uma posição válida produz duas partes, preservando o conteúdo anterior e posterior.
- **CA-RF02-02:** excluir uma seleção elimina o trecho e une as partes remanescentes sem espaço vazio decorrente do corte.
- **CA-RF02-03:** mover o terceiro clipe para a primeira posição altera a ordem da composição; o NVDA informa a nova posição.
- **CA-RF02-04:** desfazer restaura a edição anterior e refazer reaplica a alteração, sem modificar a mídia original.

### RF03 — Áudio, mixagem e organização

**Estado: aprovado.** A importação de áudios deverá reutilizar a biblioteca e a inserção explícita descritas no RF01.

- **RF03.01 — Fontes independentes.** O aplicativo deverá permitir controlar separadamente o áudio original dos vídeos e os áudios ou músicas adicionados ao projeto.
- **RF03.02 — Volume.** Cada fonte de áudio editável deverá admitir volume de **0% a 200%**, incluindo silêncio em 0%.
- **RF03.03 — Sobreposição.** O aplicativo deverá permitir reproduzir simultaneamente áudio original e um ou mais áudios adicionados, preservando seus controles individuais.
- **RF03.04 — Intervalo e ordem.** Áudios importados deverão poder ser posicionados e reorganizados temporalmente na composição, inclusive com definição de início e término.
- **RF03.05 — Controle por trecho.** O usuário deverá poder alterar o volume de trechos específicos sem impor o mesmo volume a toda a composição.
- **RF03.06 — Prévia sonora.** O editor deverá permitir ouvir o resultado das alterações antes da exportação.
- **RF03.07 — Entrada e saída graduais.** Áudios deverão admitir fade-in e fade-out, segundo o RF04.
- **RF03.08 — Sem normalização obrigatória.** O sistema não deverá impor ajuste automático de volume ou emitir alertas automáticos obrigatórios de distorção.

**Critérios de aceitação — RF03**

- **CA-RF03-01:** um áudio importado aparece primeiro na biblioteca; somente após ação explícita é inserido na composição.
- **CA-RF03-02:** uma música em 40% e o áudio original em 100% podem ser reproduzidos simultaneamente.
- **CA-RF03-03:** o controle de volume aceita os limites de 0% e 200%, anunciados corretamente pelo NVDA.
- **CA-RF03-04:** o usuário consegue deslocar um áudio e ouvir uma prévia do resultado sem usar o mouse.

### RF04 — Legendas, imagens, textos e efeitos

**Estado: aprovado.** O RF04 reúne o posicionamento de elementos, legendas e o conjunto fechado de efeitos da primeira versão.

#### RF04.A — Legendas: importação, IA e edição

- **RF04.01 — Duas origens.** O aplicativo deverá permitir adicionar legendas **importando SRT** ou **gerando-as por IA**. Não deverá oferecer criação de legendas do zero por digitação manual no editor.
- **RF04.02 — Importação local.** A importação de SRT deverá funcionar sem API Key, sem conexão com serviços de IA e independentemente da habilitação da IA.
- **RF04.03 — Geração automática.** Com a IA habilitada e configurada, o usuário deverá poder solicitar legendas para **o vídeo inteiro** ou **somente um intervalo selecionado** com o RF05.
- **RF04.04 — Idiomas.** O idioma padrão da geração deverá ser **português brasileiro**. Quando suportado pelo provedor, o aplicativo deverá permitir detecção automática ou seleção de outros idiomas.
- **RF04.05 — Revisão da geração.** Após a geração, o aplicativo deverá informar a quantidade de legendas produzidas e permitir confirmar sua aplicação ao projeto.
- **RF04.06 — Conflitos.** Se o intervalo de destino já possuir legendas, o aplicativo deverá solicitar a escolha entre **substituir as existentes** ou **cancelar**. Não deverá sobrescrevê-las automaticamente.
- **RF04.07 — Edição contextual.** Para corrigir uma legenda existente, o usuário deverá pausar o vídeo em um momento em que ela esteja ativa, abrir o menu contextual pelo **clique direito**, **Shift + F10** ou **tecla de aplicações**, e escolher **Editar legenda**.
- **RF04.08 — Propriedades no mesmo fluxo.** A interface **Editar legenda** deverá permitir alterar texto, fonte, tamanho, cor, negrito, itálico, fundo e posicionamento, sem exigir navegação por várias interfaces desconectadas.
- **RF04.09 — Alcance da formatação.** Ao alterar propriedades visuais de legendas, o usuário deverá poder optar por **aplicar apenas à legenda selecionada** ou **aplicar a todas as legendas do projeto**.
- **RF04.10 — Retorno da edição.** Ao confirmar ou cancelar a edição contextual, o vídeo deverá permanecer pausado; a posição temporal deverá ser preservada e o foco deverá retornar à região de edição de origem.

#### RF04.B — Posicionamento e tamanho de imagens e textos

- **RF04.11 — Posicionamento universal.** Legendas, textos e imagens deverão utilizar o mesmo conjunto de **nove posições**, resultantes de três posições verticais (superior, centro, inferior) e três horizontais (esquerda, centro, direita).
- **RF04.12 — Inserção de imagem.** Ao inserir uma imagem, o usuário deverá escolher entre **substituir a imagem do vídeo durante o intervalo** e **sobrepor a imagem ao conteúdo visual existente**. O áudio original deverá ser preservado por padrão; seu controle permanecerá disponível pelo RF03.
- **RF04.13 — Tamanho de imagem sobreposta.** O aplicativo deverá oferecer **pequeno (limite de 25%)**, **médio (50%)**, **grande (75%)** e **personalizado (5% a 100%, em passos de 5%)**, usando largura e altura disponíveis do quadro como limites. A proporção da imagem deverá ser preservada.
- **RF04.14 — Imagem substitutiva.** Para uma imagem que substitua o conteúdo visual, o aplicativo deverá oferecer **Preencher por inteiro** (corte proporcional de extremidades quando necessário) e **Permitir bordas** (imagem integral centralizada, com faixas pretas quando houver áreas não preenchidas).
- **RF04.15 — Intervalo de exibição.** O usuário deverá conseguir determinar início e fim da exibição de elementos, aproveitando a seleção temporal do RF05.
- **RF04.16 — Sobreposição simultânea.** Elementos poderão ocupar a mesma região no mesmo intervalo. O NVDA deverá informar a ocorrência ou possibilidade de sobreposição, sem bloquear a operação.
- **RF04.17 — Edição de propriedades.** O usuário deverá poder modificar posição e propriedades de elementos já inseridos, por teclado e sem alterar os originais.

#### RF04.C — Efeitos e transições

- **RF04.18 — Conjunto fechado de transições.** O editor deverá disponibilizar exatamente estas cinco transições na primeira versão: **Dissolver suavemente**, **Passagem pelo preto**, **Passagem pelo branco**, **Deslizar** e **Zoom de transição**.
- **RF04.19 — Ponto de aplicação.** O usuário deverá poder aplicar uma transição entre conteúdos visuais adjacentes, incluindo clipes de apresentação, vinhetas já produzidas, vídeos documentais e fotografias.
- **RF04.20 — Fades.** O editor deverá oferecer **fade-in** e **fade-out** para vídeos, fotografias e áudios. Efeitos sonoros e visuais deverão possuir controles independentes.
- **RF04.21 — Duração simplificada.** Para as transições, o usuário deverá poder selecionar **Rápida (0,3 s)**, **Normal (0,8 s)** ou **Suave (1,5 s)**.
- **RF04.22 — Descrição acessível.** Cada transição deverá possuir nome e descrição objetiva do resultado visual, identificáveis pelo NVDA.
- **RF04.23 — Alteração e remoção.** O usuário deverá poder pré-visualizar, modificar ou remover efeitos e transições aplicados sem usar o mouse.
- **RF04.24 — Vinhetas prontas.** Vinhetas pré-produzidas deverão ser importadas como vídeos comuns pelo RF01. Não haverá criador automático de intros na primeira versão.

#### RF04.D — Habilitação, credenciais e falhas de IA

- **RF04.25 — Estado inicial.** A função **Habilitar inteligência artificial** deverá estar **desativada por padrão**.
- **RF04.26 — Ocultação.** Com a IA desativada, opções e ações que dependam de IA não deverão ser exibidas e não deverão executar chamadas aos provedores; funções locais deverão continuar disponíveis.
- **RF04.27 — Configuração.** Com a IA habilitada, o usuário deverá poder configurar ou substituir a chave de API e selecionar o provedor compatível, quando existir mais de um.
- **RF04.28 — Pré-condição.** A geração por IA não deverá ser iniciada sem chave de API configurada. A configuração deverá impedir o início de operações com credenciais ausentes ou reconhecidas como inválidas.
- **RF04.29 — Proteção de credenciais.** As chaves deverão ser protegidas por mecanismos adequados do Windows e não deverão ser gravadas em texto simples no arquivo SQLite do projeto.
- **RF04.30 — Transparência do envio.** Antes do primeiro envio de áudio ou outros dados a um serviço externo, o aplicativo deverá informar o envio e solicitar autorização explícita.
- **RF04.31 — Falhas.** Problemas de autenticação, indisponibilidade do provedor ou interrupção de geração deverão produzir mensagens acessíveis e preservar o conteúdo previamente salvo.
- **RF04.32 — Desativação posterior.** Desabilitar a IA não deverá remover legendas, resultados já incorporados ou outros conteúdos anteriormente produzidos.
- **RF04.33 — Autorização para alterações.** Nenhuma alteração sugerida pela IA deverá ser aplicada à composição sem autorização explícita do usuário.

**Critérios de aceitação — RF04**

- **CA-RF04-01:** importar um SRT funciona com a IA desligada; não existe opção de criar legenda nova apenas digitando-a.
- **CA-RF04-02:** com a IA desligada, **Gerar legendas com IA** fica oculta; com IA habilitada e chave válida, é possível escolher vídeo inteiro ou seleção.
- **CA-RF04-03:** após gerar legendas para um trecho, o sistema apresenta a quantidade gerada e solicita confirmação; diante de conflito, oferece substituir ou cancelar.
- **CA-RF04-04:** pausando sobre uma legenda existente, **Shift + F10 → Editar legenda** permite editar o texto e propriedades; o retorno preserva o ponto de reprodução.
- **CA-RF04-05:** a formatação permite selecionar **esta legenda** ou **todas as legendas**.
- **CA-RF04-06:** uma imagem sobreposta admite as nove posições, tamanhos pré-definidos e personalizados; uma substituição visual admite **Preencher por inteiro** e **Permitir bordas**.
- **CA-RF04-07:** o menu de transições apresenta somente as cinco opções aprovadas e os três tempos aprovados; os efeitos fade-in e fade-out são aplicáveis aos tipos autorizados.
- **CA-RF04-08:** um erro de API não altera legendas existentes e é anunciado em mensagem acessível.

### RF05 — Navegação temporal e seleção de trechos

**Estado: aprovado (refinamento 0.6).**

- **RF05.01 — Precisão básica.** Na região de navegação temporal, as **setas direita e esquerda** deverão avançar ou retroceder **um quadro por acionamento**. A duração do passo será derivada da taxa de quadros do conteúdo.
- **RF05.02 — Deslocamento intermediário.** **Ctrl + seta direita/esquerda** deverá avançar/retroceder **um segundo**.
- **RF05.03 — Deslocamento rápido.** **Page Down** deverá avançar **dez segundos**; **Page Up** deverá retroceder **dez segundos**.
- **RF05.04 — Seleção com Shift.** A combinação de **Shift** com cada atalho temporal deverá alterar a seleção pelo respectivo intervalo, nos dois sentidos.
- **RF05.05 — Extremos.** **Home** deverá posicionar o cursor no início e **End** no fim da composição, sem selecionar conteúdo.
- **RF05.06 — Seleção até os extremos.** **Shift + Home** deverá selecionar da posição atual até o início; **Shift + End**, da posição atual até o fim. Se já estiver no extremo indicado, não deverá criar seleção positiva.
- **RF05.07 — Anúncio da posição.** Ao navegar manualmente, o NVDA deverá anunciar a posição temporal atual, sem anunciar repetidamente a duração total da seleção. O tempo deverá admitir representação `HH:MM:SS.mmm`.
- **RF05.08 — Precisão de áudio.** Em operações específicas de áudio, o aplicativo deverá permitir ajustes mais finos do que um quadro quando necessários e tecnicamente viáveis, sem exigir conhecimento de milissegundos.
- **RF05.09 — Sincronização.** Cortes do conteúdo audiovisual deverão manter a sincronização entre imagem e áudio original; alterações exclusivas no áudio não deverão encurtar automaticamente o vídeo.
- **RF05.10 — Prévia de corte.** O usuário deverá conseguir ouvir e/ou reproduzir o conteúdo próximo aos pontos de corte para verificar o resultado.

**Critérios de aceitação — RF05**

- **CA-RF05-01:** na linha do tempo, pressionar uma seta desloca a posição em um quadro, Ctrl + seta em um segundo e Page Up/Down em dez segundos.
- **CA-RF05-02:** Shift + Home seleciona do cursor ao início; Shift + End seleciona do cursor ao final, sem executar corte.
- **CA-RF05-03:** o NVDA anuncia apenas a posição atual a cada deslocamento manual; não interrompe a reprodução com anúncios temporais contínuos.
- **CA-RF05-04:** a reprodução de teste após um corte mantém áudio e imagem sincronizados.

### RF06 — Exportação

**Estado: aprovado.**

- **RF06.01 — Contêiner de saída.** O vídeo exportado deverá utilizar o formato **MP4**.
- **RF06.02 — Resoluções.** O aplicativo deverá oferecer **720p** e **1080p**, com **1080p selecionado por padrão**.
- **RF06.03 — Orientação.** O aplicativo deverá oferecer **16:9 (horizontal)** e **9:16 (vertical)**. A orientação deverá ser escolhida pelo usuário, **sem opção pré-selecionada**.
- **RF06.04 — Enquadramento.** Quando houver diferença de proporção, o usuário deverá escolher entre **Preencher a tela** (permitindo recorte proporcional) e **Permitir bordas** (preservando integralmente a imagem, com faixas quando necessário).
- **RF06.05 — Codificação.** Parâmetros técnicos de codificação deverão ser definidos automaticamente pelo aplicativo; não serão exigidos conhecimentos de codec para executar uma exportação comum.
- **RF06.06 — Arquivo de destino.** O usuário deverá poder definir **nome do arquivo** e **pasta de destino**.
- **RF06.07 — Progresso.** O aplicativo deverá disponibilizar **porcentagem concluída**, **estimativa de tempo restante** quando calculável e estado de processamento acessíveis ao NVDA.
- **RF06.08 — Conclusão.** Ao concluir, o aplicativo deverá informar o resultado e oferecer ações para **abrir o vídeo** ou **abrir a pasta** que o contém.
- **RF06.09 — Responsividade.** Durante a exportação, a interface deverá continuar utilizável para consultar estado e mensagens, sem bloquear o leitor de telas; ações de edição poderão permanecer indisponíveis durante o processamento.

**Critérios de aceitação — RF06**

- **CA-RF06-01:** a tela apresenta MP4, 1080p inicialmente selecionado e nenhuma orientação selecionada; a exportação exige a escolha da orientação.
- **CA-RF06-02:** o usuário seleciona 720p/1080p, 16:9/9:16 e uma das duas políticas de enquadramento usando apenas teclado.
- **CA-RF06-03:** o usuário informa nome e pasta, acompanha progresso e pode abrir o arquivo ou sua pasta depois da conclusão.

### RF07 — Requisito original não recuperado

**Estado: pendente.** O conteúdo original do RF07 mencionado na versão 0.2 não foi recuperado com segurança. **Nenhum comportamento foi atribuído a este identificador.** A numeração permanece reservada até redefinição explícita.

## 4. Gerenciamento de projetos (GP)

**Estado: aprovado.** O módulo de projetos tem identificadores próprios para não ocupar indevidamente o RF07.

- **GP01 — Tela inicial.** O aplicativo deverá disponibilizar **Novo projeto**, **Meus projetos**, **Abrir projeto existente** e **Configurações**.
- **GP02 — Catálogo por diretório.** **Meus projetos** deverá localizar projetos válidos no diretório de projetos configurado e apresentá-los em uma lista acessível.
- **GP03 — Identificação.** Cada projeto listado deverá ser identificado, no mínimo, por **nome** e **data da última modificação**.
- **GP04 — Diretório configurável.** O aplicativo deverá utilizar uma pasta padrão de projetos e permitir alterá-la nas configurações. A alteração não deverá excluir projetos já salvos.
- **GP05 — Abertura externa.** **Abrir projeto existente** deverá permitir selecionar um arquivo de projeto localizado fora do diretório padrão.
- **GP06 — Novo projeto.** O usuário deverá poder informar o nome do projeto e iniciar com biblioteca e linha do tempo vazias.
- **GP07 — Formato.** O estado do projeto deverá ser salvo em **SQLite**, em um arquivo reconhecido e validado pelo aplicativo. **Não será criado um formato proprietário para a finalidade de impedir edição externa.**
- **GP08 — Conteúdo salvo.** O projeto deverá preservar referências às mídias, biblioteca importada, ordem da linha do tempo, cortes, divisões, volumes, legendas, imagens, efeitos, configurações pertinentes e posição temporal da sessão.
- **GP09 — Referências externas.** O salvamento deverá preservar referências aos arquivos originais sem duplicar automaticamente seu conteúdo no banco do projeto.
- **GP10 — Mídia ausente.** Se uma mídia referenciada não for encontrada, o aplicativo deverá avisar e permitir indicar sua nova localização sem descartar automaticamente as operações associadas.
- **GP11 — Salvamento manual.** **Ctrl + S** deverá salvar as alterações do projeto sem exportar ou renderizar vídeo.
- **GP12 — Salvamento automático.** Havendo alterações não salvas, o aplicativo deverá realizar salvamento automático a cada **dois minutos**.
- **GP13 — Recuperação.** Após encerramento inesperado, na próxima abertura o aplicativo deverá oferecer recuperação da sessão disponível.
- **GP14 — Integridade.** O editor deverá verificar se o arquivo SQLite contém a estrutura esperada de um projeto válido e informar falhas de abertura de modo acessível.
- **GP15 — Preservação.** Salvar e reabrir projetos não deverá alterar as mídias originais.

**Critérios de aceitação — GP**

- **CA-GP-01:** **Meus projetos** encontra e lista os arquivos de projeto válidos existentes na pasta configurada.
- **CA-GP-02:** importar uma mídia, organizar a composição e executar Ctrl + S permite reabrir o projeto com o estado preservado.
- **CA-GP-03:** com alterações pendentes, o projeto é salvo automaticamente após o intervalo de dois minutos; sem alterações, não é exigida gravação desnecessária.
- **CA-GP-04:** após simular um encerramento inesperado, o aplicativo oferece recuperar a sessão disponível.
- **CA-GP-05:** quando uma mídia foi movida, o editor informa o problema e aceita uma nova localização preservando os demais dados do projeto.

## 5. Requisitos não funcionais — acessibilidade e navegação (RNF01)

**Estado: aprovado integralmente em 18/10/2026.** O modelo combina navegação por regiões, linha do tempo temporal e lista estruturada de conteúdos.

### 5.1 Navegação por regiões

- **RNF01.01 — Uso integral pelo teclado.** Todos os fluxos descritos nesta ERS deverão poder ser executados sem mouse.
- **RNF01.02 — Acesso direto a regiões.** O editor deverá disponibilizar:

  | Atalho | Destino |
  |---|---|
  | **Alt + 1** | Biblioteca de mídias |
  | **Alt + 2** | Propriedades do elemento selecionado |
  | **Alt + 3** | Linha do tempo |
  | **Alt + 4** | Prévia e reprodução |
  | **Alt + 5** | Comandos de projeto (salvar, exportar, gerenciar) |

- **RNF01.03 — Navegação convencional.** A interface deverá suportar **Tab** e **Shift + Tab** entre controles, **F6** e **Shift + F6** para circular entre as regiões principais e **Ctrl + Tab** para alternar entre guias quando aplicável.
- **RNF01.04 — Comandos por contexto.** Atalhos como setas, Home e End deverão obedecer ao controle que possui foco. Os comandos temporais do RF05 deverão atuar na região de linha do tempo, sem capturar a navegação convencional da biblioteca ou de campos de texto.
- **RNF01.05 — Reprodução contextual.** **Barra de espaço** deverá reproduzir/pausar quando o foco estiver na linha do tempo ou na área de reprodução; dentro de campos editáveis, deverá manter sua função normal.

### 5.2 Linha do tempo e lista estruturada

- **RNF01.06 — Duas modalidades complementares.** O editor deverá permitir navegar pela **posição temporal** conforme RF05 e por uma **lista estruturada dos conteúdos** da composição.
- **RNF01.07 — Informações por item.** A lista estruturada deverá apresentar nome, tipo, ordem e duração dos elementos quando disponíveis, anunciáveis pelo NVDA.
- **RNF01.08 — Seleção sem reprodução implícita.** Selecionar um item na lista não deverá iniciar automaticamente sua reprodução nem aplicar operações de edição.
- **RNF01.09 — Reordenação acessível.** O usuário deverá poder mover um conteúdo para antes, depois, início ou fim da sequência sem arrastar com o mouse; após o movimento, o NVDA deverá informar sua posição na lista.
- **RNF01.10 — Estados distintos.** O editor deverá distinguir **foco de teclado**, **elemento selecionado** e **posição temporal**, mantendo consistência entre essas informações sem tratá-las como equivalentes.

### 5.3 Menus, foco e mensagens

- **RNF01.11 — Menus contextuais.** Comandos pertinentes ao item ou instante atual deverão estar disponíveis por **clique direito**, **Shift + F10** e **tecla de aplicações**, em menus identificáveis pelo NVDA.
- **RNF01.12 — Restauração de foco.** Ao fechar menus e diálogos, o foco deverá retornar ao elemento de origem quando ele continuar disponível; salvar automaticamente não deverá deslocar o foco.
- **RNF01.13 — Cancelamento.** A tecla **Escape** deverá fechar menus e diálogos canceláveis, preservando o estado anterior à operação ainda não confirmada.
- **RNF01.14 — Notificações proporcionais.** O NVDA deverá receber anúncios relevantes para mudanças de estado, conclusão de ações e erros, sem repetição desnecessária.
- **RNF01.15 — Reprodução sem interrupções recorrentes.** Durante a reprodução contínua, o editor não deverá anunciar automaticamente a posição temporal a cada avanço do vídeo; anúncios temporais ocorrerão na navegação manual ou consulta explícita.
- **RNF01.16 — Área de mensagens.** O aplicativo deverá disponibilizar uma região acessível de **mensagens recentes**, permitindo reler avisos, erros e resultados.
- **RNF01.17 — Progresso consultável.** Operações longas deverão expor estado e progresso consultáveis, com anúncios pontuais que não impeçam ouvir ou navegar.

### 5.4 Interface e implementação acessível

- **RNF01.18 — Identificação de controles.** Controles interativos deverão expor nome, tipo, estado, valor e ações aplicáveis por **UI Automation**, conforme sua função.
- **RNF01.19 — Componentes nativos.** A implementação deverá priorizar controles nativos do WPF quando atendam ao comportamento exigido. Componentes personalizados deverão oferecer exposição acessível equivalente.
- **RNF01.20 — Indicadores visuais.** A interface deverá oferecer foco visível, contraste adequado e informação que não dependa exclusivamente de cor.
- **RNF01.21 — Testes contínuos.** A acessibilidade deverá ser verificada durante o desenvolvimento usando NVDA, navegação exclusivamente por teclado e ferramentas de inspeção de UI Automation.

**Critérios de aceitação — RNF01**

- **CA-RNF01-01:** é possível iniciar projeto, importar mídias, editar, salvar e exportar sem mouse.
- **CA-RNF01-02:** Alt + 1 a Alt + 5 levam às cinco regiões correspondentes; F6/Shift + F6 alternam regiões, preservando ordem previsível de foco.
- **CA-RNF01-03:** na biblioteca, as setas navegam entre itens; na linha do tempo, executam o RF05; num campo de texto, não alteram o vídeo.
- **CA-RNF01-04:** a lista estruturada permite selecionar, identificar e reordenar clipes sem iniciar reprodução por seleção.
- **CA-RNF01-05:** após editar uma legenda por Shift + F10, o foco retorna ao contexto anterior, com reprodução pausada e posição preservada.
- **CA-RNF01-06:** salvar automaticamente não desloca o foco; notificações de progresso não interrompem continuamente a reprodução.
- **CA-RNF01-07:** um erro de importação ou exportação permanece disponível para releitura em **mensagens recentes**.
- **CA-RNF01-08:** auditoria de UI Automation e teste real com NVDA identificam nomes e estados dos controles e permitem concluir os fluxos essenciais.

## 6. Diretrizes técnicas aprovadas e decisões de implementação pendentes

### 6.1 Diretrizes técnicas estabelecidas

- **DT01 — Plataforma:** Windows, aplicativo instalável, C# e WPF; .NET 8 ou superior conforme documento-base.
- **DT02 — Áudio e vídeo:** FFmpeg para operações locais de corte, composição, mixagem e exportação; integração por processo externo ou biblioteca ainda sujeita a escolha de implementação.
- **DT03 — Persistência:** SQLite em arquivo de projeto; operações não destrutivas e mídia original referenciada.
- **DT04 — Acessibilidade:** controles WPF e UI Automation; linha do tempo personalizada com `AutomationPeer` e notificação de posição temporal quando adequada; atalhos implementáveis com mecanismos próprios do WPF.
- **DT05 — IA opcional:** integrações externas por API condicionadas à habilitação e configuração; ações de edição sob controle do usuário; armazenamento seguro de credenciais.

### 6.2 Escolhas técnicas ainda não fechadas

As seguintes decisões **não** são tratadas como requisitos já aprovados:

- Biblioteca de prévia audiovisual: **LibVLCSharp** ou **FFME**; exige prova de conceito com NVDA e avaliação da experiência de foco.
- Empacotamento e instalador: **MSIX** ou **Inno Setup**.
- Biblioteca de integração com FFmpeg e estratégia de execução/monitoramento de processos.
- Codec e parâmetros concretos para a exportação MP4, mantendo a exigência funcional de codificação automática.
- Esquema SQLite, versionamento/migração do esquema e extensão final do arquivo de projeto.
- Pasta padrão exata de projetos. O requisito aprovado é a existência de um diretório configurável; uma estrutura sob **Documentos** é apenas sugestão.

## 7. Pendências e riscos de validação

### 7.1 Pendência de requisito

- **PEN-01 — RF07:** recuperar ou redefinir expressamente o requisito original, sem inferir sua finalidade.

### 7.2 Pontos de validação técnica

- **VAL-01 — Quadros e áudio:** comprovar que navegação quadro a quadro, seleção e cortes geram resultados coerentes em diferentes taxas de quadros; os ajustes exclusivamente sonoros deverão respeitar limitações reais dos arquivos.
- **VAL-02 — Transições:** verificar resultado e duração das cinco transições nas combinações de vídeos e imagens, sem prometer comportamento visual não validado.
- **VAL-03 — Codecs de entrada:** validar arquivos reais MP4/MOV, M4A e OGG/Opus, inclusive amostras de iPhone e mensagens de voz.
- **VAL-04 — Prévia acessível:** escolher o mecanismo de reprodução que permita controle previsível de foco, teclado e NVDA.
- **VAL-05 — Recuperação:** testar salvamento periódico, falhas de energia/processo e recuperação de sessão sem perda indevida de dados já persistidos.
- **VAL-06 — Leitores de tela:** testar a comunicação de notificações e a lista estruturada com versões atuais do NVDA em Windows.
- **VAL-07 — Conflitos de atalhos:** validar Alt + 1–5, F6, Page Up/Down e combinações com Shift em cenários reais de foco.

## 8. Matriz de rastreabilidade resumida

| Grupo | Decisão principal | Referência de verificação | Estado |
|---|---|---|---|
| RF01 | Biblioteca com importação múltipla e inserção explícita | CA-RF01-01 a 04 | Aprovado |
| RF02 | Cortar, dividir, reordenar sem lacunas decorrentes de exclusão | CA-RF02-01 a 04 | Aprovado |
| RF03 | Áudio independente, múltiplas fontes, 0–200% | CA-RF03-01 a 04 | Aprovado |
| RF04 | SRT/IA, edição contextual, imagens, cinco transições | CA-RF04-01 a 08 | Aprovado |
| RF05 | Navegação por quadros/segundos e seleção por teclado | CA-RF05-01 a 04 | Aprovado |
| RF06 | Exportação MP4, enquadramento e progresso acessível | CA-RF06-01 a 03 | Aprovado |
| GP | SQLite, Meus projetos, autosave e recuperação | CA-GP-01 a 05 | Aprovado |
| RNF01 | Navegação por regiões, lista estruturada e NVDA | CA-RNF01-01 a 08 | Aprovado |
| RF07 | Conteúdo não recuperado | PEN-01 | Pendente |

## 9. Revisão da qualidade da especificação

A redação foi revisada segundo boas práticas de engenharia de requisitos, considerando **clareza, identificadores estáveis, atomicidade, consistência, viabilidade, rastreabilidade e possibilidade de verificação**.

| Critério de revisão | Tratamento aplicado |
|---|---|
| Clareza e linguagem normativa | Regras redigidas com “deverá”, identificando ator, condição e resultado quando pertinente. |
| Atomicidade | Comportamentos distintos receberam subidentificadores; listas fechadas foram explicitadas. |
| Testabilidade | Cada conjunto de requisitos possui critérios de aceitação observáveis. |
| Consistência | Prevaleceu a decisão recente: não há criação manual de legendas nem formato proprietário obrigatório; RF05 substitui o deslocamento antigo de um segundo por seta. |
| Rastreabilidade | Mantidos os identificadores RF01–RF07; novos fluxos usam GP e RNF para não inventar o RF07. |
| Separação de responsabilidades | Requisitos funcionais, não funcionais, decisões arquiteturais e riscos estão separados. |
| Gestão de lacunas | Ponto não recuperado e escolhas técnicas restantes estão identificados como pendentes, não como fatos aprovados. |

**Resultado da revisão:** documento adequado como **baseline funcional de requisitos para planejamento e derivação de histórias técnicas**, com pendências explicitadas. **Não há alegação de certificação formal nem de implementação/validação prática concluída.**

## 10. Identidade do produto e proposta de logotipo

**Situação:** o nome **sonarFrame** foi **aprovado** pelo responsável pelo projeto. Os elementos visuais abaixo são **proposta para avaliação**, não requisitos funcionais aprovados nem identidade visual definitiva.

### 10.1 Nome e posicionamento

- **Grafia oficial de trabalho:** `sonarFrame` (s minúsculo e F maiúsculo, sem espaço).
- **Categoria:** editor de vídeo acessível para Windows, operável por teclado e compatível com NVDA.
- **Associação conceitual:** *sonar* remete à percepção por ondas sonoras; *frame* representa o quadro de vídeo. Juntos, comunicam a possibilidade de trabalhar com conteúdo visual por uma experiência orientada à audição e à navegação acessível.
- **Assinatura textual sugerida, não aprovada:** “Edição de vídeo sem barreiras.”

### 10.2 Descrição sugerida do logotipo

**Conceito:** um símbolo geométrico que reúne um quadro de vídeo e um pulso de sonar, acompanhado do nome **sonarFrame**.

**Descrição visual detalhada:** à esquerda do nome, há uma moldura horizontal de vídeo, desenhada somente por seus quatro cantos arredondados, com pequenas aberturas entre eles. Dentro da moldura, um ponto circular à esquerda emite **três arcos curvos concêntricos** em direção à direita, como ondas de sonar se propagando. O desenho é simples, sem texturas, brilhos, gradientes ou detalhes pequenos. À direita aparece o texto **sonarFrame**, em uma fonte sem serifa de boa legibilidade. A palavra é contínua; **sonar** aparece com peso regular e **Frame** em seminegrito, preservando a grafia escolhida para a marca.

**Cores sugeridas:** azul-petróleo escuro (`#0B2D45`) no nome e na moldura; verde-água escuro (`#007A7A`) nas ondas. Deve existir também versão monocromática, em preto ou branco, sem perda de identificação.

**Versão reduzida:** em ícones pequenos, usar somente a moldura e o conjunto simplificado de ondas, verificando a legibilidade em 16 × 16, 24 × 24 e 32 × 32 pixels. A iconografia não deverá depender somente de cor para ser reconhecida.

**Descrição acessível sugerida para apresentação verbal:** “O logotipo sonarFrame combina uma moldura de vídeo formada por quatro cantos com ondas curvas de sonar no centro. Ao lado, o nome sonarFrame aparece em letras simples, com destaque para a palavra Frame.”

**Texto alternativo curto, quando a imagem não estiver acompanhada do nome escrito:** `sonarFrame — editor de vídeo acessível`.

**Texto alternativo quando o nome estiver visível ao lado do símbolo:** utilizar alternativa concisa para evitar leitura duplicada; quando a imagem for estritamente decorativa e o nome estiver presente como texto, marcar o símbolo como decorativo na interface.

### 10.3 Considerações para uso comercial

A adoção do nome no projeto **não equivale a uma verificação de disponibilidade de marca, domínio ou registro**. Antes de disponibilizar o produto comercialmente, realizar pesquisas de anterioridade e avaliar registro da marca e uso do símbolo. A proposta de logotipo deverá ser validada quanto a escalabilidade, contraste e identificação por pessoas com e sem deficiência visual.

## 11. Histórico de versões

- **v0.2 — 14/10/2026:** documento-base de trabalho, com RF01–RF06, RF07 não recuperado e diretrizes técnicas iniciais.
- **v0.3 — 18/10/2026:** consolidação de decisões anteriormente recuperadas; diversos pontos ainda em refinamento.
- **v0.4 — 18/10/2026:** incorporação das decisões posteriores aprovadas para RF01–RF06, SQLite e gerenciamento de projetos, geração opcional de legendas por IA, efeitos, RF05 revisado e RNF01 de acessibilidade/navegação. Remoção de afirmações superadas das versões anteriores.
- **v0.5 — 18/10/2026:** adoção do nome **sonarFrame**; adição de conceito e descrição acessível de logotipo como **proposta ainda não aprovada**. Requisitos e critérios de aceitação da versão 0.4 preservados.

## 12. Referências de boas práticas

Fontes utilizadas como orientação para a **qualidade da redação e acessibilidade**, não como certificação do documento:

1. ISO/IEC/IEEE 29148:2018 — *Systems and software engineering — Life cycle processes — Requirements engineering*. ISO: <https://www.iso.org/standard/72089.html>.
2. INCOSE — *Guide to Writing Requirements*, v4 (2023): <https://portal.incose.org/Web/iCore/Store/StoreLayouts/Item_Detail.aspx?Category=EBOOKS&iProductCode=GUIDEWRITEREQ>.
3. Microsoft Learn — UI Automation e `AutomationPeer` em aplicações WPF: <https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/ui-automation-of-a-wpf-custom-control>.
4. Microsoft Learn — Visão geral de foco no WPF: <https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/focus-overview>.

---

**Regra de governança:** futuras alterações em requisitos aprovados deverão atualizar seus identificadores, critérios de aceitação e histórico de versões. O RF07 somente poderá ser definido mediante nova decisão explícita.
