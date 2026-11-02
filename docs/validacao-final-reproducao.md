# Fechamento técnico da reprodução

01/11/2026. Exportação permanece em outra feature. Este registro separa implementação, evidência automatizada e aceite externo.

## Implementação

- Índice assíncrono com timestamps racionais da origem, inclusive VFR. Cancelamento não deixa o índice parcial reutilizável; caminho, tamanho e modificação invalidam os dados antigos.
- Navegação pausada compõe diretamente os arquivos e as camadas ativas. Não usa os quadros da prévia de 30 fps como substitutos dos quadros da origem. Funciona antes da primeira reprodução, em recortes fora de keyframes, entre clipes e no fim exato.
- Fontes de imagem/áudio usam passos de 1/30 s; vídeos usam seus timestamps. Ctrl+setas e Page Up/Down conservam a navegação por tempo.
- A última busca prevalece. Cancelar preparação ou busca interrompe leitura/extração; sair do contexto aguarda a liberação. Falhas não mantêm uma imagem antiga identificada como resultado novo.
- A linha visual usa o tempo global da composição, evitando soma de arredondamentos de um quadro por clipe. Áudio preserva os offsets na origem e é composto com volumes individuais.
- Reproduzir intervalo prepara uma fonte delimitada. O término natural do arquivo encerra o áudio e o cursor retorna ao tempo absoluto da composição; o timer da UI não decide o fim da fonte de áudio.
- O adaptador nativo conserva o destino solicitado enquanto aguarda a atualização de tempo do player, evitando que uma atualização antiga do relógio faça o cursor recuar a zero.
- Reproduzir/Pausar dinâmico, Voltar para a tela inicial, ações por Tab e consulta das camadas ativas mantidos. A atualização periódica de posição não altera continuamente o nome acessível durante reprodução.

O cache de vídeo e intervalo respeita 2 GB por janela. O índice tem limite de 16 mídias ou um milhão de timestamps no conjunto; uma mídia acima de um milhão de quadros exige divisão. Não há exportação, nova persistência de layout ou alteração no formato SQLite dos projetos.

## Evidência automatizada

Compilação Release sem avisos ou erros. A rodada completa aprovou 24 testes WPF e 72 testes de núcleo/aplicação/infraestrutura. Depois de acrescentar o teste dos dez formatos no player nativo, `testar-previa-desktop.ps1` aprovou os três testes nativos, elevando a cobertura total a 97 testes aprovados. O teste de VFR foi repetido depois de acrescentar sobreposição visual no timestamp exato e no fim semiaberto da camada.

Casos verificados:

- VFR com quadros de cores distintas nos tempos 0, 0,04 e 0,12 s; navegação nos dois sentidos, fronteiras, origem recortada, camada de vídeo e atualização externa da fonte.
- Fonte de 60 fps com quadro intermediário que não existe na grade de 30 fps; recorte de 1 ms.
- Vinte clipes de 50 ms: imagem e áudio mudam no tempo global esperado, sem deriva acumulada. Intervalo de 400 ms com duração própria e conteúdo de áudio conferido.
- Camadas, transparência, proporções, rotação MOV, mistura/volumes, saturação, fontes ausentes, cache, cancelamento durante preparação e índice, recuperação e liberação de arquivos.
- Player LibVLC real: imagem, áudio, vídeo, busca, pausa, retomada, quadro antes da preparação, fim da seleção e retorno à composição completa.

`validar-reproducao.ps1` gera arquivos e confere metadados, preparação e imagem em JPG, JPEG, PNG, MP4, MOV, MP3, WAV, M4A, OGG/Vorbis e OGG/Opus. Todos passaram. São amostras sintéticas, não uma certificação de todos os codecs e arquivos de cada contêiner.

## Medições

Windows 10.0.26300 x64; 8 processadores lógicos; Intel Family 6 Model 140; aproximadamente 8 GB disponíveis ao runtime. Fonte sintética de 160×90 repetida em MP4 de 30 minutos, prévia com lado maior de 640 pixels, camada de áudio e camada visual. Não representa fontes 4K ou muitos efeitos.

| Composição | Preparação | Verificação/reutilização do cache | Busca e composição de um quadro | Arquivo preparado |
|---|---:|---:|---:|---:|
| 1 minuto | 10,001 s | <1 ms | 328 ms | 5.973.883 bytes |
| 10 minutos | 44,932 s | 1 ms | 2.321 ms | 61.170.277 bytes |
| 30 minutos | 159,531 s | 1 ms | 1.900 ms | 183.848.927 bytes |

A meta inicial de busca em até 500 ms não foi atingida consistentemente. A UI permanece assíncrona e cancelável; não há garantia de busca instantânea. A verificação do cache não mede o tempo até a primeira amostra audível. A leitura inicial do índice faz parte da primeira operação que precisa dele.

Reprodução das medições: `./validar-reproducao.ps1 -Benchmark`. O JSON e as amostras ficam em `.tools/test-artifacts/validacao-reproducao`; o projeto `projeto-validacao.sonarframe` permite conferir os mesmos formatos no editor. A execução desta sessão usou TEMP/TMP em `.tools/test-artifacts/temporarios` para respeitar o ambiente de ferramentas.

## Aceite externo

A publicação foi substituída em `artifacts/desktop`; nenhuma pasta adicional foi criada em `artifacts`. O executável publicado abriu com PATH restrito a Windows/System32, DOTNET_ROOT/DOTNET_ROOT_X64 apontando para diretório inexistente e diretório de trabalho separado da instalação. A árvore UI Automation confirmou a tela inicial e a tela de projeto, incluindo Voltar, Reproduzir composição, ações e nomes dos arquivos. O percurso interativo completo foi interrompido para evitar disputa de comandos quando o controle do computador detectou entrada do usuário. Não registrar esse percurso como integralmente concluído.

Os hashes dos arquivos principais estão em `.tools/test-artifacts/validacao-reproducao/publicacao.json`. A abertura isolada comprova inicialização portátil nesta máquina; não comprova reprodução em uma máquina limpa.

O NVDA instalado foi localizado, mas as duas tentativas de inicialização pelo controle do computador não produziram processo/janela utilizável. Não foi declarada validação da fala nesta versão. O ambiente também não possui Windows Sandbox; uma execução isolada de PATH/runtime nesta máquina não equivale a testar uma instalação limpa.

Para o aceite final: abrir a versão de `artifacts/desktop`, percorrer as ações com Tab e setas usando NVDA, confirmar Reproduzir/Pausar e Voltar, navegar por quadros, reproduzir uma seleção e conferir com mídias reais. Em outra máquina Windows x64 sem SDK/FFmpeg instalado, copiar a pasta inteira e repetir abertura, prévia e composição. Não marcar esses dois itens como aprovados apenas com base nos testes WPF.

Referências da implementação: [busca e accurate_seek do FFmpeg](https://ffmpeg.org/ffmpeg.html#Main-options), [filtros de timestamps, trim e composição](https://ffmpeg.org/ffmpeg-filters.html).

## Cache incremental após edições

A preparação passou a usar blocos de cinco segundos. Os blocos válidos são preservados entre edições e podem ser reutilizados ao desfazer/refazer. A montagem final copia o vídeo e codifica uma única faixa de áudio a partir dos blocos com PCM em ponto flutuante. O limite total continua em 2 GB por janela, incluindo a seleção; a limpeza descarta blocos antigos por uso e o fechamento remove o cache temporário.

Validação: build Release sem avisos ou erros e 99 testes aprovados (74 de domínio/aplicação/infraestrutura e 25 de desktop, incluindo o player nativo). O teste incremental confirma três blocos iniciais, apenas um novo bloco após alterar o volume de uma camada, dois reaproveitados e três reaproveitados ao desfazer. Alterar a origem de áudio invalida apenas o bloco que a usa. Outro teste verifica vídeo, áudio e uma camada atravessando a junção, além do último bloco com duração fracionária.

As medidas de preparação integral registradas anteriormente neste documento são históricas, anteriores ao cache incremental; não representam um benchmark desta implementação. A primeira preparação ainda precisa criar todos os blocos necessários, e deslocamentos na timeline podem invalidar blocos posteriores.
## Correções da revisão — 2 de novembro de 2026

Sobre e Ajuda agora informam o suporte a camadas, o nome Cancelar preparação ou busca e Home/End na lista da composição. Metadados são reutilizados por identidade do arquivo em cache limitado a 256 entradas, com exclusão de versões antigas, proteção contra consultas concorrentes e sem armazenar leituras canceladas ou alteradas durante a inspeção.

O áudio recebe até um segundo de decodificação anterior ao recorte; atrim descarta o excesso. Um teste compara amostras nas junções de 5 e 10 segundos com a decodificação contínua, usando recorte inicial de 137 ms e MP3, M4A/AAC, Ogg/Vorbis e Opus. Os quatro formatos passaram, com erro quadrático relativo inferior a 2% nas janelas de 100 ms ao redor das junções, incluindo a codificação AAC final.

Medição reproduzível: `./validar-reproducao.ps1 -BenchmarkBuscas`. O arquivo `.tools/test-artifacts/validacao-reproducao/buscas.json` registra uma busca inicial e cinco seguintes por projeto. Cada projeto usa uma origem sintética de 30 minutos; o índice cobre a origem inteira mesmo nos recortes menores. Esta medição isola a navegação sem preparação prévia da composição e não é uma comparação direta com o benchmark anterior com camadas.

| Duração da composição | Primeira busca, sem índice | Cinco buscas seguintes |
| --- | --- | --- |
| 1 minuto | 5.001 ms | 103, 109, 113, 103, 109 ms |
| 10 minutos | 4.881 ms | 97, 103, 107, 100, 115 ms |
| 30 minutos | 6.033 ms | 111, 107, 109, 124, 110 ms |

A meta de até 500 ms foi atendida nas buscas com índice e metadados disponíveis neste ensaio. A leitura inicial do índice ainda ultrapassa essa meta; continua assíncrona e cancelável, sem promessa de primeira busca instantânea. Os resultados não garantem a mesma latência para todas as mídias, quantidades de camadas ou máquinas.

Validação final desta revisão: compilação Release sem avisos ou erros; 101 testes aprovados, sendo 76 de domínio/aplicação/infraestrutura e 25 de desktop, incluindo reprodução nativa. A matriz dos dez formatos importáveis também passou.

## Busca por janela — 2 de novembro de 2026

A navegação agora consulta timestamps de quadros por janela com fim absoluto, expansão progressiva (margens de 5, 20 e 80 segundos) e recurso ao índice completo quando não há cobertura suficiente. Mantém invalidação por origem e cancelamento. Não substitui quadros por timestamps de pacotes. Extremos e mídias com keyframes distantes ainda podem exigir leitura maior; não há garantia universal de 500 ms.

No mesmo ensaio sintético `-BenchmarkBuscas`, a primeira busca levou 297 ms (1 minuto), 305 ms (10 minutos) e 290 ms (30 minutos), contra 5.001, 4.881 e 6.033 ms anteriores. As cinco seguintes ficaram em 101–159, 106–113 e 107–120 ms, respectivamente. Essas medições substituem a limitação anterior de leitura integral obrigatória na primeira busca intermediária.
Validação: 102 testes na suíte completa e um teste adicional de keyframe distante, todos aprovados (103 no total). Inclui CFR/VFR, recortes fracionários, expansão, reutilização, invalidação e recurso ao índice completo no extremo. Matriz de dez formatos aprovada.
