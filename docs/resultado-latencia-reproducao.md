# Otimização da latência de reprodução

## Diagnóstico e decisão

O reaproveitamento de blocos não evitava trabalho proporcional à duração total. A cada composição diferente, mesmo sem blocos novos, o preparador concatenava todo o PCM, codificava AAC e executava outro FFmpeg para montar o MP4. A fábrica inicializa LibVLC uma vez por janela, mas cria um player por abertura. O coordenador carrega pausado, posiciona e retoma; o estado `Playing` não comprova a apresentação do primeiro quadro.

Pipeline anterior: identidade → blocos de vídeo/áudio → AAC integral → remux MP4 → carregamento/posicionamento/retomada → saída nativa.

Pipeline escolhido: mesma identidade e mesmos blocos → remux MOV copiando H.264 e PCM float → mesmo player → saída nativa. Nenhuma mudança nos projetos salvos, mídias originais, cortes racionais, camadas ou resolução da prévia. A seleção de intervalo ainda usa seu processamento específico; não confundir seus números com reprodução completa.

## Matriz anterior à escolha

Valores de referência da investigação inicial em composição CFR de 30 s: mudança localizada de volume, 3,64 s até saída, com 2,43 s em AAC; reordenação sem renderizações, 1,68 s; segunda abertura, 0,51 s. São medições preliminares, não os resultados finais padronizados abaixo. As estimativas desta tabela foram apresentadas antes da implementação; nenhuma é promessa de desempenho.

| Estratégia | Problema | Medido → esperado | Ganho estimado | Esforço / complexidade | RAM adicional | Disco adicional | Risco / manutenção |
|---|---|---|---|---|---|---|---|
| A: cache atual | Invalidação/duplicação | Reordenação 1,68 s → próximo disso; já renderiza zero | Pequeno nesse caso | Baixo / baixa | Pequena | Pequeno | Baixos |
| B: conteúdo | Reutilizar segmentos deslocados | Já implementado; AAC de 2,43 s permanece | Sem ganho incremental comprovado | Médio / média | Índices | Sem acréscimo obrigatório | Médios |
| C: segmentação dinâmica | Menos área renderizada | Volume 3,64 → hipótese 3–3,6 s | 0–18% | Alto / alta | Mais índices | Mais arquivos/junções | Altos |
| D: sob demanda | Não preparar tudo antes de tocar | Hipótese 0,5–2 s com região disponível | Potencial alto, não medido | Alto / alta | Buffers, sem medição | Pré-carregamento limitado pelo orçamento | Altos |
| E: montagem por cópia | AAC integral desnecessário | Volume 3,64 → estimativa 1,2–1,8 s | 51–67% | Médio / média | Sem buffer integral novo; um processo a menos | Aproximadamente +10,8 MB por 30 s | Médio / baixa-média |
| F: camadas adicionais de cache | Reuso de transformações | Fluxos já separados; AAC permanece | Incerto | Alto / alta | Índices adicionais | Resultados intermediários adicionais | Altos |
| G: proxies | Decodificação de originais pesados | Não elimina AAC; prévia já tem lado máximo 640 | Incerto nestas amostras | Médio / média | Não medido | Cópias proporcionais às origens | Médios |
| H: preparação ociosa | Antecipar trabalho | Pode aproximar de 0,51 s se terminar antes do Play | Condicional à ociosidade | Alto / média-alta | Fila e processo antecipado | Cache antecipado | Médios/altos |

Estimativa de disco de E: PCM float estéreo 48 kHz = 384.000 bytes/s, contra AAC nominal de 24.000 bytes/s. Diferença de 64,8 MB em 180 s e 216 MB em 600 s, sem contar vídeo e índices. Mantém-se o teto de 2 GB; não se aumenta o orçamento para obter o ganho. O consumo real de RAM não foi isolado; não atribuir números fictícios a ele.

Escolhida E combinada com A/B/F já existentes. O ensaio MOV confirmou a base temporal de áudio `1/48000`, compatível com as amostras do cache. A alternativa Matroska não foi necessária para a implementação escolhida. A montagem continua proporcional ao volume de bytes, porém deixa de decodificar/codificar áudio. Não foi criado cache de versões completas que multiplicasse o tamanho das prévias.

Reprodução sob demanda foi considerada especialmente para o custo frio: um arquivo progressivo precisa coordenar disponibilidade, busca, encerramento e sincronismo; concatenação com arquivos ainda inexistentes não é uma fila de reprodução confiável. Alternar players por segmento também exige validar continuidade e áudio nas junções. Uma implementação com callbacks/stream próprio teria custo maior que a remoção da etapa global já medida. Permanece alternativa para projetos longos após avaliar o gargalo residual. Preparação ociosa requer prioridade/cancelamento/limites próprios; iniciar FFmpeg a cada movimento do cursor seria contraproducente.

Referências de conceitos, sem atribuir aos outros editores os ganhos medidos aqui: [reuso de previews no Premiere](https://helpx.adobe.com/uk/premiere/desktop/render-and-export/render-sequences-for-playback/use-preview-files-when-rendering.html), [cache do Resolve](https://documents.blackmagicdesign.com/UserManuals/DaVinci-Resolve-17-Colorist-Guide.pdf), [preview scaling e proxies do Shotcut](https://forum.shotcut.org/pub/preview-scaling), [background rendering do Final Cut](https://support.apple.com/en-ph/guide/final-cut-pro/ver717f3ca3/mac), [concatenação e contêineres FFmpeg](https://ffmpeg.org/ffmpeg-formats.html), [estatísticas do LibVLC 3](https://videolan.videolan.me/vlc-3.0/group__libvlc__media.html).

## Instrumentação e método

`DiagnosticoPreparacao` registra identidade/normalização, construção/consulta de chaves, hits/misses, segmentos e fluxos renderizados, áudio, vídeo, AAC, montagem/validação e preparação total. A criação de processos mede `Process.Start`, não todo o carregamento interno/primeira decodificação do FFmpeg. Os tempos de áudio e vídeo incluem sua inicialização e processamento; não somar novamente os tempos sobrepostos. Metadados/índices e coordenação explicam parte do tempo residual.

`FabricaPreviaVlc` mede inicialização da biblioteca/player e observa incrementos em `DisplayedPictures` e `PlayedAudioBuffers` depois de retomar. Os contadores anteriores do carregamento pausado são descontados. A observação termina ao pausar/descartar ou após 15 s e não bloqueia o retorno do comando. O diagnóstico do Desktop diferencia conclusão do comando, primeiro quadro e primeiro buffer.

Esses contadores são evidência da saída do motor, não medição acústica/física de tela. São amostrados a cada 10 ms, mas a atualização interna do LibVLC pode acrescentar atraso; não alegar precisão de 10 ms. A latência começa na entrada do comando WPF, não num sensor da tecla física. O benchmark exige confirmação tanto visual quanto de áudio; o resultado agregado usa o primeiro dos dois eventos.

`medir-latencia.ps1 -Etapa antes|depois` gera as amostras, compila com `--no-incremental` e executa `BenchmarkLatencia.MedirSaidaNativa`. A compilação integral evita reutilizar um binário antigo ao trocar snapshots com datas anteriores. O parâmetro só nomeia o relatório: **não restaura o algoritmo anterior**. A montagem anterior instrumentada foi preservada localmente em `.tools/laboratorio-latencia/CacheBaseline.cs`; a comparação exige executar a versão correspondente do código.

São 84 combinações: 14 cenários × 30/180/600 s × CFR/VFR. Fontes sintéticas de 600 s, 160×90, seno 440 Hz, AAC; VFR alterna 15/30 fps. Há imagem vermelha e áudio 880 Hz para camadas separadas. A prévia usa o perfil real de 640 pixels/30 fps. Cada série começa com cache próprio vazio; alterações compartilham o cache da série. Antes dos cortes, aquece-se a composição original. Editar começa a reprodução em zero; busca distante usa duração menos 5 s. Segunda reprodução também recria o player para padronizar aberturas; não mede o caminho mais rápido de retomar o mesmo player pausado.

Resultados brutos e amostras: `.tools/test-artifacts/latencia-reproducao`. Execuções sequenciais na mesma máquina, sem outro benchmark FFmpeg em paralelo. Uma observação por combinação; não representam distribuição estatística nem performance garantida em mídias 4K, discos lentos ou outra máquina. Os ensaios interrompidos durante o ajuste dos contadores não compõem a comparação final.

## Implementação e arquivos

- `CacheBlocosComposicao.cs`: um remux MOV por cópia substitui AAC + remux MP4; limites, validação e limpeza preservados.
- `DiagnosticoPreparacao.cs` e `PreparadorComposicaoFfmpeg.cs`: métricas de identidade/cache/processos com contexto assíncrono por preparação.
- `FabricaPreviaVlc.cs`: inicialização e observação de saída com encerramento antes de liberar recursos nativos.
- `JanelaPrincipal.Composicao.cs` e `JanelaPrincipal.Reproducao.cs`: registro sem bloquear edição e distinção entre cancelamento explícito e obsolescência interna.
- `TestesCacheIncremental.cs`: PCM exato, contagem de processos, equivalência integral e solicitações concorrentes.
- `TestesReproducaoComposicao.cs`: edição durante preparação, cursor/foco e silêncio sobre cancelamento interno.
- `BenchmarkLatencia.cs` e `medir-latencia.ps1`: benchmark nativo reproduzível e opt-in.

As demais alterações já presentes no checkout pertencem às entregas anteriores ou ao usuário; esta análise não as atribui à otimização atual.

## Validação e resultados

- `./build.ps1`: build sem avisos/erros; 81 testes de domínio/aplicação/infraestrutura e 30 testes Desktop aprovados. Os cinco nativos e o benchmark são opt-in e foram executados separadamente.
- `./testar-previa-desktop.ps1`: cinco testes nativos aprovados, incluindo retomada do modo mudo, cortes, histórico, cursor e formatos.
- `./validar-reproducao.ps1`: dez formatos aprovados (JPG, JPEG, PNG, MP4, MOV, MP3, WAV, M4A, OGG e Opus).
- `./run-desktop.ps1 -Publish`: publicação concluída em `artifacts/desktop`, substituindo a versão anterior. Nenhum diretório alternativo de publicação foi criado.
- `./medir-latencia.ps1 -Etapa antes`: 84 casos aprovados; duração total do teste 33 min 30 s.
- `./medir-latencia.ps1 -Etapa depois`: 84 casos aprovados; duração total do teste 11 min 57 s. A duração global inclui aquecimentos e não substitui a métrica de cada reprodução.
- Comparação automática em `.tools/laboratorio-latencia/comparar.ps1`: mesmas quantidades de blocos renderizados nas 84 combinações; zero AAC nas montagens otimizadas; zero renderizações em repetição, divisão equivalente e busca distante.
- Equivalência integral de vídeo e áudio frio/quente por `framemd5`, inclusive corte de 4,99 a 5,01 s atravessando uma fronteira. PCM da montagem idêntico ao PCM concatenado do cache; contagem exata de amostras e PTS contínuos em 14 s e 14,98 s. Mantidos testes de junções MP3/AAC/Vorbis/Opus, camadas, saturação, cancelamento e fontes alteradas.
- Solicitações concorrentes da mesma composição reutilizam a prévia e não iniciam FFmpeg. Edição durante preparação cancela o trabalho obsoleto, mantém cursor/foco e não anuncia cancelamento interno.

### Ganho após edições

Cada duração tem vinte pares: dez operações de edição em CFR e as mesmas dez em VFR. Não entram nesta agregação a primeira preparação, segunda abertura, divisão equivalente e busca distante; todos aparecem na tabela integral. O ganho abaixo é a mediana dos ganhos percentuais **por par**, não a razão de duas medianas independentes.

| Duração | Ganho mediano por cenário | Casos com ganho ≥60% | Casos que pioraram |
|---|---:|---:|---:|
| 30 s | 18,0% | 3/20 | 5/20 |
| 180 s | 73,0% | 15/20 | 1/20 |
| 600 s | 87,5% | 18/20 | 0/20 |

A meta de 60% foi alcançada em quinze dos vinte cenários médios, **não em todos**. Exemplo CFR: corte no meio de 180 s passou de 12,45 s para 2,18 s; corte próximo ao fim, de 15,72 s para 2,57 s. Reordenação de 600 s: CFR 46,65 → 3,93 s (91,6%); VFR 54,80 → 4,39 s (92,0%). Nesses últimos casos já havia zero renderizações antes: o ganho vem da montagem, não de novos acertos do cache.

Edições sucessivas médias tiveram ganho de 33,2% em CFR e piora de 8,6% em VFR. Nos curtos, cinco casos de edição pioraram. Não há evidência para prometer a mesma redução em toda duração ou operação.

### Primeira preparação e variação

| Duração | Tipo | Antes | Depois |
|---|---|---:|---:|
| 30 s | CFR | 10,49 s | 42,88 s |
| 30 s | VFR | 6,82 s | 7,29 s |
| 180 s | CFR | 47,76 s | 44,89 s |
| 180 s | VFR | 39,65 s | 31,92 s |
| 600 s | CFR | 129,81 s | 172,77 s |
| 600 s | VFR | 132,50 s | 119,82 s |

O caso curto CFR inclui **30,36 s na inicialização LibVLC/player**, antes de abrir a mídia. O valor foi preservado. Nas outras primeiras aberturas da série otimizada, a inicialização ficou entre 0,42 e 0,83 s. Três verificações isoladas em processos novos, sem reprodução/montagem, mediram 0,764 s, 0,496 s e 0,310 s; não reproduziram o pico. O primeiro ensaio isolado mede carregamento, motor e criação do player; os dois seguintes também aplicam as três propriedades da fábrica. Esses ensaios não substituem o percurso WPF e não provam a causa do atraso.

A máquina possui aproximadamente 8 GB de RAM; uma leitura durante a série encontrou cerca de 810 MB livres e compressão de memória ativa. Isso é contexto para a variabilidade, **não prova causal** do pico. A carga externa e as condições térmicas não foram controladas. A renderização inicial CFR de 600 s também ficou mais lenta, apesar de o código que renderiza os blocos não ter mudado nesta otimização. As medições não permitem atribuir toda variação entre execuções ao contêiner. São necessárias repetições alternadas sob condições comparáveis para estabelecer regressão causal de desempenho.

## Revisão final e limites

Revisão pelos critérios de `code-reviewer`: não foram encontrados defeitos novos relevantes de correção, concorrência, descarte, segurança de caminhos ou acessibilidade nos diffs desta entrega. A observação dos contadores termina antes de liberar o player; diagnóstico não bloqueia a edição. As fontes e regras de domínio permanecem preservadas. Veredito técnico: **Pode aprovar**, com os limites de desempenho e aceite externo registrados abaixo.

- **Atendido automaticamente:** cópia sem AAC global, conteúdo equivalente, precisão/continuidade do áudio, cache, histórico, cancelamento, concorrência e foco no percurso testado.
- **Parcial:** meta de ganho por cenário; a primeira reprodução continua preparando toda a composição e a montagem ainda copia todos os bytes. Não houve melhoria uniforme no custo frio.
- **Limite de armazenamento:** PCM aumenta a prévia, mantendo 2 GB como teto. Próximo desse teto, pode sobrar menos espaço para preparar uma seleção; não há elevação silenciosa do orçamento.
- **Aceite externo:** fala real do NVDA, experiência com mídias/projetos do usuário e execução em máquina limpa. Testes WPF e contadores nativos não equivalem a homologação humana nem medição física do som/tela.
- **Próximas melhorias:** priorizar investigação de reprodução sob demanda para reduzir o custo frio longo; medir agrupamento de processos ao criar blocos novos; avaliar proxies com originais de alta resolução. Preparação ociosa só com fila limitada, prioridade e cancelamento. Se o pico LibVLC reaparecer, coletar logs de inicialização e separar espera de agendamento, carregamento da biblioteca e criação do player antes de mudar opções do motor.

Arquivos completos: `.tools/test-artifacts/latencia-reproducao/antes.json`, `depois.json`, `comparacao.json`, `comparacao.csv`, `resumo.json` e `inicializacao-isolada-*.json`. O script de comparação valida a correspondência dos 84 pares e os invariantes de renderização/AAC. Valores negativos na tabela seguinte significam piora medida; nenhum caso foi removido.

## Comparação integral: tempo até o primeiro quadro ou buffer de áudio
| Duração | Tipo | Cenário | Antes (s) | Depois (s) | Ganho | Blocos novos antes/depois |
|---|---|---|---:|---:|---:|---:|
| 30 s | cfr | primeira | 10,49 | 42,88 | -308,6% | 6/6 |
| 30 s | cfr | segunda | 0,47 | 0,97 | -105,9% | 0/0 |
| 30 s | cfr | divisao | 0,86 | 0,97 | -11,9% | 0/0 |
| 30 s | cfr | corte-inicio | 2,51 | 4,91 | -95,6% | 1/1 |
| 30 s | cfr | corte-meio | 2,82 | 3,59 | -27,4% | 1/1 |
| 30 s | cfr | corte-fim | 3,68 | 2,20 | 40,1% | 2/2 |
| 30 s | cfr | reordenacao | 1,48 | 1,34 | 9,4% | 0/0 |
| 30 s | cfr | volume | 4,13 | 1,47 | 64,5% | 1/1 |
| 30 s | cfr | camada-visual | 4,93 | 2,41 | 51,2% | 1/1 |
| 30 s | cfr | camada-audio | 4,96 | 1,37 | 72,4% | 1/1 |
| 30 s | cfr | desfazer | 4,04 | 0,98 | 75,8% | 0/0 |
| 30 s | cfr | refazer | 2,49 | 1,38 | 44,5% | 0/0 |
| 30 s | cfr | sucessivas | 2,13 | 1,79 | 16,0% | 1/1 |
| 30 s | cfr | busca-distante | 0,48 | 0,61 | -27,9% | 0/0 |
| 30 s | vfr | primeira | 6,82 | 7,29 | -6,9% | 6/6 |
| 30 s | vfr | segunda | 0,57 | 1,17 | -105,8% | 0/0 |
| 30 s | vfr | divisao | 0,54 | 1,29 | -138,2% | 0/0 |
| 30 s | vfr | corte-inicio | 1,88 | 2,45 | -30,4% | 1/1 |
| 30 s | vfr | corte-meio | 2,71 | 2,47 | 8,8% | 1/1 |
| 30 s | vfr | corte-fim | 4,73 | 5,06 | -7,1% | 2/2 |
| 30 s | vfr | reordenacao | 1,43 | 1,85 | -29,3% | 0/0 |
| 30 s | vfr | volume | 4,29 | 3,43 | 20,0% | 1/1 |
| 30 s | vfr | camada-visual | 6,12 | 3,92 | 36,0% | 1/1 |
| 30 s | vfr | camada-audio | 4,58 | 2,27 | 50,4% | 1/1 |
| 30 s | vfr | desfazer | 3,63 | 1,73 | 52,3% | 0/0 |
| 30 s | vfr | refazer | 1,54 | 1,38 | 10,7% | 0/0 |
| 30 s | vfr | sucessivas | 2,09 | 1,89 | 9,8% | 1/1 |
| 30 s | vfr | busca-distante | 0,14 | 0,93 | -582,0% | 0/0 |
| 180 s | cfr | primeira | 47,76 | 44,89 | 6,0% | 36/36 |
| 180 s | cfr | segunda | 0,92 | 1,02 | -11,1% | 0/0 |
| 180 s | cfr | divisao | 0,60 | 1,16 | -93,1% | 0/0 |
| 180 s | cfr | corte-inicio | 7,02 | 2,55 | 63,7% | 1/1 |
| 180 s | cfr | corte-meio | 12,45 | 2,18 | 82,5% | 1/1 |
| 180 s | cfr | corte-fim | 15,72 | 2,57 | 83,6% | 2/2 |
| 180 s | cfr | reordenacao | 10,04 | 1,42 | 85,9% | 0/0 |
| 180 s | cfr | volume | 14,71 | 1,71 | 88,4% | 1/1 |
| 180 s | cfr | camada-visual | 16,27 | 3,06 | 81,2% | 1/1 |
| 180 s | cfr | camada-audio | 16,49 | 2,54 | 84,6% | 1/1 |
| 180 s | cfr | desfazer | 14,77 | 1,96 | 86,7% | 0/0 |
| 180 s | cfr | refazer | 5,51 | 1,52 | 72,4% | 0/0 |
| 180 s | cfr | sucessivas | 4,91 | 3,28 | 33,2% | 1/1 |
| 180 s | cfr | busca-distante | 0,46 | 0,90 | -95,3% | 0/0 |
| 180 s | vfr | primeira | 39,65 | 31,92 | 19,5% | 36/36 |
| 180 s | vfr | segunda | 0,54 | 1,21 | -125,2% | 0/0 |
| 180 s | vfr | divisao | 0,54 | 1,31 | -144,7% | 0/0 |
| 180 s | vfr | corte-inicio | 7,83 | 4,48 | 42,8% | 1/1 |
| 180 s | vfr | corte-meio | 9,89 | 4,47 | 54,8% | 1/1 |
| 180 s | vfr | corte-fim | 16,11 | 5,27 | 67,3% | 2/2 |
| 180 s | vfr | reordenacao | 9,43 | 2,60 | 72,4% | 0/0 |
| 180 s | vfr | volume | 17,33 | 4,01 | 76,9% | 1/1 |
| 180 s | vfr | camada-visual | 15,26 | 4,06 | 73,4% | 1/1 |
| 180 s | vfr | camada-audio | 14,75 | 3,82 | 74,1% | 1/1 |
| 180 s | vfr | desfazer | 15,41 | 4,22 | 72,6% | 0/0 |
| 180 s | vfr | refazer | 5,07 | 2,82 | 44,4% | 0/0 |
| 180 s | vfr | sucessivas | 4,86 | 5,28 | -8,6% | 1/1 |
| 180 s | vfr | busca-distante | 0,48 | 0,24 | 49,3% | 0/0 |
| 600 s | cfr | primeira | 129,81 | 172,77 | -33,1% | 120/120 |
| 600 s | cfr | segunda | 0,87 | 0,83 | 5,5% | 0/0 |
| 600 s | cfr | divisao | 0,53 | 0,62 | -16,4% | 0/0 |
| 600 s | cfr | corte-inicio | 16,26 | 7,20 | 55,7% | 1/1 |
| 600 s | cfr | corte-meio | 31,67 | 4,98 | 84,3% | 1/1 |
| 600 s | cfr | corte-fim | 51,10 | 6,77 | 86,7% | 2/2 |
| 600 s | cfr | reordenacao | 46,65 | 3,93 | 91,6% | 0/0 |
| 600 s | cfr | volume | 48,62 | 4,67 | 90,4% | 1/1 |
| 600 s | cfr | camada-visual | 49,70 | 5,81 | 88,3% | 1/1 |
| 600 s | cfr | camada-audio | 48,70 | 4,04 | 91,7% | 1/1 |
| 600 s | cfr | desfazer | 47,04 | 3,78 | 92,0% | 0/0 |
| 600 s | cfr | refazer | 12,96 | 4,53 | 65,0% | 0/0 |
| 600 s | cfr | sucessivas | 13,31 | 5,82 | 56,3% | 1/1 |
| 600 s | cfr | busca-distante | 0,80 | 0,99 | -23,4% | 0/0 |
| 600 s | vfr | primeira | 132,50 | 119,82 | 9,6% | 120/120 |
| 600 s | vfr | segunda | 0,90 | 1,02 | -13,8% | 0/0 |
| 600 s | vfr | divisao | 0,52 | 1,05 | -101,2% | 0/0 |
| 600 s | vfr | corte-inicio | 15,61 | 4,54 | 70,9% | 1/1 |
| 600 s | vfr | corte-meio | 35,70 | 6,66 | 81,3% | 1/1 |
| 600 s | vfr | corte-fim | 58,18 | 10,18 | 82,5% | 2/2 |
| 600 s | vfr | reordenacao | 54,80 | 4,39 | 92,0% | 0/0 |
| 600 s | vfr | volume | 58,47 | 4,50 | 92,3% | 1/1 |
| 600 s | vfr | camada-visual | 102,19 | 4,53 | 95,6% | 1/1 |
| 600 s | vfr | camada-audio | 82,20 | 3,49 | 95,8% | 1/1 |
| 600 s | vfr | desfazer | 62,31 | 3,29 | 94,7% | 0/0 |
| 600 s | vfr | refazer | 16,85 | 4,26 | 74,7% | 0/0 |
| 600 s | vfr | sucessivas | 17,07 | 3,93 | 77,0% | 1/1 |
| 600 s | vfr | busca-distante | 0,87 | 0,90 | -3,3% | 0/0 |
