# Changelog

## Em desenvolvimento

- Correção dos textos de Sobre/Ajuda, cache de metadados na busca em pausa e decodificação antecipada de áudio nos recortes, com validação de junções em MP3, AAC, Vorbis e Opus.

- Cache incremental da reprodução em blocos de cinco segundos: edições reaproveitam intervalos intactos e desfazer/refazer pode recuperar blocos anteriores, com descarte por uso dentro do limite de 2 GB.

- Navegação assíncrona por timestamps reais de vídeos CFR/VFR, cancelável, com invalidação do índice quando a mídia muda.
- Quadro pausado composto diretamente das origens e camadas, disponível antes da primeira reprodução; fronteiras, recortes e vídeos acima de 30 fps preservados na navegação.
- Intervalos selecionados reproduzidos a partir de arquivo temporário delimitado, sem depender do timer da interface para encerrar o áudio.
- Linha visual baseada no tempo global da montagem, evitando deriva acumulada entre clipes com duração fracionária.
- Validação reproduzível de todos os formatos de importação e benchmark de 1, 10 e 30 minutos em validar-reproducao.ps1.

- Botão Voltar para a tela inicial no topo do projeto aberto, com proteção das alterações não salvas.
- Botão da composição alterna texto e nome acessível entre Reproduzir e Pausar conforme o estado, mantendo acesso durante a preparação.

- Reprodução da composição com FFmpeg/LibVLC: recortes, fotos, áudio, volumes e camadas, preparação cancelável, pausa/parada, cursor e intervalo selecionado.
- Camadas centralizadas sem corte, com proporção e transparência preservadas; última camada acima. Áudio misturado sem normalização automática.
- Revalidação do cache antes de retomar, limpeza de resíduos identificados de sessões encerradas e quadro da prévia decodificado ao pausar ou navegar após preparação.
- Botões visíveis de remoção na biblioteca e composição; Delete na timeline remove o elemento e permite desfazer, preservando a mídia original.

- Diálogo de importação aplica foco de teclado à guia selecionada após exibição e ativação da janela; setas mantêm o foco na guia para acessibilidade.

- Tab/Shift+Tab percorre as ações em todas as áreas antes de trocar de área; a timeline mantém as setas na lista e a seleção ao acessar ações.
- Timeline ordenada por início no vídeo, com nome, início, fim e duração; mover clipes considera a sequência principal mesmo com camadas intercaladas na exibição.

- Setas permanecem na lista de importados; Delete remove mídias não utilizadas da biblioteca com desfazer e preservação dos arquivos originais.
- Importação com seleção automática de tipo pelas quatro setas, Tab para escolher arquivos, foco em Fechar ao concluir e retorno à lista de importados.

- Biblioteca e prévia reunidas lado a lado; Tab acessa as ações do arquivo selecionado sem percorrer a lista inteira, e Enter abre a prévia diretamente.

- Tab/Shift+Tab alterna as áreas do editor com entrada direta em Reproduzir/Pausar na prévia; setas verticais percorrem a área e horizontais movem o tempo na timeline.
- Itens da biblioteca e da timeline apresentam o nome primeiro ao leitor de telas, sem identificadores internos.
- Ajuda integrada ao fim da lista inicial, com navegação por Cima/Baixo e Tab/Shift+Tab; barra Projeto/Ajuda restrita ao editor.
- Ajuda na tela inicial expande Atalhos de teclado e Sobre o sonarFrame por Enter/Espaço, preservando as opções do menu anterior e a navegação de retorno sem Escape.

- Menus Projeto e Ajuda com abertura explícita por Enter/Espaço; navegação entre cabeçalhos por setas sem expansão automática.

- Prévia explícita de uma mídia da biblioteca, sem alterar a montagem, cursor ou histórico do projeto.
- Reprodução/pausa, parada no início, busca em segundos e consulta acessível de posição; imagens estáticas sem controles temporais.
- LibVLCSharp inicializado sob demanda em segundo plano, abertura cancelável e liberação da mídia anterior antes de trocar a prévia.
- Pausa antes de editar/desfazer/refazer, encerramento ao sair do projeto e tratamento de arquivos ausentes ou inválidos.
- Laboratório do incremento 0 e testes separados para o adaptador nativo do editor. Exportação continua pendente.

## 0.1.0

Primeira versão de montagem não destrutiva para Windows x64.

- Núcleo de projeto, tempo racional, biblioteca e composição em pacotes .NET 10.
- Interface WPF com navegação por regiões, seleção temporal, menus e mensagens acessíveis por UI Automation.
- Importação local de vídeos, fotos e áudios com FFprobe, progresso e cancelamento.
- Inserção simples ou múltipla, inserção intermediária, divisão no cursor, exclusão de intervalo, reordenação e desfazer/refazer.
- Camadas independentes com intervalo editável e controle de volume de 0 a 200%.
- Salvamento SQLite, salvamento automático, recuperação explícita de sessão e atualização de referências de mídia.
- Catálogo de projetos, pasta configurável e Salvar como.
- Distribuição portátil para Windows x64 e três pacotes NuGet versionados conjuntamente.

Reprodução, renderização/exportação, legendas, transições e IA não integram a versão 0.1.0. A acessibilidade utiliza controles WPF nativos e UI Automation; a homologação com NVDA e usuários permanece requisito de distribuição estável.
