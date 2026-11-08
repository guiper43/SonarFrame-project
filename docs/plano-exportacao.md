# Plano da feature: exportação MP4

Data: 07/11/2026. Revisão de correções: 08/11/2026. Base: `bcc6fa9`, branch `feature/exportacao`.

## Objetivo e fronteira de escopo

Gerar um arquivo MP4 utilizável fora do editor a partir da composição atual, preservando cortes, ordem, fotos, volumes de 0 a 200% e camadas de áudio, vídeo e imagem, com operação integral por teclado e anúncios proporcionais para NVDA.

Fora desta entrega: legendas, fades, transições, propriedades de posição/tamanho de camadas, exportação 4K, escolha manual de codec, taxa de quadros configurável e fila de exportações.

## Requisitos atendidos

| Requisito | Tratamento |
|---|---|
| RF06.01 | Contêiner MP4 (H.264 + AAC, `+faststart`) |
| RF06.02 | 720p e 1080p; 1080p marcado ao abrir |
| RF06.03 | 16:9 e 9:16 sem pré-seleção; exportar sem orientação é recusado com erro acessível |
| RF06.04 | Permitir bordas (escala inteira centralizada com faixas) ou Preencher a tela (recorte proporcional centralizado) na sequência principal |
| RF06.05 | Parâmetros de codificação automáticos |
| RF06.06 | Nome do arquivo e destino de envio (**Pasta Downloads (padrão)** ou **Outra pasta selecionada**), com persistência da escolha do usuário em `settings.json`, vedação de mistura com pastas de projeto e confirmação para substituir |
| RF06.07 | Porcentagem, estado e tempo restante estimado consultáveis; anúncios a cada 10% |
| RF06.08 | Conclusão com ações Abrir vídeo e Abrir pasta |
| RF06.09 | Interface responsiva; edição, prévia e troca de projeto bloqueadas durante a exportação; mensagens recentes e área de comandos disponíveis |
| CA-RF06-01 a 03 | Testes WPF do diálogo e do fluxo com exportador falso; testes com FFmpeg real |
| CA-RNF01-01 / 07 | Fluxo por teclado; falhas registradas em mensagens recentes |

## Decisões por camada

- **Core:** sem alteração de modelo nem de `JsonPropertyName`.
- **Application:** `OpcoesExportacao` ganha `SubstituirExistente`, exige extensão `.mp4` e calcula `Dimensoes()` (1280×720, 1920×1080, 720×1280, 1080×1920). `IExportador` e `ProgressoOperacao` são mantidos.
- **Infrastructure:** `ExportadorFfmpeg` (`IExportador`, `IAsyncDisposable`) usa um motor `PreparadorComposicaoFfmpeg` próprio, renderizando a partir dos originais com as mesmas regras de composição da prévia, em uma passagem audiovisual (cortes por timestamps, mixagem sem normalização, camadas centralizadas com transparência). O perfil de exportação muda somente resolução, enquadramento da sequência principal e preset/CRF; a prévia continua com os mesmos argumentos. Não são gerados blocos intermediários de vídeo ou PCM: o FFmpeg grava H.264/AAC diretamente em um MP4 temporário na pasta de destino, movido somente após verificar duração e dimensões. O cache da instância contém apenas o script de filtros e o marcador de uso, sem crescer com a duração da mídia. O arquivo MP4 de saída pode ultrapassar 2 GB conforme o espaço disponível no destino; ele não é cache reutilizável. O progresso deriva dos timestamps processados pelo FFmpeg. Cancelamento ou falha encerram o FFmpeg e removem os parciais, sem tocar em mídias originais. O destino não pode coincidir com uma mídia do projeto.
- **Desktop:** `IExportador` registrado como `Scoped`. Botão “Exportar vídeo…” na região 5, item no menu Projeto e Ctrl+E. Diálogo por `CriarDialogo`/`AdicionarFechar` com grupos de opções navegáveis por setas, incluindo escolha entre **Pasta Downloads (padrão)** e **Outra pasta selecionada**, persistência de `ExportsDirectory` em `ConfiguracoesDesktop` e recusa de pastas que contenham ou sejam pastas de projeto. Durante a exportação, a região 5 exibe progresso, Consultar progresso e Cancelar exportação (Escape também cancela). O salvamento automático a cada dois minutos e o botão Salvar/Ctrl+S permanecem disponíveis, sem deslocar foco ou modificar o snapshot exportado.

## Incrementos

| Incremento | Entrega | Conclusão |
|---|---|---|
| 1 | Contrato e validações | Concluído (`TestesExportacao`) |
| 2 | Motor FFmpeg | Concluído (`TestesExportacao` com mídias reais) |
| 3 | DI e interface | Concluído (`TestesInjecaoDependencias`, `TestesExportacaoDesktop`) |
| 4 | Validação das correções | Concluído em 08/11/2026: build, suíte geral, prévia nativa, matriz de formatos, publicação em `artifacts/desktop` e revisão manual com leitor de telas (NVDA) |

## Auditoria de aderência (SDD)

| Critério / Requisito | Classificação | Evidência |
|---|---|---|
| RF06.01 a RF06.06 | Atendido (Automatizado) | `TestesExportacao` (`SonarFrame.Tests`) e `TestesExportacaoDesktop` (`SonarFrame.Desktop.Tests`) |
| RF06.07 a RF06.09 | Atendido (Automatizado) | `TestesExportacaoDesktop` (progresso, bloqueio de regiões durante exportação, ações Abrir vídeo/pasta, cancelamento) |
| CA-RF06-01 | Atendido (Automatizado) | `DialogoApresentaMp41080pSemOrientacaoPreSelecionadaENavegaPorTeclado` verifica padrões e recusa sem orientação |
| CA-RF06-02 | Atendido (Automatizado + NVDA) | `DialogoApresentaMp41080pSemOrientacaoPreSelecionadaENavegaPorTeclado` exercita setas nas listas; `ExportacaoGeraMp4ComOrientacaoEnquadramentoCortesCamadasEProgresso` exporta horizontal/vertical em 720p/1080p; revisado manualmente com leitor de telas em 08/11/2026 |
| CA-RF06-03 | Parcial / Com Ressalva | `ExportacaoAtualizaProgressoBloqueiaEdicaoEOfereceAbrirVideoEPasta` verifica progresso e ações finais e o fluxo foi revisado com leitor de telas; abertura no player real em máquina limpa permanece externa |
| Cancelamento e proteção de arquivos | Atendido (Automatizado) | `ExportacaoProtegeOriginaisExigeSubstituicaoELimpaParciaisNoCancelamento`; `CancelamentoEFalhaRegistramMensagensRecentesERestauramInterface` |
| GP12 e preservação do foco | Atendido (Automatizado) | `ExportacaoPermiteSalvamentoAutomaticoEManualSemMoverFoco` verifica o limiar de dois minutos, arquivo principal/recuperação e salvamento manual com exportação pendente |
| Uso de disco, cortes, volumes e camadas | Atendido (Automatizado) | `ExportacaoDiretaPreservaCortesVolumesECamadasSemAcumularBlocos` verifica ausência de arquivos intermediários de mídia, cores/ordem dos trechos, intervalo da camada, silêncio e proporção dos volumes no MP4 |
| CA-RNF01-01 | Atendido no escopo da feature | Testes WPF cobrem navegação por teclado e o fluxo de exportação foi revisado manualmente com leitor de telas em 08/11/2026 |
| CA-RNF01-07 | Parcial / Com Ressalva | `CancelamentoEFalhaRegistramMensagensRecentesERestauramInterface` verifica erro de exportação em mensagens recentes; não cobre todos os erros de importação |
| Player externo e máquina limpa | Pendente de Aceite Externo | Revisão com leitor de telas concluída em 08/11/2026; resta validar em máquina limpa |

## Limites conhecidos

- Saída a 30 fps, como a prévia; fontes de 60 fps são reduzidas. A exportação usa o grafo da composição inteira; custo de memória/processamento cresce com a quantidade de fontes simultâneas, sem cache de mídia proporcional à duração.
- Camadas visuais seguem a convenção atual (centralizadas, sem corte) em qualquer enquadramento.
- A revisão do fluxo de exportação com leitor de telas foi concluída em 08/11/2026; a validação em máquina limpa permanece externa.

## Validação das correções — 08/11/2026

- `./build.ps1`: build Release com zero avisos/erros; 112 testes de domínio/aplicação/infraestrutura e 46 de Desktop aprovados. Oito testes condicionais foram ignorados nessa execução; sete nativos foram executados separadamente, e o benchmark opcional não foi executado.
- `./testar-previa-desktop.ps1`: sete testes nativos aprovados.
- `./validar-reproducao.ps1`: dez formatos aprovados.
- `./run-desktop.ps1 -Publish`: publicação self-contained Windows x64 concluída em `artifacts/desktop`, substituindo a publicação anterior.
- As execuções utilizaram `TEMP`/`TMP` em `.tools/revisao-exportacao`, evitando o caminho especial do sandbox. Logs locais: `correcoes-build.log`, `correcoes-previa.log`, `correcoes-matriz.log` e `correcoes-publicacao.log` nessa pasta.
- Revisão final pelos critérios de `code-reviewer`: os achados de bloqueio do salvamento, acúmulo de blocos, conflito entre Downloads e pastas de projetos, filtragem de telemetria `-progress` do FFmpeg, validação centralizada de nomes de arquivo (`OpcoesExportacao.NomeArquivoValido`), foco nas listas do diálogo e restauração de foco após diálogos foram corrigidos e cobertos por testes automatizados. Veredito da revisão de código: **Pode aprovar**; isso não substitui o aceite externo registrado acima.
## Compatibilidade das pastas — 08/11/2026

Downloads é obtida pela pasta conhecida do Windows (`SHGetKnownFolderPath`, `FOLDERID_Downloads`), incluindo redirecionamentos, sem criar diretórios durante a consulta. A integração é restrita ao Windows e não introduz dependência de WPF nos pacotes.

Ao carregar configurações antigas sem `ExportsDirectory`, `ProjectsDirectory` é preservado. Se os projetos estiverem em Downloads, o destino sugerido passa a ser a subpasta `sonarFrame-exportacoes`; a interface só a cria após confirmação da exportação. A leitura não regrava o JSON, move mídias ou altera a localização dos projetos.

Cobertura: `DownloadsRespeitaLocalizacaoRedirecionadaSemCriarPasta` simula uma localização redirecionada, sem alterar as pastas do usuário; `ConfiguracaoAntigaPreservaProjetosEResolveConflitoComDownloads` verifica JSON legado com e sem conflito, preservação do arquivo na leitura e persistência posterior das duas propriedades.
Validação consolidada: build Release sem avisos/erros, 112 testes de núcleo/infraestrutura, 46 de Desktop, 7 testes nativos de prévia e 10 formatos de reprodução aprovados, revisão final sem novos achados e publicação concluída em `artifacts/desktop`.
