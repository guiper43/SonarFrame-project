# Plano da feature: fades independentes de áudio e vídeo

Data: 15/11/2026. Base: `cbd8e69` (`feature/exportacao`), branch `feature/fades`.

## Objetivo e fronteira de escopo

Permitir configurar, ouvir/visualizar na prévia, exportar, alterar e remover efeitos de entrada e saída graduais (**fade-in** e **fade-out**) com controles independentes para áudio e vídeo em clipes da sequência principal e camadas da composição, operando integralmente por teclado e com anúncios proporcionais para NVDA.

Fora desta entrega: cinco transições visuais entre clipes adjacentes (`RF04.18–19`, `RF04.21–22`), propriedades de posicionamento/tamanho de camadas (`RF04.11–17`) e legendas SRT/IA (`RF04.A`, `RF04.D`).

## Mapeamento de requisitos da ERS

| Requisito / Critério | Tratamento |
|---|---|
| RF03.07 | Entrada e saída graduais (*fade-in* e *fade-out*) aplicáveis às fontes de áudio (vídeos e áudios na sequência ou camadas) |
| RF04.20 | *Fade-in* e *fade-out* para vídeos, fotografias e áudios com controles independentes para efeitos sonoros e visuais |
| RF04.23 | Pré-visualizar, modificar e remover os efeitos de fade sem usar o mouse, com suporte a desfazer e refazer |
| GP08 | Persistência dos fades aplicados no arquivo `.sonarframe` sem quebrar a compatibilidade de projetos anteriores |
| CA-RF04-07 (parcial: fades) | Efeitos *fade-in* e *fade-out* restritos aos tipos autorizados (áudio apenas em vídeos e áudios; vídeo apenas em vídeos e imagens); transições permanecem fora desta entrega |
| RNF01.01 / RNF01.12 / RNF01.14 | Operação 100% por teclado em Propriedades (`Alt+2`) e no menu contextual da timeline (`Shift+F10`), com restauração de foco e validação acessível (`Assertive`) |

## Decisões arquiteturais por camada

- **Core (`SonarFrame.Core`):**
  - `Clipe` recebe `FadeInAudio`, `FadeOutAudio`, `FadeInVideo` e `FadeOutVideo` do tipo `TempoMidia` (padrão `default`, equivalente a `TempoMidia.Zero`), serializados com `[JsonPropertyName("AudioFadeIn")]`, `[JsonPropertyName("AudioFadeOut")]`, `[JsonPropertyName("VideoFadeIn")]` e `[JsonPropertyName("VideoFadeOut")]` e `JsonIgnoreCondition.WhenWritingDefault` para preservar a compatibilidade exata com projetos legados quando não há fades ativos.
  - `Projeto.Validar()` garante que nenhum fade seja negativo, que `FadeInAudio + FadeOutAudio <= clipe.Duracao` e `FadeInVideo + FadeOutVideo <= clipe.Duracao`, que imagens (`TipoMidia.Imagem`) não tenham fades de áudio e que áudios (`TipoMidia.Audio`) não tenham fades de vídeo.
  - Novo comando `DefinirFades(Guid ClipeId, TempoMidia FadeInAudio, TempoMidia FadeOutAudio, TempoMidia FadeInVideo, TempoMidia FadeOutVideo)` atualiza clipes da sequência principal ou camadas.
  - Ao dividir um clipe (`DividirClipe`) ou recortar extremidades em `ExcluirIntervalo`: a parte inicial preserva o *fade-in* (limitado à nova duração) e zera o *fade-out* na borda cortada; a parte final preserva o *fade-out* (limitado à nova duração) e zera o *fade-in* na borda cortada.
- **Application (`SonarFrame.Application`):**
  - `ItemComposicao` expõe `FadeInAudio`, `FadeOutAudio`, `FadeInVideo` e `FadeOutVideo`.
  - `AcoesEdicao` oferece `DefinirFadesSelecionado` e `RemoverFadesSelecionado`.
- **Infrastructure (`SonarFrame.Infrastructure`):**
  - `IdentidadeComposicao`: `Normalizar` e `Segmentos` não fundem clipes adjacentes quando há *fade-out* no primeiro ou *fade-in* no segundo, e evitam quebrar blocos de 5 segundos no meio de um fade que caiba no limite do bloco. `ChaveConteudo` inclui os fades de áudio na chave sonora e os fades de vídeo na chave visual, preservando o reaproveitamento independente de blocos de áudio e vídeo.
  - `CacheBlocosComposicao`: `RecortarBloco` projeta os intervalos de *fade-in* e *fade-out* do clipe original sobre a janela `[inicio, fim)` do bloco.
  - `PreparadorComposicaoFfmpeg`: aplica `afade=t=in`/`afade=t=out` no grafo de áudio e `fade=t=in`/`fade=t=out` no grafo de vídeo (com `:alpha=1` em camadas visuais transparentes para suavizar a opacidade sobre o vídeo base sem cobri-lo de preto).
  - `QuadrosComposicaoFfmpeg`: aplica a atenuação proporcional de opacidade (`colorchannelmixer=aa=...`) ao extrair um quadro estático em um instante dentro de *fade-in* ou *fade-out* visual.
- **Desktop (`SonarFrame.Desktop`):**
  - Na região 2 (`Propriedades do elemento`, `Alt+2`) e no menu contextual da timeline (`Shift+F10`), disponibilizar **Configurar fades…** e **Remover fades**.
  - O diálogo **Fades do conteúdo** usa `CriarDialogo`/`AdicionarFechar` e exibe apenas os campos compatíveis com o tipo da mídia selecionada (áudio e vídeo para `Video`; apenas áudio para `Audio`; apenas vídeo para `Imagem`), validando limites individuais e a soma `fade-in + fade-out <= duração` com anúncio `Assertive` e retorno de foco ao campo inválido.
  - `DescreverConteudo` informa os fades configurados nas propriedades do elemento selecionado.

## Divisão em incrementos verificáveis

| Incremento | Entrega | Verificação |
|---|---|---|
| 1 | Modelo `Clipe`, invariantes em `Projeto.Validar()`, comando `DefinirFades`, herança em `DividirClipe`/`ExcluirIntervalo`/`AtualizarCamada` e `AcoesEdicao` | Testes unitários em `SonarFrame.Tests` cobrindo validação por tipo de mídia, soma `fadeIn + fadeOut`, divisão/exclusão/encurtamento de camada, desfazer/refazer e persistência SQLite/JSON |
| 2 | Motor FFmpeg (áudio, vídeo principal, camadas com canal alfa, quadros estáticos, continuidade `CorteFade` entre blocos de 5s e exportação MP4) | Testes com FFmpeg real validando atenuação de áudio/vídeo nas pontas, fades cruzando fronteiras de 5s sem reinício, preservação de cache incremental e exportação MP4 |
| 3 | Interface WPF (`RegiaoPropriedades`, `MenuTimeline`, diálogo acessível por tipo de mídia, pausa antes de abrir modal, restauração de foco e anúncios) | Testes STA em `SonarFrame.Desktop.Tests` cobrindo navegação por teclado, campos exibidos por tipo de mídia, dízimas periódicas, recusa acessível quando `fadeIn + fadeOut > duração` e remoção de fades |
| 4 | Validação completa, publicação em `artifacts/desktop` e revisão `code-reviewer` | `./build.ps1`, `./testar-previa-desktop.ps1`, `./validar-reproducao.ps1`, `./run-desktop.ps1 -Publish` e auditoria SDD |

## Auditoria de aderência SDD

| Requisito / Critério | Status | Evidência |
|---|---|---|
| RF03.07 | Atendido (Automatizado) | `Clipe.FadeInAudio`/`FadeOutAudio`, `DefinirFades`, `PreparadorComposicaoFfmpeg` (`afade` com continuidade `CorteFade`), `TestesFades` |
| RF04.20 | Atendido (Automatizado) | Controles independentes de áudio e vídeo em `Clipe`, `CriarDialogoFades` e `PreparadorComposicaoFfmpeg`, `TestesFades` e `TestesFadesDesktop` |
| RF04.23 | Atendido (Automatizado) | Pré-visualização real (blocos/quadros), edição, remoção (`RemoverFadesSelecionado`) e histórico desfazer/refazer por teclado, `TestesFades` e `TestesFadesDesktop` |
| GP08 | Atendido (Automatizado) | Persistência `.sonarframe` com `WhenWritingDefault` sem quebrar projetos legados (`TestesCompatibilidade`, `TestesFades`) |
| CA-RF04-07 (fades) | Parcial / Com Ressalva | Parte de fades (tipos permitidos, independência áudio/vídeo, edição e remoção sem mouse) coberta por `TestesFades` e `TestesFadesDesktop`; transições visuais (`RF04.18–19`, `RF04.21–22`) permanecem fora de escopo desta entrega |
| RNF01.01 / RNF01.12 / RNF01.14 | Parcial / Com Ressalva | Coberto em testes STA (`TestesFadesDesktop`, `AutomationLiveSetting.Assertive`, restauração de foco); homologação com NVDA real pendente de aceite externo |
