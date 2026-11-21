# Timeline gráfica complementar

Base: develop em 676b81c. Branch: feature/timeline-visual, em worktree isolada.

**Estado:** primeira implementação local em 21/11/2026. A decisão de produto está registrada na [ADR 0002](adr/0002-edicao-visual-e-acessibilidade.md) e nos requisitos RNF01.22–25 da ERS. Aceite humano visual e com NVDA pendente.

## Escopo acordado

Timeline gráfica proporcional ao tempo, régua e cursor, camadas em linhas separadas, zoom, clique para navegar, arraste na régua para selecionar intervalo e arraste de clipes da sequência principal para reordenar. Escape cancela gestos. A lista acessível, seus atalhos e menu permanecem disponíveis na mesma região. Miniaturas, ondas de áudio, alças de corte e manipulação de imagens sobre a prévia ficam para incrementos posteriores.

## Requisitos

- RF02.06–09 / CA-RF02-03–04: reordenação reutilizando MoverClipe e histórico existentes; nenhuma edição das mídias originais.
- RF05.04–07: cursor e seleção temporal compartilhados; mouse como entrada adicional, sem alterar atalhos de precisão por quadro.
- RNF01.01–14: lista e menu acessíveis preservados, foco explícito, cancelamento, estados distinguíveis por texto e contorno, cores do tema Windows.
- GP08: nenhuma alteração em serialização, banco ou formato .sonarframe.

## Arquitetura e integração

Core, Application e Infrastructure permanecem intactos. Um controle Desktop próprio desenha somente projeções de identificadores, nomes, intervalos e faixas. Não conhece fades, legendas ou LayoutVisual. Um arquivo parcial adapta os gestos aos comandos existentes. Três chamadas curtas integram o componente à inicialização, à atualização da janela e à sincronização do cursor durante a reprodução; não reorganizar XAML, menus, edição, exportação ou renderização compartilhados pelas outras features.

A superfície gráfica complementa a lista existente, sem acrescentar região principal ou substituir sua automação. Mouse usa posição proporcional quantizada em unidades de tela; os resultados são TempoMidia racionais, sem alterar a precisão dos comandos temporais existentes. Gestos não gravam alterações até a liberação do mouse. Cancelamento/perda de captura descarta o gesto. Comandos assíncronos rejeitam gestos concorrentes e validam a sessão.

## Incrementos e aceite

1. Projeção de dados, conversão racional entre posição e tempo, hit test e destino de reordenação com testes de limites.
2. Superfície com régua, nomes, seleção, cursor, zoom e cancelamento, preservando tema e lista acessível.
3. Adaptador Desktop com navegação, seleção e uma única entrada no histórico por reordenação; testes STA e preservação do teclado.
4. Build, testes nativos e matriz de formatos, publicação em artifacts/desktop desta worktree e revisão code-reviewer. Verificar merges de teste descartáveis com as outras features, sem alterar suas branches. Aceite falado com NVDA e usabilidade visual permanecem validações humanas.

## Implementação e verificação local — 21/11/2026

- `ModeloTimelineVisual` projeta a sequência e as camadas sem recriar clipes; converte coordenadas de tela para frações racionais limitadas ao intervalo da composição.
- `SuperficieTimelineVisual` desenha régua, clipes, camadas, cursor e seleção, usando recursos do tema Windows. Gestos não alteram a sessão antes da confirmação; Escape, perda de captura, mudança de composição, troca de sessão ou desativação cancelam a interação.
- `JanelaPrincipal.TimelineVisual` oferece zoom de 100% a 1600%, rolagem e adaptação aos comandos existentes. A sincronização durante reprodução reutiliza o caminho de atualização do cursor; não há um segundo relógio de reprodução.
- `TestesTimelineVisual`: sete testes de projeção, limites racionais, destino de reordenação, sincronização com a lista, histórico, equivalência com o menu, cancelamento, hit test e zoom. A renderização WPF da região foi inspecionada, sem substituir avaliação humana.
- Build Release: zero avisos e zero erros. Suíte regular: 165 aprovados; oito testes opcionais ignorados nessa execução. `testar-previa-desktop.ps1`: sete testes nativos aprovados. `validar-reproducao.ps1`: dez formatos aprovados.
- Nesta máquina, a execução usou o SDK local do repositório principal e caminhos temporários menores em `.tools/tmp-timeline` e `.tools/tv`, evitando falhas de ferramentas nativas com caminhos longos e com o diretório temporário do ambiente isolado. Nenhum ajuste no domínio ou na persistência foi necessário.
- Simulações descartáveis de merge do código, por `git merge-tree`, com `feature/fades`, `feature/legendas-srt`, `feature/propriedades-visuais` e `feature/instalador-windows`: sem conflitos textuais. Isso não constitui teste funcional conjunto dessas features nem altera suas branches.
- Revisão pelos critérios de `code-reviewer`: sem achados impeditivos nesta entrega. Sem alteração de JSON/SQLite, contratos de domínio ou arquivos originais de mídia.

| Critério | Evidência / situação |
|---|---|
| CA-RNF01-09 | Reordenação visual equivalente ao menu; uma entrada no histórico e desfazer/refazer automatizados |
| CA-RNF01-10 | Cursor, seleção invertida, item selecionado e projeção sincronizados nos testes Desktop |
| CA-RNF01-11 | Cancelamento por Escape e perda de captura sem edição; mudança de composição invalida o gesto |
| CA-RNF01-12 | Contornos, marcador de cursor, texto, tema Windows e zoom implementados; aceite de usabilidade visual em escalas e telas distintas pendente |
| CA-RNF01-13 | Aceite humano visual e falado com NVDA permanece explicitamente pendente |
