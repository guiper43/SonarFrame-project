# Timeline unificada

Ajuste solicitado em 2 de novembro de 2026, na branch de reprodução atual. Substitui a separação de foco entre navegação temporal e lista descrita na auditoria anterior.

- Alt+3 entra na lista da timeline. Esquerda/Direita, Ctrl e Shift mantêm os passos e a seleção de RF05 nessa mesma lista.
- Delete após seleção temporal exclui o intervalo e fecha a lacuna. Shift e Ctrl apenas navegam/selecionam; a exclusão exige Delete ou uma ação explícita.
- Cima/Baixo ativa o contexto de item, inclusive nos extremos. Delete nesse contexto abre o menu focado em remover o conteúdo da composição, sem executar remoção imediatamente. O intervalo anterior pode ser consultado, mas não é excluído por esse Delete.
- Shift+F10, Aplicações e botão direito abrem o mesmo menu nativo: consulta temporal, divisão, exclusão do intervalo, remoção do item, reordenação e histórico. Escape retorna à timeline.
- A posição e o resumo da seleção continuam visíveis; deixam de constituir um controle separado de busca. Reprodução e cancelamento continuam acessíveis na tela.

Requisitos: RF02.01–09, RF05.01–07, RNF01.04/06–12. Mudança restrita a Desktop, testes e documentação; comandos puros, persistência e preparação de mídia são preservados.

Validação: eventos WPF de Shift+Ctrl+setas/Delete, contexto após Cima/Baixo, menu e cancelamento, histórico, foco e reprodução; build, testes nativos e matriz de formatos; publicação em artifacts/desktop. Fala real do NVDA requer homologação humana.

## Resultado da implementação

- Implementado em `JanelaPrincipal.xaml`, `JanelaPrincipal.Navegacao.cs`, `JanelaPrincipal.xaml.cs`, `JanelaPrincipal.Edicao.cs` e na atualização visual da posição em `JanelaPrincipal.Composicao.cs`.
- `TimelineAlternaEntreIntervaloEItemSemRemoverAoAbrirMenu` cobre Shift+Ctrl+seta, Delete nos dois contextos, foco na remoção, Escape, Aplicações, Shift+F10 e seleção do alvo pelo botão direito. Os testes existentes foram adaptados para acionar as ações pelo evento de menu.
- Build com zero avisos/erros; 80 testes gerais e 29 testes WPF aprovados; cinco testes nativos aprovados; dez formatos aprovados. `git diff --check` sem erros.
- Revisão `code-reviewer`: nenhum achado confirmado pendente no ajuste. Selecionar não exclui; abrir/cancelar o menu não modifica o projeto; remoção e exclusão continuam usando os comandos existentes e preservando originais. Veredito: Pode aprovar.
- Homologação externa pendente: confirmar com NVDA a descoberta do menu, o anúncio da ação focada, o retorno após Escape e a compreensão da mudança de contexto entre setas verticais e horizontais.
- Publicação concluída com `./run-desktop.ps1 -Publish` em `artifacts/desktop`, após o usuário fechar o aplicativo. A versão anterior foi substituída pela timeline unificada validada.
