# Auditoria da edição acessível da timeline

Registro da primeira revisão. A interação com controle temporal separado foi substituída posteriormente pelo [plano da timeline unificada](plano-timeline-unificada.md); consulte `desktop.md` para os atalhos atuais.

Data: 2 de novembro de 2026. Branch: `feat/reproducao-e-previa`.

Revisão incremental da edição existente, conforme `sonarFrame_ERS_v05.md`, `plano-reproducao.md` e as decisões de interação de `AGENTS.md`. A correção de retomada do áudio já validada pelo usuário foi preservada. Não houve migração de arquitetura ou integração da branch de injeção de dependências.

## Problemas e correções

| Impacto | Evidência e causa | Correção |
|---|---|---|
| Médio | `TeclaAreaEditor` encaminhava Home/End, mas não Page Up/Down da lista para o navegador temporal. | Encaminhamento dos dois atalhos, inclusive com Shift. |
| Médio | `Atualizar` substituía os objetos da lista sem restaurar seu contêiner focado. | Restauração do foco no item selecionado quando a lista é reconstruída e já possuía o foco; foco na lista quando não existe mais item selecionado. |
| Médio | Os rótulos não continham tipo de mídia nem ordem explícita. | Nome, tipo, número do item, início, fim, duração e sequência/camada disponíveis pelo peer UI Automation nativo. |
| Médio | `LerPosicao` retornava apenas o cursor mesmo havendo seleção. | Enter no controle temporal consulta cursor, limites e duração do intervalo com milissegundos. Navegar com Shift continua anunciando apenas a extremidade; seleção vazia é identificada corretamente. |
| Médio | Corte, exclusão e histórico tinham confirmações sem detalhes suficientes; Ctrl+Z/Y indisponíveis ficavam silenciosos. | Corte identifica o nome e as partes; exclusão informa limites, cursor e nova duração; histórico informa estado restaurado e indisponibilidade. Exclusão inválida não desloca o foco. |
| Médio | `MoverClipe` criava outro array mesmo quando origem e destino coincidiam, consumindo uma etapa de desfazer e apagando o refazer. | Retorno do projeto original; interface informa que o item já está nessa posição. |
| Baixo | Testes de eventos sintéticos liam Shift/Ctrl do teclado físico. Falha reproduzida com diagnóstico `modificadores Shift` em um teste de seta simples. | Handlers consultam o dispositivo do evento; `TecladoTeste` fornece estados determinísticos. Os atalhos são exercitados pela rota WPF. |

## Rastreabilidade e cobertura

| Requisitos e critérios | Evidência automatizada | Classificação |
|---|---|---|
| RF02: seleção, divisão e exclusão com fechamento; CA-RF02-01/02 | `TestesEdicao`, `TestesAcoesEdicao`; novo percurso `EditarPorAtalhoEBotaoPreservaFocoIntervaloEReproduzEstadoRestaurado` verifica origens, durações, cursor, seleção e biblioteca. | Automatizado |
| RF02: inserção intermediária, reordenação e histórico; CA-RF02-03/04 | `InsercaoIntermediariaEReordenacaoAtualizamListaHistoricoEPrevia`; `MoverParaMesmaPosicaoPreservaHistoricoERefazer`; teste existente de camadas intercaladas na lista. | Automatizado |
| RF05: navegação e seleção; CA-RF05-01/02/03 | `PageUpEDownNaListaMovemTempoSemTrocarClipe`; `SelecaoAteExtremosPodeSerConsultadaSemCriarEdicaoOuIntervaloVazio`; testes existentes de Enter, extremos, quadros racionais, CFR/VFR e anúncios. | Automatizado |
| RF05: reprodução após edição; CA-RF05-04 | Snapshot fornecido ao preparador após divisão, exclusão, inserção e reordenação; `CorteExclusaoEDesfazerAtualizamReproducaoNativa` verifica estado e duração no LibVLC após corte, exclusão, desfazer e refazer. Testes de preparação existentes verificam amostras de áudio, vídeo, junções e camadas com FFmpeg. | Automatizado; percepção audiovisual humana pendente |
| RNF01: contexto dos atalhos, seleção independente do cursor, nomes e foco | Eventos WPF de Ctrl+B/Z/Y, Delete, Page Up/Down, Shift+Home/End; consulta de `AutomationPeer.GetName`; foco no contêiner após divisão; Ctrl+B em campo de texto não edita o projeto. | Automatizado |
| RNF01: fala real, proporcionalidade e ausência de duplicação no NVDA | Propriedades UIA, conteúdo das mensagens e percurso automatizado não comprovam o resultado falado. Nenhum processo NVDA encontrado nesta sessão. | Pendente de aceite externo |

## Arquivos desta revisão

- Domínio: `src/SonarFrame.Core/ComandosEdicao.cs`.
- Interface: `src/SonarFrame.Desktop/JanelaPrincipal.xaml`, `JanelaPrincipal.xaml.cs`, `JanelaPrincipal.Edicao.cs`, `JanelaPrincipal.Navegacao.cs` e `JanelaPrincipal.Quadros.cs`.
- Origem dos modificadores nos eventos: também `DialogoImportacao.cs` e `MenuComAberturaExplicita.cs`.
- Testes: `tests/SonarFrame.Tests/TestesEdicao.cs`; em `tests/SonarFrame.Desktop.Tests`, `TestesNavegacaoTimeline.cs`, `TestesReproducaoComposicao.cs`, `TestesPreviaNativa.cs`, `TecladoTeste.cs`, `TestesMenu.cs`, `TestesPreviaBiblioteca.cs` e `TestesDialogoImportacao.cs`.
- Documentação: este relatório, `desktop.md` e `CHANGELOG.md`.

As alterações anteriores em governança, configurações de agentes, workflows e `FabricaPreviaVlc.cs` não fazem parte desta revisão. Foram preservadas.

## Validação

- `./build.ps1`: compilação com zero avisos e zero erros; 80 testes de domínio/aplicação/infraestrutura e 28 testes WPF aprovados. Os cinco testes nativos são ignorados nesta execução e rodados pelo script específico.
- `./testar-previa-desktop.ps1`: cinco testes nativos aprovados, nenhum ignorado, incluindo edição/exclusão/histórico e preservação da correção do áudio.
- `./validar-reproducao.ps1`: dez formatos aprovados (JPG, JPEG, PNG, MP4, MOV, MP3, WAV, M4A, OGG/Vorbis e OGG/Opus). Relatório em `.tools/test-artifacts/validacao-reproducao/resultado.json`.
- `git diff --check`: sem erros de whitespace.
- `./run-desktop.ps1 -Publish`: concluído em `artifacts/desktop`, sobrescrevendo a publicação anterior.

Na primeira execução, uma comparação de arrays imutáveis no teste novo foi ajustada para comparar seus elementos. As falhas intermitentes de teclado foram diagnosticadas e corrigidas com o dispositivo de teste, antes da execução completa aprovada. Uma tentativa direta de `dotnet test` sem o ambiente do script não conseguiu consultar NuGet; a restauração pelos scripts configurados foi concluída normalmente.

## Revisão final de código

Revisão pelo critério da skill `code-reviewer`: comandos continuam puros, tempos racionais e mídias originais preservados; persistência e contratos JSON não foram alterados; mudanças da interface reaproveitam controles nativos, tema e região de anúncios existentes. Os testes aguardam as tarefas de edição e verificam o resultado, além do acionamento do comando. Não foram encontrados outros defeitos confirmados no diff desta revisão.

Veredito de código: **Pode aprovar**. Risco não confirmado: comportamento falado e eventual duplicação de anúncios de foco/região viva em versões/configurações específicas do NVDA. O aceite externo de acessibilidade depende do roteiro abaixo.

## Roteiro de homologação com NVDA

1. Abrir a publicação em `artifacts/desktop`, importar dois vídeos e inserir na sequência. Usar somente teclado. Confirmar anúncio de nome, tipo, ordem e duração ao navegar com Cima/Baixo.
2. Selecionar o segundo clipe sem deslocar o cursor. Enter deve ir ao início desse clipe sem mudar foco ou seleção. Page Up/Down deve mover dez segundos; Home/End, aos extremos.
3. Posicionar dentro do primeiro clipe e usar Ctrl+B. Confirmar o ponto exato anunciado, duas partes na lista e foco preservado mesmo se o segundo clipe estiver selecionado. Desfazer e repetir pelo botão Dividir.
4. Selecionar um intervalo atravessando uma junção com Shift e setas. Alt+3 e Enter devem informar início, fim e duração. Shift+Home/End no próprio extremo não deve inventar intervalo positivo.
5. Delete no controle temporal deve excluir o intervalo e fechar a lacuna. Conferir a nova duração, cursor na junção e continuidade de áudio/vídeo. Comparar com o botão Excluir intervalo. Delete na lista deve remover somente o elemento selecionado.
6. Inserir antes do clipe selecionado e mover antes/depois ou início/fim pelo menu Shift+F10. Conferir ordem e anúncio. Repetir um movimento no extremo e confirmar que Ctrl+Z desfaz a última edição efetiva.
7. Usar Ctrl+Z/Y e os botões para restaurar cortes, exclusões e ordem. Conferir seleção temporal, cursor, foco e reprodução do estado restaurado, inclusive com camadas.
8. Tentar dividir na borda e desfazer sem histórico. Confirmar feedback claro. Nos campos de texto, Ctrl+B/Z/Y deve respeitar o contexto do campo.
9. Durante reprodução contínua, confirmar ausência de fala de tempo repetitiva. Observar se anúncios de foco e região viva se repetem indevidamente. Registrar versão do NVDA e do Windows, sequência de teclas e fala observada.

Os testes automatizados não equivalem à homologação humana com NVDA. A aprovação externa de acessibilidade permanece aberta até esse percurso ser realizado.
