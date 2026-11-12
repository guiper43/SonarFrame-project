# Recomposição incremental — investigação e resultados

## Causa e mudança

O cache anterior já considerava o conteúdo interno dos blocos, não somente as extremidades. O problema principal era a grade fixa na composição: remover um segundo no início transformava os recortes originais `[0,5)`, `[5,10)` etc. em outros recortes. IDs e nomes dos clipes também participavam da identidade, invalidando partes após divisão sem alteração audiovisual.

A segmentação agora procura fronteiras de cinco segundos **na mídia de origem**, preservando a grade global de 30 fps da prévia. Descontinuidades de origem são respeitadas; partes contíguas da mesma fonte são normalizadas sem modificar o projeto do usuário. Fronteiras que não caem na grade global não são adotadas como pontos de segmentação. Assim, não se reutiliza um vídeo cujo conteúdo teria outra fase de amostragem.

As chaves incluem perfil, identidade dos arquivos (caminho, tamanho, modificação e tipo), recortes inteiros, volumes pertinentes e todas as camadas relevantes em ordem e posições relativas ao segmento. Não incluem nomes ou IDs de edição, cursor, foco ou seleção. Dimensões da saída participam da chave visual. Mudanças de arquivo são verificadas novamente antes de publicar resultados.

Vídeo H.264 e áudio PCM ficam em caches separados. Volume e camadas exclusivamente sonoras não invalidam o vídeo. A mixagem, reamostragem e pré-roll de áudio são preservados. O áudio final é codificado uma única vez em AAC; o MP4 final copia os fluxos codificados. Não foi adotada concatenação de AAC codificado separadamente por trecho, evitando introduzir atrasos de encoder em cada junção.

## Hipóteses investigadas

| Hipótese | Resultado |
|---|---|
| 1. Invalidação global | Não havia descarte explícito de todos os blocos, mas os recortes mudavam em cascata após ripple. Corrigido com fronteiras por origem. |
| 2. Índice/posição na chave | Índice global não era serializado diretamente. Recortes decorrentes da grade global e IDs/nomes causavam misses. Removida identidade de edição. |
| 3. Só extremidades/vizinhos | A chave anterior já incluía todos os clipes e camadas recortados. Mantida comparação de conteúdo completo; nenhuma heurística de um vizinho. |
| 4. Deslocamento dos clipes | Remover/inserir no início pode manter os segmentos posteriores. Reuso comprovado por contagem e comparação completa da saída incremental/fria. |
| 5. Camadas absolutas | Convertidas para posições relativas no segmento; ordem, origem, duração e volume relevante permanecem na chave. Deslocamento incompatível invalida o segmento afetado. |
| 6. Áudio | Antes, vídeo/mixagem compartilhavam o processo. Agora são fases e caches distintos. AAC final ainda é codificado integralmente após uma mudança de composição. |
| 7. Montagem | Antes já usava cópia de vídeo, mas AAC era recodificado dentro da montagem. Agora codificação AAC e mux são medidos separadamente; montagem final usa cópia de ambos os fluxos. |
| 8. Interface | Foco/seleção/cursor não chamam renderização de composição; quadro pausado tem caminho próprio. Existe guarda de preparação ativa na janela. Preparações completas também foram serializadas no serviço; uma segunda chamada idêntica reaproveita o resultado. |
| 9. Granularidade | Mantido alvo de cinco segundos, ancorado na origem e com bordas menores quando necessário. Não foi criado cache hierárquico nem índice persistente novo. |
| 10. Evicção | LRU de fluxos fora da composição atual preservado, sob limite total de 2 GB. Removida redução antecipada do cache ao preparar intervalo. Desfazer/refazer reutiliza fluxos, mas remonta o arquivo final. |
| 11. Progresso | Percentuais correspondem a etapas concluídas, inclusive validação de hits, sem repetir o mesmo percentual. Contadores e tempos ficam no diagnóstico, não são anunciados pelo NVDA. |

## Diagnóstico

`PreparadorComposicaoFfmpeg.UltimoDiagnostico` expõe:

- `BlocosConsultados`, `CacheHits`, `CacheMisses`, `BlocosRenderizados`: um segmento é hit quando ambos os fluxos existem; miss quando falta pelo menos um. Em uma prévia inteira reutilizada, não há consultas de blocos: `PreviaReutilizada=true` e contadores zero.
- `VideosRenderizados` e `AudiosRenderizados`: permitem identificar misses parciais, como áudio após alteração de volume.
- `RenderizacaoVideoMs`, `MixagemAudioMs`: tempos de parede das etapas, incluindo configuração, busca/decodificação e codificação intermediária. MixagemAudioMs não pretende medir apenas CPU do filtro amix.
- `RenderizacaoComMixagemMs`: soma das duas fases; não deve ser somada novamente aos tempos individuais.
- `CodificacaoAudioMs`, `MontagemFinalMs`, `TotalPreparacaoMs`: AAC final, cópia/montagem/validação do arquivo, total da preparação. O total também inclui metadados, identidade, manifestos e gerenciamento de cache.

A janela grava a última tentativa em `%LOCALAPPDATA%/sonarFrame/diagnostico-reproducao.json`, com `TempoAteReproducaoMs` incluindo abertura do motor nativo, resultado, tempo adicional de preparação do intervalo e o diagnóstico da composição. A gravação não gera anúncios nem contém caminhos das mídias. O benchmark mede a preparação; não confundir esse tempo com a abertura nativa do player.

## Método do benchmark

Mídias sintéticas de 30 segundos, fonte 160×90 com áudio AAC a 48 kHz, três clipes de dez segundos; CFR de 30 fps e VFR alternando cadências de 15/30 fps. Máquina local com Intel Core i7-1165G7. Mesmo conjunto de arquivos, ordem de cenários e comandos antes/depois. O preparador permanece aberto durante cada sequência, incluindo preparação do original entre cortes: os resultados também refletem reaproveitamento de versões anteriores. Uma execução local por versão, com cache do sistema operacional e carga da máquina não controlados; os tempos não são garantia universal nem análise estatística.

Baseline: implementação baseada em `0ff220cd9fd53a6621a15775a91dc1615267cf0a`, com instrumentação, antes de modificar identidade/segmentação. Os dados estão em `.tools/test-artifacts/incremental/antes.json` e `depois.json`.

Reprodução do benchmark atual: `./validar-reproducao.ps1 -Incremental depois`. O parâmetro nomeia o relatório; não restaura o algoritmo antigo. `antes.json` é o registro histórico da execução anterior à otimização. Os scripts não fazem checkout nem modificam mídias do usuário.

## Limitações e compromissos

- Primeira preparação sem cache ficou mais lenta no benchmark devido a processos distintos de vídeo/áudio. O ganho está nas edições subsequentes; não há alegação de aceleração de todos os cenários.
- O arquivo final ainda é reconstruído e o AAC final ainda é recodificado após mudanças. Isso é trabalho global, mas não renderização global de imagem/mixagem das origens. Desfazer/refazer reaproveita fluxos intermediários, não versões completas do MP4.
- Reproduzir um intervalo ainda gera um recorte codificado da prévia completa. A preparação completa reutiliza o cache quando válida; o custo adicional do recorte aparece em `PreparacaoIntervaloMs`.
- Cortes não alinhados à grade de quadros podem invalidar muitos segmentos, pois sua equivalência visual não está comprovada. Precisão do projeto continua racional; o perfil da prévia permanece 30 fps.
- A política não procura subintervalos arbitrários dentro de blocos já codificados: bordas novas podem precisar de novos segmentos. Próximo ao fim, um corte pode gerar dois segmentos curtos onde antes se renderizava um bloco maior.
- Mudança da proporção do quadro determinada pela primeira mídia visual invalida o vídeo adequadamente. Fontes modificadas e mudanças de perfil também impedem reutilização.
- Cache permanece por sessão e é removido ao descartar o preparador. A navegação por quadros continua lendo as fontes e timestamps reais; não depende do cache de reprodução.
- A impressão subjetiva de latência, continuidade audiovisual e anúncios precisa de validação com as mídias reais do usuário e NVDA.

## Validação e revisão

O teste `RippleDivisaoEVolumeReutilizamConteudoComCamadasRelativas` cobre VFR, divisão sem renderizar, nomes/IDs, corte inicial com camada deslocada, comparação framemd5 integral (vídeo e áudio) entre cache quente e preparação fria, volume integral/parcial sem renderizar vídeo, deslocamento de camada, corte fracionário e inserção inicial. Os testes existentes de junções, codecs, camadas e cancelamento são preservados. O teste de cache com imagem foi atualizado para reconhecer deduplicação de fluxos idênticos, não presumir um arquivo por segmento.

Revisão pelos critérios de `code-reviewer`: identidade audiovisual completa, grade temporal racional, publicação de fluxos somente após êxito, preservação de originais, cancelamento do FFmpeg, limites de cache e ausência de novas dependências de WPF nas camadas portáveis. Nenhum defeito confirmado pendente no diff; os compromissos de desempenho acima permanecem explícitos. Veredito de código: **Pode aprovar**; aceitação de desempenho nas mídias reais e NVDA permanece externa.

- `./build.ps1`: zero avisos/erros, 81 testes de domínio/aplicação/infraestrutura e 29 testes WPF aprovados.
- `./testar-previa-desktop.ps1`: cinco testes aprovados, nenhum ignorado.
- `./validar-reproducao.ps1`: dez formatos aprovados, incluindo MP4/MOV, imagens e MP3/WAV/AAC/Vorbis/Opus.
- `./run-desktop.ps1 -Publish`: publicação concluída em `artifacts/desktop`, substituindo a versão anterior.
- `git diff --check`: sem erros.

Arquivos centrais: `CacheBlocosComposicao.cs`, `IdentidadeComposicao.cs`, `PreparadorComposicaoFfmpeg.cs`, `IntervaloComposicaoFfmpeg.cs` e `DiagnosticoPreparacao.cs` em Infrastructure; `JanelaPrincipal.Composicao.cs` para tempo até reprodução e diagnóstico; `TestesCacheIncremental.cs` e `TestesPreparacaoComposicao.cs`; `BenchmarkIncremental.cs`, `Programa.cs` e `validar-reproducao.ps1` para reprodução das medições. As alterações anteriores da timeline e governança foram preservadas.

## Comparação registrada (milissegundos)

| Fonte | Cenário | Antes (ms) | Depois (ms) | Segmentos renderizados antes → depois |
|---|---|---:|---:|---|
| cfr | primeira | 5665 | 7380 | 6 → 6 |
| cfr | segunda | 1 | 1 | 0 → 0 |
| cfr | cursor-selecao | 1 | 1 | 0 → 0 |
| cfr | corte-0 | 3753 | 1315 | 6 → 1 |
| cfr | corte-14 | 3466 | 2039 | 2 → 1 |
| cfr | corte-28 | 4986 | 3497 | 1 → 2 |
| cfr | divisao | 5529 | 0 | 2 → 0 |
| cfr | reordenacao | 917 | 1859 | 0 → 0 |
| cfr | volume | 4125 | 5818 | 2 → 2 |
| cfr | camada-inclusao | 3663 | 3382 | 1 → 1 |
| cfr | camada-remocao | 2816 | 2653 | 0 → 0 |
| cfr | desfazer | 2950 | 2508 | 0 → 0 |
| cfr | refazer | 908 | 1175 | 0 → 0 |
| cfr | sucessivas | 4186 | 1814 | 6 → 1 |
| vfr | primeira | 6721 | 8026 | 6 → 6 |
| vfr | segunda | 0 | 0 | 0 → 0 |
| vfr | cursor-selecao | 0 | 0 | 0 → 0 |
| vfr | corte-0 | 9094 | 1315 | 6 → 1 |
| vfr | corte-14 | 3239 | 2086 | 2 → 1 |
| vfr | corte-28 | 3834 | 3080 | 1 → 2 |
| vfr | divisao | 4118 | 0 | 2 → 0 |
| vfr | reordenacao | 1045 | 2338 | 0 → 0 |
| vfr | volume | 4426 | 6010 | 2 → 2 |
| vfr | camada-inclusao | 4204 | 3080 | 1 → 1 |
| vfr | camada-remocao | 3997 | 2570 | 0 → 0 |
| vfr | desfazer | 3921 | 2491 | 0 → 0 |
| vfr | refazer | 1991 | 868 | 0 → 0 |
| vfr | sucessivas | 4471 | 1026 | 6 → 1 |

Zero milissegundos na tabela significa valor arredondado abaixo de 0,5 ms. Em volume, os dois segmentos processados depois são somente áudio: zero segmentos de vídeo foram renderizados. O corte inicial reutilizou cinco dos seis segmentos, em CFR e VFR.

Houve regressões de tempo em preparação fria, reordenação e volume nesta rodada; não foram ocultadas. Na mudança de volume CFR, 1843 ms ficaram no processamento de áudio, 2788 ms no AAC final e 1174 ms na montagem/validação, apesar de zero renderização visual. Portanto, menos renderização não implica menor tempo total em todo cenário. O alvo principal — corte inicial e alterações sucessivas sem recompor o restante — apresentou redução real de tempo e trabalho.

No corte inicial CFR, a preparação final de 1315 ms incluiu aproximadamente 391 ms de vídeo, 67 ms de áudio, 699 ms de AAC e 145 ms de montagem. A renderização não corresponde ao custo inteiro da preparação.
