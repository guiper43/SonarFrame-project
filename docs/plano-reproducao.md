# Plano da feature: reprodução e prévia

Data: 01/11/2026. Base analisada: `7e36d52`, versão 0.1.0.
Estado: planejamento; implementação não iniciada. Exportação fica em outra feature.

## Objetivo e limites

Permitir ouvir/ver uma mídia antes de inseri-la e conferir a montagem real, respeitando ordem, recortes, imagens, volume e camadas, por teclado e com NVDA. Entregar capacidades utilizáveis em incrementos; cada incremento declara exatamente o que consegue reproduzir.

Referências funcionais: RF01.08, RF03 (áudio), RF05.01–07 e RF05.09–10, RNF01. A ERS continua sendo a referência normativa. Este plano não declara RF03 ou RF04 integralmente implementados.

Ficam fora: exportação MP4, interface de exportação, legendas, IA, fades, transições, novas ferramentas de posicionamento visual, instalador e alterações cosméticas gerais. Preparar mídia temporária para tocar a composição é processamento interno de prévia, não uma entrega de exportação.

## Base existente

- Core: projeto e coleções imutáveis, tempo racional, clipes com início na origem, camadas absolutas, volumes de 0 a 200%, comandos validados.
- Application: sessão, histórico, seleção e cursor separados; `IReprodutorPrevia` e `INavegadorQuadros` existem sem implementação real. `Posicionar(..., reproducao: true)` evita anúncios, mas não emite evento para atualizar a interface.
- Infrastructure: FFprobe, SQLite com JSON versionado e recuperação. Nomes JSON antigos são preservados; não alterar o formato por conveniência da reprodução.
- Desktop: WPF em code-behind dividido em arquivos parciais. A região de prévia ainda é informativa. Manter esse padrão; não migrar a aplicação inteira para MVVM.
- Distribuição: .NET 10, Windows x64. O desktop atualmente copia FFprobe, mas não FFmpeg.
- Histórico recente: `77857fe` adiciona ações/camadas/compatibilidade; `8242fa3` liga os fluxos ao desktop; `7e36d52` prepara artefatos 0.1.0. Não havia diffs locais ao iniciar a análise.

## Comportamento proposto

### Biblioteca

Selecionar um item não inicia reprodução. Um comando explícito abre sua prévia na região existente. Vídeos e áudios usam o arquivo original; imagens são exibidas estaticamente, sem inventar duração na biblioteca. A duração definida ao inserir uma imagem vale apenas na composição. Prévia da biblioteca não altera cursor, seleção ou histórico do projeto. Seleção múltipla exige escolher um único item para prévia.

### Composição

Reproduzir começa no cursor; pausar conserva a posição. Ao chegar ao fim, parar no fim e anunciar uma única vez; um novo comando Reproduzir reinicia do início. Parar volta ao início do contexto ativo. Trocar biblioteca/composição interrompe a reprodução anterior: nunca manter dois áudios ativos.

Disponibilizar Reproduzir/Pausar, Parar, Consultar posição e Reproduzir seleção. A seleção usa intervalo semiaberto e para em seu fim; não há repetição automática. Uma seleção vazia desabilita esse comando. Para conferir cortes, navegar para antes do corte e reproduzir ou selecionar um intervalo que o atravesse.

Navegação manual pausa, posiciona e apresenta o novo quadro; reprodução só retorna por comando explícito. Buscar repetidamente cancela ou invalida buscas antigas, prevalecendo a última. No fim exato, conservar cursor na duração e apresentar o último quadro válido. Áudio sem vídeo apresenta área neutra com identificação acessível; imagens na composição duram o intervalo do clipe.

Antes de editar, desfazer/refazer, religar mídia ou trocar projeto, pausar. Alterações de composição invalidam a prévia preparada. Nunca reproduzir silenciosamente uma versão anterior das edições. Salvar não precisa interromper a reprodução. Fechar cancela preparação e libera processos, eventos, arquivos e dispositivos.

### Teclado e leitor de telas

Manter Alt+4 e navegação de regiões. Espaço alterna reprodução somente no controle temporal e na área apropriada da prévia; listas, botões, textos e diálogos mantêm teclas nativas. Os controles oferecem nomes e estados por UI Automation.

Anunciar início, pausa, conclusão, falha e preparação; não anunciar cada atualização de tempo. Consultar posição fala contexto, posição e duração sob demanda. Navegação manual anuncia o destino confirmado. Atualizar apenas posição/estado durante reprodução, sem reconstruir listas ou retirar foco. Não chamar `Atualizar()` da janela inteira a cada evento do motor.

### Composição audiovisual

Sequência principal contínua, preservando recortes e sincronismo do áudio original. Misturar todas as camadas de áudio ativas e áudio dos vídeos, aplicando volume individual sem normalização automática que anule a proporção escolhida. Documentar e testar saturação quando a soma exceder a faixa; qualquer limitador adicional exige decisão explícita.

Proposta para camadas visuais existentes, ainda sem propriedades de layout: quadro de prévia com proporção da primeira mídia visual principal; somente áudio usa quadro neutro. Centralizar e preservar proporção com bordas, sem cortar; compor camadas na ordem armazenada, última acima, preservando transparência. Não persistir isso como orientação de exportação. Confirmar essa convenção antes do incremento de camadas, pois o modelo atual não define empilhamento nem enquadramento. Informar sobreposições acessivelmente sem repetir avisos durante cada quadro.

## Estratégia técnica

Candidato inicial: LibVLCSharp/WPF para apresentação e reprodução de uma fonte; FFmpeg para preparar uma prévia composta em cache local. Uma instância por contexto ativo, não um player independente por camada. Essa divisão evita tratar múltiplos relógios de players como um mixer sincronizado.

O motor só será fixado após a prova técnica do incremento 0. LibVLCSharp oferece WPF e APIs/eventos de reprodução; isso não garante precisão de seek ou acessibilidade no projeto. FFmpeg fornece trim/atrim, setpts/asetpts, concat, volume, adelay, amix e overlay. O plano de composição deverá normalizar timestamps e formatos, inclusive trechos sem áudio/vídeo, antes de unir fontes.

Começar com preparação completa do snapshot de composição antes de tocar. É uma escolha de simplicidade e consistência, com custo de espera e disco. Mostrar preparação cancelável; a UI continua responsiva. Preparação por segmentos e streaming ficam como otimização futura, salvo se a prova técnica demonstrar que a estratégia completa não atende aos casos de uso. Não prometer reprodução instantânea após cada edição.

### Responsabilidades e arquivos previstos

- Core: preservar `Projeto`, `Clipe`, `CamadaTemporal` e `TempoMidia`; adicionar regras somente quando necessárias à semântica aprovada.
- Application: ampliar `IReprodutorPrevia` com estado, posição/duração, eventos de término/falha, parada e descarte; definir fonte de biblioteca/composição explicitamente. Adicionar coordenador de reprodução, identificador de geração e contrato de preparação. Evitar tipos WPF/LibVLC no contrato.
- Infrastructure: adaptador do motor, preparação FFmpeg, cache, consulta de timestamps FFprobe; se necessário, isolar adaptador Windows em projeto próprio para preservar os pacotes portáveis existentes.
- Desktop: `JanelaPrincipal.Reproducao.cs`, controles na região existente e conexão ao ciclo de vida. Marcar eventos com geração/contexto e encaminhar pelo Dispatcher.
- Tests: coordenador com motor falso, integração audiovisual com FFmpeg/FFprobe e testes WPF. Testes do motor nativo separados de testes determinísticos sem dispositivo de áudio.
- Scripts: dependências fixadas, locks, checksums, cópia de FFmpeg/runtime nativo e avisos de terceiros; validar ZIP em ambiente sem SDK e sem dependências no PATH.

### Concorrência, cache e persistência

Estados: sem mídia, preparando, pronto, reproduzindo, pausado, concluído e erro. Cancelamento volta a um estado utilizável, nunca a erro genérico. Operações são serializadas; geração nova invalida resultados/eventos de geração anterior, mesmo quando o motor não consegue cancelar a busca física.

O relógio do motor é a referência durante reprodução; timer da UI apenas consulta/apresenta posição. Limitar atualização visual, inicialmente a até 10 Hz. Atualizar cursor da sessão sem inserir histórico ou emitir anúncios. Manter a semântica atual de persistência do cursor; cache não depende dele. Testar o salvamento de snapshots enquanto a reprodução avança.

Chave de cache: composição, recortes, volumes, ordem das camadas, perfil da prévia, versão do preparador e identidade dos arquivos (caminho, tamanho e modificação; hash adicional quando necessário). Excluir cursor, seleção, nome do projeto e seleção de item da chave. Validar fontes antes da reprodução e detectar mudanças durante preparação.

Arquivos temporários fora do SQLite e das pastas de mídia original; publicação atômica de cache concluído. Limite inicial proposto de 2 GB com remoção dos menos usados, sem remover arquivo em reprodução; avisar e cancelar se um item não couber, preservando projeto. Limpar resíduos incompletos ao iniciar. Nunca executar exclusão fora da pasta exclusiva de cache.

FFmpeg via argumentos estruturados, sem shell; drenar stdout/stderr, tratar cancelamento e encerrar processo filho. Ausência de motor, mídia inacessível, falha de decode, falta de espaço e dispositivo indisponível devem gerar mensagem acionável. Não ignorar camada ausente e tocar montagem incompleta como se estivesse correta.

### Precisão temporal

Preservar tempo racional no domínio. Converter unidades apenas nas bordas do motor. Implementar índice de timestamps por mídia para VFR, cancelável e em segundo plano; não bloquear UI no contrato síncrono atual de `INavegadorQuadros`. Preparar índice antes de habilitar passo por quadro ou evoluir o contrato explicitamente.

Seek aproximado não pode ser anunciado como quadro exato. Para passo manual, resolver o timestamp da origem e apresentar quadro decodificado correspondente, usando extração específica se o motor não garantir isso. Prévia composta pode usar taxa fixa para fluidez, mas não deve redefinir a navegação VFR da origem. Validar fronteiras entre clipes, recortes fora de keyframes e clipes curtos.

## Entregas incrementais

| Incremento | Entrega | Critério para concluir |
|---|---|---|
| 0 — Prova técnica | WPF + motor candidato, seek, eventos, áudio, pausa, descarte e prévia FFmpeg curta | Demonstrar MP4/MOV, áudio, foto, corte fora de keyframe e mix de duas fontes; registrar precisão, latência, espaço, foco/NVDA e distribuição. Fixar arquitetura ou revisar este plano antes de integrar |
| 1 — Prévia da biblioteca | Reprodução real de um item, imagem estática, controles e erros acessíveis | Ouvir/ver antes de inserir; trocar mídia sem sobreposição sonora; não alterar projeto; teclado e cancelamento funcionam |
| 2 — Sequência principal | Prévia preparada com recortes, reordenação, fotos e volumes, cursor e seleção | Corte e desfazer/refazer refletem na reprodução, áudio/vídeo sincronizados, preparação cancelável e cache correto. Com camadas presentes, bloquear composição com explicação até incremento 3 |
| 3 — Camadas | Mix de áudio e composição visual segundo convenção confirmada | Respeitar início/fim, origem e volume; camadas após corte continuam no tempo certo; sobreposições determinísticas; nenhuma camada omitida silenciosamente |
| 4 — Navegação precisa | Índice VFR, passo de quadros, buscas rápidas e revisão de cortes | Quadro exibido corresponde ao destino; última busca vence; sequência de passos e fronteiras correta em CFR/VFR |
| 5 — Distribuição e validação | Testes reais, percurso NVDA, artefato portátil e documentação | Funcionar sem SDK/FFmpeg no PATH, sem processos residuais; erros recuperáveis; escopo e limitações registrados |

Dependências: 0 → 1 → 2 → 3; 4 depende de 2 e deve ser revalidado com 3; 5 fecha o conjunto. Cada incremento funcional inclui testes, documentação e artefato quando distribuído. Usar pré-releases para capacidades parciais; reservar versão final da feature para os critérios completos. Não antecipar a versão nem criar tags durante planejamento.

## Validação e critérios de aceite finais

- Unitários: estados, término, seleção, comandos durante reprodução, eventos atrasados, cancelamento, erros e descarte; navegação não adiciona histórico; cursor não invalida cache.
- Composição: mídias sintéticas com quadros numerados e pulsos sonoros; verificar conteúdo antes/depois do corte, duração com tolerância declarada de um quadro/um bloco de áudio e sincronismo sem deriva acumulada. Validar recorte, volumes 0/100/200, silêncio, fotos e camadas simultâneas.
- Formatos: todos os formatos de importação atuais, vídeo sem áudio, áudio isolado, VFR/CFR, MOV de celular com rotação, resoluções/proporções diferentes, Unicode e espaços nos caminhos, arquivos ausentes/corrompidos. Mídia sintética não substitui amostras reais.
- UI: Espaço contextual, Alt+4, F6, preservar foco, nomes/estados acessíveis, consulta de posição, erros e progresso sem fala contínua. Percurso manual com NVDA obrigatório; testes WPF atuais não o substituem.
- Robustez: fechar/trocar projeto preparando ou tocando; editar/desfazer e buscar rapidamente; dispositivo indisponível; pouco disco; cache obsoleto; cancelar e tentar novamente; reabrir projetos 0.1.0 sem migração desnecessária.
- Desempenho: medir na prova técnica projetos de 1, 10 e 30 minutos e registrar hardware. Metas iniciais para prévia já preparada: comandos respondem visualmente em até 100 ms, início em até 1 s, seek em até 500 ms, sem travamento da UI; confirmar viabilidade antes de tratá-las como garantia. Preparação tem medição própria, progresso e cancelamento, sem estimativa fictícia.
- Entrega: documentação explica o que se reproduz, espera de preparação, cache e limites; originais preservados; reprodução local sem IA/rede; exportação permanece indisponível.

## Decisões a fechar durante a execução

1. Motor e versões após prova técnica; fallback só será selecionado se houver falha demonstrada nos critérios.
2. Confirmar convenção visual das camadas antes do incremento 3; registrar qualquer ajuste funcional na ERS.
3. Definir perfil de resolução/taxa da prévia e orçamento de cache a partir das medições, sem confundir com configurações da futura exportação.
4. Verificar se o caminho de precisão exige extração de quadros separada e ajustar contrato assíncrono de navegação.

## Referências técnicas consultadas

- [LibVLCSharp: integração e requisitos WPF](https://docs.videolan.me/libvlcsharp/docs/getting_started.html)
- [LibVLCSharp: MediaPlayer](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.MediaPlayer.html)
- [FFmpeg: filtros de composição e áudio](https://ffmpeg.org/ffmpeg-filters.html)

Essas APIs sustentam a proposta; desempenho, fidelidade da composição e funcionamento com NVDA ainda exigem a prova no SonarFrame.
