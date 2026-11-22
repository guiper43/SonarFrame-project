# Plano da feature: legendas SRT locais e edição contextual

Data: 15/11/2026. Base: `733163e` (`feature/fades`), branch `feature/legendas-srt`.

## Objetivo e fronteira de escopo

Permitir importar arquivos externos de legenda `.srt` (SubRip) sem necessidade de chave de API ou configuração de IA, renderizar as legendas na prévia, na extração de quadros e na exportação MP4, e editar texto e formatação visual de uma legenda ativa na posição do cursor em um diálogo único operável 100% por teclado com suporte a escopo individual ou global e às 9 posições visuais predefinidas.

Fora desta entrega: transcrição automática por IA / Gemini (`RF04.03–05`, `RF04.24–27`), posicionamento/escala de camadas visuais (`RF04.12–17`) e transições entre clipes (`RF04.18–19`, `RF04.21–22`).

## Mapeamento de requisitos da ERS

| Requisito / Critério | Tratamento |
|---|---|
| RF04.01 | Importação de arquivos externos `.srt` (SubRip) pela terceira guia de `DialogoImportacao` (**Legendas e efeitos**) para aplicação na linha do tempo |
| RF04.02 | Importação, exibição, edição e exportação de legendas `.srt` funcionam integralmente offline, sem chave de API ou configuração de IA |
| RF04.06 | Quando o projeto já possui legendas, `DialogoImportacao` pergunta ao usuário se deseja substituí-las ou cancelar a operação |
| RF04.07 | Quando a reprodução está pausada sobre um trecho com legenda ativa (`Inicio <= Cursor < Fim`), o menu contextual da linha do tempo (`Shift+F10` / tecla Aplicações) disponibiliza `Editar legenda…` |
| RF04.08 | A edição de legenda abre uma caixa de diálogo única contendo alteração do texto e controles de formatação visual (fonte, tamanho, cor, negrito, itálico, fundo e posicionamento) |
| RF04.09 | O diálogo de edição de legenda permite aplicar o estilo visual apenas à legenda selecionada ou a todas as legendas do projeto |
| RF04.10 | Confirmar ou cancelar a edição da legenda preserva o estado de pausa, a posição temporal atual (`Cursor`) e restaura o foco à linha do tempo |
| RF04.11 | O posicionamento visual oferece as 9 opções predefinidas (`SuperiorEsquerda`, `SuperiorCentro`, `SuperiorDireita`, `CentroEsquerda`, `Centro`, `CentroDireita`, `InferiorEsquerda`, `InferiorCentro`, `InferiorDireita`), com `InferiorCentro` como padrão |
| GP08 | Persistência das legendas e estilos no arquivo `.sonarframe` sem quebrar a compatibilidade de projetos anteriores |
| CA-RF04-01 | Importação de `.srt` aplica textos e intervalos temporais na linha do tempo sem exigir chave de API |
| CA-RF04-04 | Pausado sobre legenda ativa, `Shift+F10` exibe `Editar legenda…`; diálogo único altera texto, estilo e escopo, retornando o foco à timeline ao confirmar ou cancelar |
| CA-RF04-05 (legendas) | As 9 posições predefinidas estão disponíveis para legendas, com inferior centro como padrão |
| CA-RNF01-05 (legendas) | Abrir e fechar o diálogo de edição de legenda e a confirmação de substituição preserva o foco lógico de retorno |

## Decisões arquiteturais por camada

- **Core (`SonarFrame.Core`):**
  - `PosicaoVisual`: enum com as 9 posições predefinidas (`InferiorCentro` padrão).
  - `EstiloLegenda`: record imutável com `Fonte`, `Tamanho`, `Cor` (`#RRGGBB`), `Negrito`, `Italico`, `Fundo` e `Posicao` (`[JsonPropertyName]` em inglês).
  - `Legenda`: record imutável com `Id`, `Inicio`, `Fim`, `Texto` e `Estilo`.
  - `Projeto`: propriedade `Legendas` (`ImmutableArray<Legenda>`) sempre inicializada para `[]` em memória e serializada via `LegendasSerializadas` (`[JsonPropertyName("Subtitles"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`) para omitir a propriedade no JSON quando não há legendas, preservando `TestesCompatibilidade`.
  - Comandos `DefinirLegendas` e `EditarLegenda` (com `EscopoEstiloLegenda.ApenasSelecionada` ou `TodasDoProjeto`), além do recorte/deslocamento proporcional de legendas em `ExcluirIntervalo`.
- **Application (`SonarFrame.Application`):**
  - `LeitorSrt`: parser determinístico de arquivos `.srt` com suporte a múltiplos blocos, quebras de linha, separadores `,` ou `.` nos milissegundos e mensagens claras de validação.
  - `AcoesEdicao`: métodos `LegendaAtiva()`, `DefinirLegendas(...)` e `EditarLegenda(...)`.
- **Infrastructure (`SonarFrame.Infrastructure`):**
  - `IdentidadeComposicao` e `CacheBlocosComposicao`: incluem as legendas visíveis de cada bloco na chave de vídeo (`audio != true`), sem invalidar blocos de áudio (`audio == true`), e recortam as legendas para a janela `[inicio, fim)` de cada bloco.
  - `PreparadorComposicaoFfmpeg` e `QuadrosComposicaoFfmpeg`: geram arquivo `.ass` temporário (removido em `finally`) com estilos `[V4+ Styles]` correspondentes a fonte, tamanho proporcional, cor, negrito, itálico, caixa de fundo (`BorderStyle=3` vs `1`) e alinhamento `1..9`, aplicando o filtro `ass` nos blocos de prévia, nos quadros estáticos e na exportação MP4.
- **Desktop (`SonarFrame.Desktop`):**
  - `DialogoImportacao`: habilita a guia **Legendas e efeitos** para selecionar arquivo `.srt`, solicitando confirmação acessível antes de substituir legendas existentes (`RF04.06`).
  - `JanelaPrincipal`: adiciona `MenuEditarLegenda` ao menu contextual da timeline (`Shift+F10`) e `BotaoEditarLegenda` na região de propriedades, habilitados quando pausado sobre legenda ativa, abrindo `CriarDialogoLegenda` e restaurando foco e cursor ao fechar.

## Divisão em incrementos verificáveis

| Incremento | Entrega | Verificação |
|---|---|---|
| 1 | Domínio (`PosicaoVisual`, `EstiloLegenda`, `Legenda`, `Projeto.Legendas`, `DefinirLegendas`, `EditarLegenda`, `ExcluirIntervalo`) e parser `LeitorSrt` | Testes unitários em `SonarFrame.Tests` cobrindo leitura de `.srt`, validações, escopo individual/global, exclusão de intervalo, desfazer/refazer e persistência SQLite/JSON |
| 2 | Renderização FFmpeg via `libass` em blocos de prévia, quadros estáticos e exportação MP4, com cache incremental | Testes com FFmpeg real validando renderização visual da legenda, preservação de cache de áudio e exportação MP4 |
| 3 | Interface WPF (`DialogoImportacao` guia SRT com confirmação de substituição, `MenuEditarLegenda` em `Shift+F10`, diálogo único `CriarDialogoLegenda`, 9 posições e restauração de foco/pausa/cursor) | Testes STA em `SonarFrame.Desktop.Tests` cobrindo importação `.srt`, confirmação de substituição, edição por `Shift+F10`, escopo e retorno de foco |
| 4 | Validação completa, publicação em `artifacts/desktop` e revisão `code-reviewer` | `./build.ps1`, `./testar-previa-desktop.ps1`, `./validar-reproducao.ps1`, `./run-desktop.ps1 -Publish` e auditoria SDD |

## Auditoria de aderência SDD

| Requisito / Critério | Status | Evidência |
|---|---|---|
| RF04.01 | Atendido (Automatizado) | `LeitorSrt`, `DefinirLegendas`, guia **Legendas e efeitos** em `DialogoImportacao`, `TestesLegendasSrt` e `TestesLegendasDesktop` |
| RF04.02 | Atendido (Automatizado) | Funcionamento integralmente offline sem chave de API ou serviço de IA (`LeitorSrt`, `TestesLegendasSrt`, `TestesLegendasDesktop`) |
| RF04.06 | Atendido (Automatizado) | Confirmação de substituição ou cancelamento quando o projeto já possui legendas (`CriarDialogoSubstituicaoLegendas`, `TestesLegendasDesktop`) |
| RF04.07 | Atendido (Automatizado) | `MenuEditarLegenda` em `MenuTimeline` (`Shift+F10` / tecla Aplicações) habilitado quando pausado sobre legenda ativa (`TestesLegendasDesktop`) |
| RF04.08 | Atendido (Automatizado) | Diálogo único `CriarDialogoLegenda` com texto, fonte, tamanho, cor, negrito, itálico, fundo e posicionamento (`TestesLegendasDesktop`) |
| RF04.09 | Atendido (Automatizado) | Escopo `EscopoEstiloLegenda.ApenasSelecionada` vs. `TodasDoProjeto` em `EditarLegenda` e `CriarDialogoLegenda` (`TestesLegendasSrt`, `TestesLegendasDesktop`) |
| RF04.10 | Atendido (Automatizado) | Preservação do estado de pausa, quadro da composição, posição temporal (`Cursor`) e retorno de foco à timeline após confirmar ou cancelar (`TestesLegendasDesktop`) |
| RF04.11 | Atendido (Automatizado) | Enum `PosicaoVisual` com as 9 posições predefinidas e `InferiorCentro` como padrão (`TestesLegendasSrt`, `TestesLegendasDesktop`) |
| GP08 | Atendido (Automatizado) | Persistência de legendas e estilos no arquivo `.sonarframe` preservando compatibilidade com projetos legados (`TestesCompatibilidade`, `TestesLegendasSrt`) |
| CA-RF04-01 | Atendido (Automatizado) | `TestesLegendasSrt` e `TestesLegendasDesktop` |
| CA-RF04-04 | Parcial / Com Ressalva | Coberto em testes STA (`TestesLegendasDesktop`) e de mídia real (`TestesLegendasSrt`); validação manual com NVDA real pendente de aceite externo |
| CA-RF04-05 (legendas) | Parcial / Com Ressalva | As 9 posições predefinidas estão disponíveis e testadas para legendas (`InferiorCentro` padrão); posicionamento de camadas visuais permanece fora desta entrega |
| CA-RNF01-05 (legendas) | Parcial / Com Ressalva | Restauração de foco verificada em `TestesLegendasDesktop`; validação manual com NVDA real pendente de aceite externo |
