# Continuação da investigação: disponibilização progressiva da composição

Atualização posterior: a [prova inicial de playlist local](prova-playlist-local.md) encontrou descontinuidades de aproximadamente 119–124 ms no agendamento do áudio entre segmentos prontos. A [prova de entrada contínua](prova-entrada-continua.md) testa NUT audiovisual único, fornecimento progressivo e cancelamento, incluindo o limite observado quando os dados deixam de chegar. A análise abaixo preserva a decisão e as estimativas anteriores às provas; consultar os relatórios para os resultados atuais, sem interpretar o transporte como já integrado ao aplicativo.

Data: 02/11/2026. Branch: `feat/reproducao-e-previa`. Decisão de direção arquitetural; transporte progressivo ainda depende de prova. Esta revisão confronta a síntese das três análises fornecida pelo usuário, não relatórios integrais que não foram anexados. Nenhuma substituição do pipeline foi implementada nesta etapa.

## 1. Conclusão principal

Confirmo a crítica central: o cache é incremental, mas a abertura de uma composição alterada continua dependendo de um MOV completo. A retirada do AAC global foi útil e permanece correta; não eliminou a barreira de preparação integral. Revisar essa barreira agora tem prioridade sobre outra reformulação das chaves do cache.

Recomendo **preservar o cache e evoluir a preparação para intervalos solicitados, coordenados por uma sessão de reprodução que priorize o cursor**. Primeiro entregar seleção direta; depois provar apresentação contínua de segmentos locais. HLS e servidor HTTP não são requisitos dessa direção. A escolha definitiva entre lista local e entrada contínua deve ocorrer após medir junções no motor instalado, não apenas após conseguir abrir uma playlist.

Isso separa decisões já fundamentadas — o que preparar, quando preparar, como cancelar e proteger o cache — de uma hipótese ainda aberta: qual mecanismo entrega esses segmentos continuamente ao LibVLC com menor custo.

## 2. Confronto das análises e evidências

| Conclusão recebida | Avaliação no checkout atual |
|---|---|
| Há montagem global após cache hits | Confirmada: `CacheBlocosComposicao.cs:120–146` escreve dois manifestos, executa FFmpeg, aguarda FFprobe e retorna `previa.mov`. |
| Todo o conteúdo precisa estar pronto antes de tocar | Confirmada para abertura de composição alterada/fria. Repetição equivalente e retomada do mesmo player possuem caminhos de reaproveitamento; não generalizar para toda chamada de Play. |
| Invalidação em cascata continua sendo o problema central | Não sustentada como diagnóstico geral. Identidade por conteúdo, segmentos ancorados na origem e separação de áudio/vídeo já estão implementados. Cortes que alteram a fase da grade visual podem legitimamente invalidar mais blocos. |
| Seleção prepara tudo e depois recodifica | Confirmada. O Desktop chama `PrepararAsync` antes de `PrepararIntervaloAsync`; este chama novamente `PrepararAsync` e recodifica H.264/AAC. A segunda preparação normalmente é um hit completo, não uma segunda renderização integral. Mesmo assim, a dependência da composição completa existe. |
| Edição aguarda encerramento da preparação | Confirmada em `PrepararEdicaoAsync` → `InterromperComposicaoAsync` → `preparacaoFinalizada.Task`. A espera é assíncrona: não prova bloqueio da thread WPF nem espera pela renderização inteira. O processo é cancelado e encerrado. Falta medir a demora até efetivar cada comando sob cancelamento real. |
| LibVLC persiste, mas MediaPlayer é recriado | Confirmada em `FabricaPreviaVlc.CriarAsync` e `PreviaBiblioteca.AbrirAsync`. Retomada sem alteração pode manter o player. A métrica atual reúne biblioteca/player/agendamento; não atribuir todo esse tempo à criação do MediaPlayer. |
| Hashes e FileInfo são repetidos | Confirmada em `IdentidadeComposicao.cs` e revalidações do preparador. Ainda não são o maior custo nos casos analisados; faltam testes com milhares de itens. |
| Navegação disputa o índice de quadros | Existe um único semáforo por `IndiceQuadros`, mantido durante FFprobe e interpretação. Renderização e navegação usam a mesma instância no preparador. Há possibilidade de contenção; não foi medido seu custo isolado. |
| VFR exige converter as origens para CFR | Incorreta como regra do produto. A prévia em movimento já é uma representação de 30 fps; domínio racional e navegação pelos timestamps originais permanecem. |
| HLS local exige servidor HTTP próprio | Não demonstrada. APIs de entrada local/callbacks permitem investigar transporte sem servidor. A compatibilidade de playlist HLS dinâmica via arquivo com esta versão deve ser testada. |
| Playlist local basta para edição contínua | Não demonstrada. Abrir itens sucessivos é diferente de manter áudio e vídeo sem lacunas, repetição ou deriva. |

Os testes anteriores comprovam determinados casos de equivalência e reutilização, não a correção de qualquer transformação futura. Manter testes de fase temporal, camadas e versão da origem ao evoluir a preparação.

## 3. O que os números realmente mostram

Fonte: `.tools/test-artifacts/latencia-reproducao/depois.json`, com os mesmos cenários citados na revisão. Valores arredondados, em milissegundos:

| Composição/cenário CFR | Vídeo | Áudio | Montagem/validação | Primeiro áudio |
|---|---:|---:|---:|---:|
| 30 s, corte inicial | 2160 | 609 | 1236 | 4912 |
| 30 s, reordenação | 0 | 0 | 358 | 1345 |
| 600 s, primeira | 130725 | 28311 | 3955 | 172765 |
| 600 s, reordenação | 0 | 0 | 2953 | 3925 |

Portanto, a montagem global é relevante sobretudo com cache quente; no caso frio longo, o maior custo é renderizar **todos** os segmentos antes da abertura. Eliminar apenas o MOV final não elimina os 159 s de processamento de blocos daquele caso. Também não elimina a preparação do bloco que contém o cursor quando esse bloco está ausente.

Na reordenação curta, montagem foi 358 ms de 1345 ms até áudio: remover somente essa etapa não justifica prometer reprodução instantânea. No corte inicial, vídeo/áudio novos também pesam. A soma das fases não cobre toda a coordenação e os tempos não devem ser somados duas vezes.

Identidade/normalização e construção/consulta das chaves ficaram, respectivamente, em aproximadamente 2,55/2,33 ms no corte curto e 1,02/50,39 ms na reordenação de 600 s. Isso não mede separadamente todos os hashes posteriores nem comprova comportamento com muitos milhares de clipes.

Resultados mantidos da entrega anterior: zero AAC global na montagem completa; mesmos blocos renderizados nos 84 pares antes/depois; zero novas renderizações em repetição, divisão equivalente e busca distante quente. Ganhos medianos pareados após edições: 18%, 73% e 87,5% em 30/180/600 s, com regressões registradas. O benchmark de busca distante existente aquece a composição antes: **não comprova Play frio em 02:30 sem preparar o restante**.

O pico de 30,36 s na inicialização não se reproduziu nos três ensaios isolados. Sua causa continua desconhecida. A máquina teve pouca memória livre durante a série, mas isso não demonstra causalidade. Não reutilizar esses resultados de execução única como previsão de um transporte ainda não construído.

## 4. NUT, segmentos locais e continuidade

O preparador produz um NUT de H.264 e outro NUT de PCM float estéreo/48 kHz por região. Esses arquivos têm timestamps relativos à região. Colocar os dois arquivos como itens consecutivos de uma lista faria uma sequência de mídias, não uma composição audiovisual sincronizada.

A primeira prova deve combinar **um par** por cópia, formando um segmento audiovisual local. MOV H.264/PCM é o primeiro candidato porque já funciona na aplicação. NUT multiplexado pode ser testado como alternativa; o suporte direto e a busca no adaptador instalado ainda não foram demonstrados. Manter os NUT separados como cache canônico preserva reuso de vídeo após alteração de áudio. O segmento combinado é um derivado descartável, com chave formada pelas identidades dos dois fluxos, duração e perfil de mux.

Uma lista de segmentos combinados pode reduzir trabalho antes de abrir o primeiro item, mas ainda precisa provar transições. A API oficial do [MediaListPlayer](https://videolan.videolan.me/vlc-3.0/group__libvlc__media__list__player.html) descreve sequenciamento e seleção de itens; não estabelece um contrato de continuidade amostra a amostra. O adaptador atual usa `MediaPlayer` para uma mídia, portanto isso exige integração própria, inclusive conversão entre posição do item e posição global.

Outro candidato é uma **única entrada audiovisual contínua**, alimentada progressivamente. O [StreamMediaInput](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.StreamMediaInput.html) aceita um stream .NET; seu seek usa offsets de bytes e retorno zero na leitura significa fim. A API de [callbacks do LibVLC 3](https://videolan.videolan.me/vlc-3.0/group__libvlc__media.html) oferece abertura/leitura/busca/fechamento. Isso confirma um mecanismo sem HTTP, não um editor pronto: ele não mistura fluxos, corrige timestamps ou transforma busca em bytes em busca exata na timeline.

Não basta concatenar bytes dos NUT nem disponibilizar um arquivo que chega momentaneamente ao fim. Uma entrada contínua precisa de demux/remux consistente, backpressure e distinção entre falta temporária de dados, término real e cancelamento. Um manifesto apontando para blocos ainda inexistentes não implementa sozinho essa espera. Também não basta trocar de FFmpeg a cada segmento: reinícios podem introduzir cabeçalhos, resets temporais e priming.

HLS exige representação de segmentos e codecs aceitos pelo demuxer escolhido; os NUT separados atuais não se tornam HLS apenas por escrever M3U8. Áudio codificado independentemente requer verificar priming/padding. HTTP local, se necessário na implementação testada, acrescentaria ciclo de vida e acesso a recursos; não deve ser introduzido por pressuposto. Named Pipes são transporte de bytes, não solução automática para mux, busca ou sincronismo.

## 5. Comparação de alternativas

Estimativas de engenharia para um desenvolvedor familiarizado com o projeto, incluindo testes específicos, sem homologação humana. Escalas 1–5: menor para maior complexidade. Faixas não são orçamento fechado; transportes têm incerteza alta. Linhas são alternativas ou partes compartilhadas, não devem ser somadas indiscriminadamente. Não há percentuais previstos de ganho.

| Alternativa | Evidência/problema e componentes | Implementação / manutenção | Horas | Potencial e risco | Recursos / contratos / entrega incremental |
|---|---|---:|---:|---|---|
| A. Refinar montagem completa | CacheBlocos: cópia global ainda medida | 2 / 2 | 8–16 | Reduz overhead; continua proporcional ao projeto. Reuso incorreto de versão é risco | Mais versões ocupariam disco; manter teto. Contrato atual; isolável |
| J. Preparar seleção diretamente | Desktop + Intervalo: dependência integral confirmada | 2 / 2 | 12–24 | Trabalha somente na seleção e bordas; cuidar da grade global e camadas | Menos temporários; pode preservar contrato público; primeiro incremento |
| C. Segmentos AV + lista local | NUT separados; fábrica atual abre arquivo único | 3 / 3 | 24–48 | Início por região; continuidade entre itens não provada | Derivados AV + prefetch limitado; nova sessão/posição global; protótipo isolado |
| B/D. Sessão sob demanda com prefetch | Preparador devolve Midia somente ao terminar tudo | 4 / 3 | 48–88 | Retira dependência de regiões distantes; transporte precisa passar testes | Fila limitada, leases e disco ≤2 GB; novo contrato aditivo; incremental |
| E. HLS local | Candidato, sem prova no checkout | 4 / 4 | 64–112 | Distribuição progressiva; buffering, codec e timestamps exigem validação | Segmentos derivados, eventual serviço; nova fonte/sessão; laboratório primeiro |
| F. Entrada única via callbacks | API existe; não há produtor AV contínuo no projeto | 5 / 4 | 80–144 | Evita transição entre itens; risco de deadlock, EOF e seek | Buffer limitado + muxer; contratos de sessão/entrada; prova separada |
| G. Named Pipes | Canal possível entre processos | 4 / 4 | 64–120 | Pode transportar fluxo contínuo; não resolve tempo/seek | Handles, filas e produtor; adaptador novo; Windows na borda |
| H. Mais separação A/V | Separação já implementada | 3 / 3 | 24–48 | Sem gargalo adicional demonstrado; clocks separados aumentam risco | Mais intermediários; evitar dois players independentes; adiar |
| I. MediaPlayer persistente | Recriação confirmada, custo exclusivo não isolado | 2 / 3 | 8–16 | Pode reduzir abertura; não reduz renderização/mux global | Mantém recursos nativos vivos; contratos podem permanecer; isolável |
| K. Edição por gerações | Comando espera cancelamento/disposição | 3 / 3 | 16–32 | Reduz espera para editar; risco de resultado antigo sobrescrever novo | Snapshot/fila limitada; coordenação nova, domínio intacto; incremento próprio |
| L. Proxies | Prévia já reduzida; originais pesados ainda decodificados | 3 / 3 | 32–64 | Ajuda misses/4K; não remove montagem global | Disco proporcional às origens; perfil/mapeamento temporal; opcional |
| M. Composição dinâmica tipo MLT | Novo backend, não recurso já disponível | 5 / 5 | 160–320 | Potencial de evitar pré-render integral; maior risco de equivalência | Dependências/recursos nativos e novos contratos; exploração separada |
| Índice/hash, complementar | Semáforo global e revalidações observados | 2 / 2 | 8–20 | Só priorizar após medir contenção/grandes timelines | Cache por mídia e deduplicação de consultas; contrato preservável |

PCM estéreo/48 kHz/float32 representa 384.000 bytes/s: uma janela de 15 s contém 5,76 MB de áudio, além do vídeo e overhead. Isso é cálculo de payload, não medida de RAM do processo. Derivados AV duplicam temporariamente bytes; orçamento deve incluir blocos, segmentos em uso, buffers, gerações retiradas e temporários. Preservar limite de 2 GB.

## 6. Três caminhos e recomendação

**Caminho 1 — quick win:** seleção direta, sem preparar a composição inteira. Estimativa 12–24 h, complexidade 2/5. Desktop deve escolher o caminho de seleção antes de chamar a preparação completa. Reutilizar blocos existentes e tratar bordas exatas; não mudar toda a biblioteca de mídia. Resolve seleção, não promete resolver Play contínuo da composição.

**Caminho 2 — melhor custo-benefício e recomendação principal:** preparação por demanda, cache atual e sessão local que priorize cursor, com segmentos AV derivados e prefetch limitado. Primeiro candidato a transporte: lista nativa local, exclusivamente em prova. O avanço para produção depende de continuidade e busca comprovadas. Se falhar, não disfarçar a lacuna nem liberar pausas entre itens como solução: avaliar entrada única contínua ou conservar o caminho atual enquanto a prova amadurece. A arquitetura por demanda permanece útil independentemente do transporte.

Reservar 8–16 h para a prova inicial e decisão de transporte. Para uma primeira integração pelo caminho de lista local, se aprovada, estimar 48–88 h adicionais para sessão, coordenação, integração e regressão; faixa total 56–104 h. São horas de trabalho, não prazo automático de execução do agente. Reestimar após a prova; sessões e transporte compartilham trabalho com as linhas da matriz.

**Caminho 3 — máximo potencial:** entrada audiovisual única, produzida progressivamente, com gerência explícita de buffer/seek e evolução futura para composição dinâmica. Callbacks são o candidato local mais controlável para a prova desse caminho, não garantia de menor latência. Estimativa 80–144 h para integração progressiva; um backend de composição dinâmica pode elevar o esforço para 160–320 h. Não começar por reescrever o editor ou introduzir HLS.

A recomendação revisa a prioridade anterior: montagem por cópia continua como base estável e referência de correção, mas novos ganhos estruturais devem atacar **a quantidade de conteúdo exigida antes de tocar**, não apenas tornar o MOV completo um pouco mais rápido. Ainda não há evidência para declarar playlist local, callbacks ou HLS vencedor de latência.

## 7. Plano incremental e responsabilidades

1. **Prova de transporte em `.tools`.** Usar blocos existentes, combinar pares NUT por cópia e tocar sequência local no LibVLC 3.0.24/LibVLCSharp 3.10.2. Comparar com o MOV completo, desde cache quente e frio. Ensaiar NUT AV como alternativa, sem presumir suporte. Avaliar entrada única apenas se a lista não passar. Registrar lacunas, deriva e artefatos; não substituir produção durante a prova.
2. **Seleção direta.** Preparar apenas regiões que intersectam o intervalo, com preroll necessário. Um corte dentro do bloco não é um corte seguro por simples cópia de vídeo: decodificar/recodificar a borda quando necessário. Preservar grade visual global, amostras e camadas; usar o compositor atual como referência.
3. **Contrato aditivo de sessão.** Application descreve geração, posição global, intervalo solicitado, disponibilidade e término; não conhece LibVLC, WPF, HLS ou pipes. Preservar `IPreparadorComposicao` como fallback e compatibilidade dos pacotes. Infrastructure fornece segmentos por conteúdo, valida origem, protege arquivos em uso e impõe orçamento. Desktop adapta a sessão ao motor e ao foco/anúncios existentes. Core e JSON/SQLite permanecem intactos.
4. **Agendador limitado.** Solicitação no cursor tem prioridade; testar inicialmente dois segmentos adiante, como parâmetro de laboratório, não decisão definitiva. Um worker de preparação e, se o transporte exigir, um muxer; não disparar FFmpeg por movimento de cursor. Trabalho concluído ainda válido pode permanecer no cache. Usar referência/lease para não evictar segmento consumido pelo player.
5. **Gerações e edição imediata.** Aplicar comando puro à sessão lógica sem esperar descarte nativo; invalidar geração de saída e solicitar cancelamento. A fila mantém somente a solicitação atual. O worker antigo termina antes de reutilizar seus recursos mutáveis; não basta remover o `await` atual. Snapshot antigo jamais atualiza player, cursor, progresso ou anúncios da nova geração. Fechamento continua aguardando descarte determinístico.
6. **Busca e intervalos no transporte aprovado.** Mapear tempo global para segmento e posição local, inclusive cursor em 02:30 de projeto de 10 minutos. Busca em entrada não seekable requer reiniciar a geração no destino; não simular seek nativo bem-sucedido. Se demuxer exigir seek em bytes, fornecer uma representação realmente seekable ou rejeitar o candidato. Pausa, retomada e fim da seleção devem respeitar o intervalo, sem depender apenas do timer de UI para cortar áudio.
7. **Comparação e integração.** Repetições alternadas, teste nativo e todas as suítes. Só então ativar o transporte escolhido e publicar pelo script oficial em `artifacts/desktop`, na branch atual. Manter uma rota conhecida de diagnóstico; não promover para produção um transporte que falhe nas junções.

## 8. Critérios de prova, riscos e métricas

**Continuidade:** sinal sintético contínuo com marcadores em cada fronteira, contagem de amostras, PTS/DTS e hash de quadros; testar B-frames/GOP, primeiro e último segmento, ausência de áudio e camadas. Capturar saída real por loopback quando disponível e confrontar com sinal de referência, distinguindo latência do dispositivo. Contadores de primeiro buffer não provam ausência de cortes ao longo da reprodução. Sem captura adequada, registrar essa parte como não comprovada. Timestamps devem permanecer monotônicos, sem amostras inseridas/perdidas nas junções da representação preparada; avaliar deriva A/V ao longo de 10 minutos e tolerância de apresentação compatível com o perfil de 30 fps.

**Precisão:** cortes racionais antes/depois de uma fronteira, fora de keyframes, intervalos muito curtos, recorte 4,99–5,01 s e cursor dentro de segmento. Não reinterpretar tempo VFR a partir do índice do proxy CFR. Navegação pausada continua usando timestamps reais; a prévia de 30 fps não vira referência para todos os quadros da origem.

**Disponibilidade:** atrasar intencionalmente o próximo segmento, simular disco lento, produtor mais lento que consumo, origem alterada/ausente, limite de cache e erro do processo. Nenhum arquivo parcialmente escrito pode ser anunciado como pronto. Com falta de dados, buffering deve ser explícito; não reproduzir silêncio ou quadro errado para esconder atraso.

**Concorrência e acessibilidade:** edições/undo/redo/buscas rápidas, cancelamento durante render e durante leitura bloqueada, fechar a janela e abrir outro projeto. Zero resultados de geração obsoleta apresentados; zero processos/handles retidos. Medir tempo do comando até mudança lógica, espera do índice e tempo até cancelamento efetivo. Testar teclado/foco/UIA, sem anúncio de cada bloco nem repetição de cancelamentos internos; NVDA falado continua aceite humano.

**Métrica principal:** entrada do comando → primeiro quadro/buffer da geração e posição solicitadas. Manter separado: preparação inicial, render de misses, mux, carregamento da biblioteca, criação do player, posicionamento e saída. Medir filas e disponibilidade antecipada, não apenas `Playing` ou retorno do método.

**Comparação:** repetir os 14 cenários anteriores e adicionar Play frio em cursor distante, seleção curta em projeto longo, múltiplas transições e consumidor alcançando produtor. Pelo menos cinco pares alternados exploratórios, publicando todas as amostras e mediana; ampliar amostragem antes de interpretar percentis de cauda. Reusar a mesma máquina/mídias/perfis e registrar carga/RAM; não reaproveitar uma execução antiga como controle estatístico de outra condição.

**Comprovação estrutural:** para uma mesma região inicial e mesma janela de prefetch, aumentar a duração distante do projeto não pode exigir renderizar/montar todas as regiões restantes antes do primeiro quadro. O trace deve mostrar somente segmentos necessários/preroll/prefetch no caminho crítico. Repetição e movimento de cursor sobre conteúdo pronto continuam com zero renderizações novas. Reportar bytes/processos iniciados antes da primeira saída, buffers em segundos, pico de RAM/disco e quantidade/duração dos stalls. Fixar metas de latência somente após a prova; não inventar percentuais.

## 9. Estado desta entrega

Consolidada a decisão de direção e os critérios que faltam para escolher transporte. As novas hipóteses de playlist/HLS/callbacks **não foram validadas por testes nativos nesta continuação**. Nenhum código de produção, contrato ou binário foi alterado; não houve nova execução de build, suíte ou publicação. Permanecem como evidência histórica os testes e benchmarks da entrega anterior, descritos em `resultado-latencia-reproducao.md`.

Revisão pelos critérios de `code-reviewer`: aprovar a direção de investigação e a seleção direta como próximo incremento; **não aprovar ainda a substituição do pipeline por playlist/HLS**, pois faltam provas de continuidade, busca e cancelamento. Próximo trabalho concreto: prova curta de segmentos AV locais e seleção direta, usando o cache atual e os testes existentes.
