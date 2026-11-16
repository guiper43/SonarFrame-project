# Timeline gráfica complementar

Base: develop em 676b81c. Branch: feature/timeline-visual, em worktree isolada.

**Estado:** planejamento. A decisão de produto está registrada na [ADR 0002](adr/0002-edicao-visual-e-acessibilidade.md) e nos requisitos RNF01.22–25 da ERS; não representa implementação concluída.

## Escopo acordado

Timeline gráfica proporcional ao tempo, régua e cursor, camadas em linhas separadas, zoom, clique para navegar, arraste na régua para selecionar intervalo e arraste de clipes da sequência principal para reordenar. Escape cancela gestos. A lista acessível, seus atalhos e menu permanecem disponíveis na mesma região. Miniaturas, ondas de áudio, alças de corte e manipulação de imagens sobre a prévia ficam para incrementos posteriores.

## Requisitos

- RF02.06–09 / CA-RF02-03–04: reordenação reutilizando MoverClipe e histórico existentes; nenhuma edição das mídias originais.
- RF05.04–07: cursor e seleção temporal compartilhados; mouse como entrada adicional, sem alterar atalhos de precisão por quadro.
- RNF01.01–14: lista e menu acessíveis preservados, foco explícito, cancelamento, estados distinguíveis por texto e contorno, cores do tema Windows.
- GP08: nenhuma alteração em serialização, banco ou formato .sonarframe.

## Arquitetura e integração

Core, Application e Infrastructure permanecem intactos. Um controle Desktop próprio desenha somente projeções de identificadores, nomes, intervalos e faixas. Não conhece fades, legendas ou LayoutVisual. Um arquivo parcial adapta os gestos aos comandos existentes. Somente duas chamadas curtas integram o componente à inicialização e à atualização da janela; não reorganizar XAML, menus, edição, exportação ou renderização compartilhados pelas outras features.

A superfície gráfica complementa a lista existente, sem acrescentar região principal ou substituir sua automação. Mouse usa posição proporcional quantizada em unidades de tela; os resultados são TempoMidia racionais, sem alterar a precisão dos comandos temporais existentes. Gestos não gravam alterações até a liberação do mouse. Cancelamento/perda de captura descarta o gesto. Comandos assíncronos rejeitam gestos concorrentes e validam a sessão.

## Incrementos e aceite

1. Projeção de dados, conversão racional entre posição e tempo, hit test e destino de reordenação com testes de limites.
2. Superfície com régua, nomes, seleção, cursor, zoom e cancelamento, preservando tema e lista acessível.
3. Adaptador Desktop com navegação, seleção e uma única entrada no histórico por reordenação; testes STA e preservação do teclado.
4. Build, testes nativos e matriz de formatos, publicação em artifacts/desktop desta worktree e revisão code-reviewer. Verificar merges de teste descartáveis com as outras features, sem alterar suas branches. Aceite falado com NVDA e usabilidade visual permanecem validações humanas.
