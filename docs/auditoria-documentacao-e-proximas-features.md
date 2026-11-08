# Documentação e próximas features

Data: 07/11/2026. Base: `bcc6fa9`, branch `feat/reproducao-e-previa`, sem alterações locais no início da revisão.

> Atualização de contexto em 08/11/2026: esta auditoria preserva o retrato de `bcc6fa9`. A implementação posterior de exportação na branch `feature/exportacao` está registrada em [plano-exportacao.md](plano-exportacao.md); as classificações abaixo não descrevem essas alterações posteriores.

## Alcance da revisão

Comparação estática entre ERS, README, guias de núcleo/desktop, planos de reprodução, código e testes existentes. Não constitui auditoria exaustiva de todos os critérios de aceitação nem certificação de funcionamento. Os testes citados foram inspecionados, não executados nesta revisão documental. A disponibilidade descrita vale para esta branch; não comprova promoção para `develop`, `homolog`, `main` ou release publicada.

A ERS é normativa: “aprovado” significa requisito aprovado, não recurso entregue. `release.md` registra o escopo histórico 0.1.0. Planos e resultados datados preservam decisões e medições da etapa correspondente.

## Estado funcional encontrado

| Área | Estado nesta branch | Evidência |
|---|---|---|
| Importação e biblioteca, RF01 | Implementadas para áudio, vídeo e imagens; guia de legendas ainda informativa | `DialogoImportacao.cs`; `ImportacaoMidias.cs`; `TestesImportacaoMidias.cs`; `TestesPreviaBiblioteca.cs` |
| Montagem e histórico, RF02 | Implementados; aceite de anúncios com NVDA permanece externo | `ComandosEdicao.cs`; `SessaoEdicao.cs`; teste `DividirMoverExcluirDesfazerERefazerPreservamOrigem` |
| Mixagem e volume, RF03 | Parcial: fontes simultâneas e volume 0–200%; faltam fades | `PreparadorComposicaoFfmpeg.cs`; teste `CamadasRespeitamIntervaloTransparenciaEmpilhamentoEMixSemNormalizacao` |
| Imagens e camadas, RF04.11–17 | Parcial: intervalo, sobreposição, transparência e centralização; faltam nove posições, tamanhos e modo substitutivo configuráveis | `Projeto.cs`; `ComandosCamadas.cs`; `QuadrosComposicaoFfmpeg.cs`; modelo sem propriedades de layout |
| Legendas locais/IA e efeitos, RF04 | Não implementados como fluxos do produto | `DialogoImportacao.cs:55`; modelo `Projeto` sem legendas, transições ou fades; configurações atuais contêm apenas diretório de projetos |
| Navegação e reprodução, RF05 | Implementadas, com cobertura automatizada existente; precisão/desempenho dependem do cenário | `IndiceQuadros.cs`; `JanelaPrincipal.Navegacao.cs`; `TestesPrecisaoReproducao.cs`; `TestesNavegacaoTimeline.cs` |
| Reprodução progressiva | Integrada; não é uma próxima feature a construir do zero | `ReproducaoProgressiva.cs`; `EntradaComposicaoVlc.cs`; teste `FluxoReutilizaBlocosEPreservaAudioEQuadros` |
| Exportação, RF06 | Não implementada; contrato e validação de opções já existem | `Contratos.cs:27–38`; `JanelaPrincipal.xaml.cs:402` informa indisponibilidade |
| Projetos, GP | Persistência, catálogo, autosave, recuperação e religação implementados; aceite externo não inferido | `PersistenciaProjeto.cs`; `RepositorioProjetosSqlite.cs`; `TestesPersistencia.cs`; `TestesBibliotecaProjetos.cs` |
| Acessibilidade, RNF01 | Parcial no produto completo: fluxos existentes têm testes WPF; faltam fluxos futuros e aceite integral com NVDA | `TestesIntegracaoEditor.cs`; `TestesMenu.cs`; `TestesDialogoImportacao.cs`; `plano-entrada-continua.md` |
| Injeção de dependências | Implementada, não backlog novo | `Configuracao/RegistroDependencias.cs`; `TestesInjecaoDependencias.cs`; ADR 0001 |

Os caminhos de código/testes na tabela são nomes de arquivos sob `src` e `tests`. A presença de testes não equivale a resultado aprovado nesta revisão.

## Divergências documentais

1. **Médio — guia de núcleo:** `core.md` ainda classificava reprodução com camadas como pendente. Isso induzia planejamento duplicado. Atualizado o estado da branch, preservando a distinção da versão histórica.
2. **Médio — guia desktop:** instruía usar um botão “Reproduzir composição” ausente no XAML atual e misturava a montagem completa MOV com a entrada progressiva NUT. Atualizados os controles e a distinção entre o caminho atual e o alternativo de arquivo completo.
3. **Médio — decisões técnicas:** ERS §6.2 e README apresentavam biblioteca de prévia como escolha em aberto; SQLite, extensão e pasta padrão também já têm implementação. Acrescentado registro técnico na ERS, sem alterar requisitos funcionais nem declarar aceite NVDA concluído.
4. **Baixo — organização do README:** checklist genérico não distinguia implementação existente de validação pendente. Acrescentada referência a este retrato da branch e atualizada a descrição da decisão do motor.
5. **Baixo — exemplos de artefatos:** `core.md` direcionava amostra para `artifacts`, contrariando a política atual. Corrigido para `.tools/test-artifacts`.

Os relatórios históricos de benchmarks não devem ser sobrescritos com números novos nem interpretados como medições atuais. O plano inicial de reprodução preserva deliberadamente seus critérios originais; a evolução está em `plano-entrada-continua.md`.

## Ordem proposta para próximas entregas

Esta ordem é proposta de priorização, não aprovação de novo escopo nem início de implementação.

| Ordem | Entrega | Requisitos / aceite | Limite e condição de conclusão |
|---|---|---|---|
| 0 | Fechar homologação da reprodução atual | RF05, RNF01; incremento 5 do plano de reprodução | Percurso real com NVDA/teclado, mídias reais e máquina limpa; registrar evidências separadas da suíte automatizada |
| 1 | Exportar a montagem atual para MP4 | RF06; CA-RF06-01 a 03; parte de CA-RNF01-01/07 | 720p/1080p, orientação obrigatória 16:9 ou 9:16, preencher/bordas, destino, progresso e ações finais. Preservar cortes, volumes e camadas; validar duração e sincronismo no arquivo final |
| 2 | Legendas SRT locais e edição contextual | RF04.01–02, 07–11; CA-RF04-01/04/05; CA-RNF01-05 | Importar, persistir, editar texto/estilo/alcance, apresentar na prévia e exportação; preservar foco e pausa. Sem geração por IA e sem criação manual do zero |
| 3 | Propriedades de imagens e elementos visuais | RF04.11–17; CA-RF04-06 | Nove posições, tamanhos, sobreposição/substituição e preencher/bordas; coerência entre prévia e exportação, histórico e compatibilidade dos projetos |
| 4 | Fades independentes de áudio e vídeo | RF03.07, RF04.20/23; parte de CA-RF04-07 | Configurar, ouvir/ver, alterar e remover; definir regras para durações maiores que o trecho antes de implementar |
| 5 | Cinco transições aprovadas | RF04.18–19/21–23; restante de CA-RF04-07 | Presets de 0,3/0,8/1,5 s, descrição acessível e prévia/exportação equivalentes; resolver duração e sobreposição em clipes curtos no plano |
| 6 | Geração opcional de legendas por IA | RF04.03–06/25–33; CA-RF04-02/03/08 | Depende do modelo local de legendas; consentimento, confirmação, conflitos, chave protegida e toggle desligado por padrão |
| 7 | Instalador e preparação de distribuição | DT01; decisão de empacotamento | Escolher MSIX/Inno Setup, validar máquina limpa e documentar dependências; pode avançar em paralelo ao roadmap funcional quando houver escopo de release definido |

Exportação é a prioridade funcional proposta porque fecha o fluxo de produzir um vídeo utilizável fora do editor. A preparação da prévia de 640 pixels/30 fps não deve ser reutilizada como arquivo final de 720p/1080p: planejar renderização a partir dos originais e compartilhar as regras de composição quando apropriado.

Antes de cada implementação, criar `docs/plano-<feature>.md` com incrementos, contratos por camada, critérios de aceitação e decisões ainda abertas. RF07 continua reservado. Nenhuma alteração de `JsonPropertyName` existente deve ser necessária para introduzir as novas capacidades.

## Veredito documental

**Pode aprovar com ajustes:** as divergências identificadas nos guias foram corrigidas nesta revisão; o aceite funcional integral continua pendente das validações externas e das features ainda ausentes. Nenhum código foi alterado, build/testes não foram executados e não foi gerada publicação desktop nesta tarefa.
