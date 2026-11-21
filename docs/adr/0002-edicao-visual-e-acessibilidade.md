# ADR 0002 — Edição acessível com visão, mouse, teclado e leitor de telas

- **Status:** Aceito como decisão de produto; implementação incremental pendente.
- **Data:** 2026-11-16.
- **Origem:** decisão do responsável pelo produto nesta sessão.
- **Referências:** ERS, seção 1.1 e RNF01.22–25; plano da timeline visual.

## Contexto

O sonarFrame nasceu voltado primariamente a pessoas cegas que utilizam teclado e NVDA. A interface atual permite uso visual por botões, listas e menus, mas a navegação e a edição temporal dependem principalmente de comandos de teclado e valores textuais. Quem enxerga também deve conseguir compreender a composição e realizar as tarefas de edição com uma interação visual adequada.

A ampliação do público inclui pessoas com visão, com baixa visão e pessoas que alternam entre visão, teclado, mouse e leitor de telas. O uso de tecnologia assistiva não deve ser pressuposto nem exigido.

## Decisão

O sonarFrame será um único editor acessível por modalidades complementares: interface visual operável com mouse e teclado, navegação integral por teclado e suporte ao NVDA. A experiência visual passa a ser um objetivo explícito de produto, mantendo integralmente os compromissos existentes com pessoas cegas.

1. **Um projeto e um estado de edição.** As apresentações gráfica e estruturada compartilham cursor, seleção temporal, item selecionado, composição e histórico. Não haverá projeto, motor de edição ou formato de persistência separado por modalidade de uso.
2. **Equivalência das operações.** Gestos com o mouse reutilizam comandos existentes. Arrastar não será obrigatório para executar uma operação; a alternativa por teclado deve permanecer identificável e funcional. Uma mesma operação deve produzir o mesmo resultado e poder ser desfeita e refeita independentemente da entrada usada.
3. **Informação perceptível.** Posição, seleção, foco, estados e erros devem ser compreensíveis visualmente e expostos pelos mecanismos acessíveis adequados. Cor, fala ou gesto não serão o único meio de compreender ou executar uma tarefa. Preservar tema e alto contraste do Windows, legibilidade e foco visível.
4. **Complementaridade.** A timeline gráfica complementa a lista estruturada e seus menus. As cinco regiões e os atalhos atuais permanecem. Um componente gráfico não deve criar uma sequência duplicada e extensa de focos; suas operações e informações devem ter equivalentes acessíveis sincronizados.
5. **Cancelamento e precisão.** Escape cancela gestos ainda não confirmados. Clique, arraste, seleção e foco são estados distintos. Navegação por quadro e tempos racionais existentes permanecem disponíveis; a precisão visual limitada por pixels não redefine a precisão do domínio.
6. **Validação das duas experiências.** Verificar tarefas com mouse e visão, somente teclado e NVDA. Testes automatizados não substituem o aceite humano de usabilidade visual ou de leitura falada.

## Primeira entrega aprovada

Na `feature/timeline-visual`, criada da `develop`:

- Timeline gráfica proporcional à duração, com régua, cursor, clipes e camadas identificáveis.
- Clique para navegar no tempo.
- Seleção de intervalo por arraste na régua.
- Arraste para reordenar clipes da sequência principal, com desfazer/refazer.
- Zoom e rolagem quando necessários para acessar o conteúdo.
- Preservação da lista, menus e navegação existentes por teclado/NVDA.

Miniaturas e ondas de áudio foram explicitamente adiadas para uma próxima entrega. Alças de corte e manipulação de imagens diretamente sobre a prévia não fazem parte desta primeira entrega; seu detalhamento depende de planejamento posterior.

## Integração com features em andamento

Concentrar a apresentação gráfica em arquivos próprios do Desktop e usar pontos pequenos de integração com a janela. Não reorganizar os arquivos de edição, menus ou XAML compartilhados apenas para introduzir essa apresentação. Não alterar os contratos de domínio, JSON/SQLite ou renderização para representar coordenadas de tela.

Fades, legendas e propriedades visuais continuam pertencendo às suas features. A timeline usa identificadores e informações temporais estáveis, preserva os objetos completos ao emitir comandos e não recria clipes com perda de propriedades desconhecidas. Elementos futuros podem ser acrescentados à projeção visual por adaptadores, sem exigir dependência antecipada das branches correspondentes. O instalador permanece independente.

Essa estratégia reduz a superfície de conflitos, mas não garante merges sem conflitos. A integração deverá ser verificada contra as branches reais, registrando os pontos que exigirem resolução.

## Critérios de aceite da decisão

- A mesma reordenação feita por mouse ou teclado produz ordem e histórico equivalentes.
- Navegar ou selecionar em uma apresentação atualiza o estado consultado pela outra, sem iniciar reprodução implicitamente.
- Cancelar um arraste não altera o projeto nem cria entrada de desfazer.
- A apresentação visual permite distinguir cursor, intervalo, item selecionado e foco sem depender apenas de cor.
- O usuário consegue alcançar controles e conteúdo com zoom, rolagem e escalonamento de tela; o foco permanece visível.
- O percurso existente por teclado e NVDA continua disponível, com anúncios proporcionais.
- A decisão não é registrada como funcionalidade entregue antes de implementação, testes e aceite correspondentes.
