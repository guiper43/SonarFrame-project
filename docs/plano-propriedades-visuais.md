# Plano da Feature — Posicionamento e Tamanho de Imagens e Camadas Visuais (`RF04.11–17`)

## 1. Objetivo e Fronteira de Escopo

Implementar no `sonarFrame` o suporte completo a posicionamento universal em nove regiões, escolha entre sobreposição e substituição visual na inserção de imagens e camadas visuais, tamanhos pré-definidos e personalizados para elementos sobrepostos, modos de ajuste para substituição visual, aproveitamento da seleção temporal ativa (`RF05`), aviso acessível de sobreposição simultânea na mesma região e edição não destrutiva de propriedades visuais por teclado.

### Entra neste escopo
- **`RF04.11` — Posicionamento universal:** conjunto fechado de nove posições (`SuperiorEsquerda`, `SuperiorCentro`, `SuperiorDireita`, `CentroEsquerda`, `Centro`, `CentroDireita`, `InferiorEsquerda`, `InferiorCentro`, `InferiorDireita`).
- **`RF04.12` — Inserção de imagem:** escolha entre **substituir a imagem do vídeo durante o intervalo** (`ModoExibicaoVisual.Substituir`) e **sobrepor a imagem ao conteúdo visual existente** (`ModoExibicaoVisual.Sobrepor`), preservando o áudio original por padrão e mantendo o controle de volume (`RF03`).
- **`RF04.13` — Tamanho de imagem sobreposta:** opções **Pequeno (limite de 25%)**, **Médio (50%)**, **Grande (75%)** e **Personalizado (5% a 100%, em passos de 5%)**, usando largura e altura do quadro como limites e preservando a proporção original.
- **`RF04.14` — Imagem substitutiva:** opções **Preencher por inteiro** (`AjusteVisual.PreencherInteiro`, com corte proporcional das extremidades quando necessário) e **Permitir bordas** (`AjusteVisual.PermitirBordas`, imagem integral centralizada com faixas pretas).
- **`RF04.15` — Intervalo de exibição:** definição de início e duração/término de exibição, preenchendo automaticamente a partir da seleção temporal ativa (`Projeto.Selecao`, `RF05`) quando existente.
- **`RF04.16` — Sobreposição simultânea:** permissão para múltiplos elementos visuais ocuparem a mesma região no mesmo intervalo temporal, com anúncio acessível ao NVDA informando a ocorrência sem bloquear a operação.
- **`RF04.17` — Edição de propriedades:** modificação por teclado de modo de exibição, posição, tamanho, ajuste e intervalo de elementos visuais já inseridos, preservando os arquivos originais.
- **`CA-RF04-06`, `GP08` e `RNF01`:** persistência no arquivo `.sonarframe` com compatibilidade retroativa integral e operação 100% por teclado.

### Fora de escopo desta entrega
- Criação ou edição de legendas `.srt` e geração por IA (`RF04.01–10`, `RF04.25–33`).
- Transições entre clipes e fades (`RF04.18–24`).

---

## 2. Mapeamento de Requisitos da ERS (`docs/sonarFrame_ERS_v05.md`)

| ID | Requisito / Critério | Camadas Impactadas | Estratégia de Verificação |
|---|---|---|---|
| `RF04.11` | Nove posições universais (3 verticais × 3 horizontais) | `Core`, `Infrastructure`, `Desktop` | Testes unitários de domínio, geração de filtro FFmpeg e seleção por teclado no diálogo |
| `RF04.12` | Escolha entre substituir a imagem no intervalo e sobrepor ao vídeo, preservando o áudio original | `Core`, `Application`, `Infrastructure`, `Desktop` | Testes de inserção de imagem/camada em modo `Sobrepor` e `Substituir` com preservação de áudio |
| `RF04.13` | Tamanhos Pequeno (25%), Médio (50%), Grande (75%) e Personalizado (5% a 100%, passos de 5%) preservando proporção | `Core`, `Infrastructure`, `Desktop` | Testes de invariantes em `Projeto.Validar()`, renderização FFmpeg com mídia real e diálogo WPF |
| `RF04.14` | Imagem substitutiva com `Preencher por inteiro` e `Permitir bordas` | `Core`, `Infrastructure`, `Desktop` | Testes de domínio, renderização FFmpeg (`crop` vs `pad` opaco) e edição no Desktop |
| `RF04.15` | Intervalo de exibição aproveitando a seleção temporal ativa (`RF05`) | `Application`, `Desktop` | Teste de integração WPF verificando preenchimento automático do diálogo com `Projeto.Selecao` |
| `RF04.16` | Sobreposição simultânea permitida na mesma região com anúncio acessível sem bloqueio | `Application`, `Desktop` | Testes de detecção em `AcoesEdicao.ConflitosVisuais` e anúncio na região viva `Situacao` |
| `RF04.17` | Edição por teclado de posição e propriedades visuais de elementos já inseridos sem alterar originais | `Core`, `Application`, `Desktop` | Testes de `AtualizarCamada`, `DefinirLayoutVisual`, `Desfazer`/`Refazer` e diálogo de propriedades |
| `CA-RF04-06` | Imagem sobreposta admite 9 posições e tamanhos pré-definidos/personalizados; substituição admite Preencher por inteiro e Permitir bordas | Todas | Suíte automatizada em `TestesPropriedadesVisuais` e `TestesPropriedadesVisuaisDesktop` |
| `GP08` | Persistência SQLite/JSON das propriedades visuais sem quebrar projetos legados | `Core`, `Infrastructure` | Round-trip em `RepositorioProjetosSqlite` e preservação de `TestesCompatibilidade` |

---

## 3. Decisões Arquiteturais por Camada

### 3.1 `SonarFrame.Core`
- Definir em `Projeto.cs`:
  - `PosicaoVisual`: `SuperiorEsquerda`, `SuperiorCentro`, `SuperiorDireita`, `CentroEsquerda`, `Centro`, `CentroDireita`, `InferiorEsquerda`, `InferiorCentro`, `InferiorDireita`.
  - `ModoExibicaoVisual`: `Sobrepor`, `Substituir`.
  - `TamanhoVisual`: `Pequeno = 25`, `Medio = 50`, `Grande = 75`, `Personalizado = 0`.
  - `AjusteVisual`: `PermitirBordas`, `PreencherInteiro`.
  - `LayoutVisual(ModoExibicaoVisual Modo, PosicaoVisual Posicao, int EscalaPercentual, AjusteVisual Ajuste)` com `[JsonPropertyName]` em inglês (`"Mode"`, `"Position"`, `"ScalePercent"`, `"Fit"`).
- Adicionar a `Clipe` a propriedade opcional `LayoutVisual? Layout = null` com `[JsonPropertyName("VisualLayout"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`, preservando a serialização idêntica de projetos legados quando `Layout` é `null`.
- Validar em `Projeto.Validar()`:
  - `Layout` não pode ser associado a mídias de `TipoMidia.Audio`.
  - `EscalaPercentual` deve estar entre `5` e `100` em múltiplos de `5`.
  - Clipes da sequência principal (`LinhaDoTempo`) exigem `ModoExibicaoVisual.Substituir`.
- Atualizar `InserirClipe`, `InserirClipes`, `AdicionarCamada` e `AtualizarCamada` para aceitar `LayoutVisual? Layout = null` e adicionar o comando `DefinirLayoutVisual(Guid ClipeId, LayoutVisual? Layout, TempoMidia? Duracao = null)`.

### 3.2 `SonarFrame.Application`
- Estender `ItemComposicao` com `LayoutVisual? Layout` e `LayoutVisual LayoutEfetivo`.
- Adicionar em `AcoesEdicao` o método `ConflitosVisuais(TempoMidia inicio, TempoMidia duracao, LayoutVisual layout, Guid? ignorarClipeId = null)` para identificar camadas visuais ativas no mesmo intervalo e na mesma região.

### 3.3 `SonarFrame.Infrastructure`
- Em `IdentidadeComposicao.cs`:
  - Incluir `clipe.Layout` na chave de vídeo (`audio != true`) e na condição de mesclagem de `Normalizar`, mantendo a chave de áudio (`audio == true`) insensível a alterações puramente visuais.
- Em `PreparadorComposicaoFfmpeg.cs` e `QuadrosComposicaoFfmpeg.cs`:
  - Modo `Substituir`: aplicar `Preenchimento(largura, altura)` para `AjusteVisual.PreencherInteiro` ou `Escala(largura, altura, transparente: false)` para `AjusteVisual.PermitirBordas`, cobrindo integralmente o quadro em `x=0:y=0`.
  - Modo `Sobrepor`: dimensionar proporcionalmente dentro da caixa `(largura * EscalaPercentual / 100) × (altura * EscalaPercentual / 100)` com `format=rgba` e posicionar via `overlay=x=...:y=...` nas nove posições.

### 3.4 `SonarFrame.Desktop`
- Atualizar `DescreverConteudo` para exibir modo de exibição, posição, tamanho/escala ou ajuste de enquadramento de elementos visuais.
- Ao acionar **Adicionar como camada…** ou **Adicionar à linha do tempo** para imagem, aproveitar `sessao.Atual.Selecao` como intervalo inicial quando disponível (`RF04.15`) e oferecer escolha acessível entre sobrepor e substituir (`RF04.12`), 9 posições (`RF04.11`), tamanhos 25%/50%/75%/personalizado (`RF04.13`) e ajustes `Permitir bordas`/`Preencher por inteiro` (`RF04.14`).
- Disponibilizar edição das propriedades visuais para elementos já inseridos (`BotaoEditarCamada`, `BotaoPropriedadesVisuais` e `MenuPropriedadesVisuais`, `RF04.17`) e anunciar sobreposições simultâneas na mesma região sem bloquear (`RF04.16`).

---

## 4. Divisão em Incrementos Verificáveis

1. **Incremento 1 — Domínio e Persistência (`Core` + `Application` + `SQLite`):** `LayoutVisual`, invariantes em `Projeto.Validar()`, comandos de edição, detecção de sobreposição e compatibilidade JSON/SQLite.
2. **Incremento 2 — Renderização e Cache (`Infrastructure`):** filtros FFmpeg de sobreposição posicional/escala e substituição (`PermitirBordas` / `PreencherInteiro`) na prévia, busca de quadro e exportação.
3. **Incremento 3 — Interface Acessível (`Desktop`):** diálogos de inserção e edição de propriedades visuais, aproveitamento da seleção temporal, anúncios de sobreposição simultânea e testes de integração em STA.

## 5. Revisão e correções locais

- O diálogo de propriedades visuais limita sua altura à área útil da tela e usa rolagem vertical acompanhando o foco. Teste STA valida uma janela limitada a 400 pixels e o acesso ao botão Aplicar.
- Alterações apenas visuais preservam a segmentação dos blocos de áudio e sua identidade de cache, inclusive após dividir um vídeo em trechos contíguos. A renderização de vídeo mantém os layouts distintos. Teste com FFmpeg verifica zero blocos de áudio renderizados após mudar enquadramento e a invalidação correta após mudar volume.
- Validação após as correções: build sem avisos/erros, 165 testes regulares, 7 testes nativos e matriz de 10 formatos aprovados. Revisão final pelos critérios de `code-reviewer` sem pendências nos dois achados corrigidos. A homologação falada com NVDA continua manual.
