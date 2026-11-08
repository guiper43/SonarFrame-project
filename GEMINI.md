# sonarFrame no Antigravity

## Integração específica

- Aplicar AGENTS.md na raiz como base comum do projeto. Se o ambiente não o tiver carregado, ler esse arquivo antes de planejar ou editar.
- Seguir as regras de arquitetura, injeção de dependências (`src/SonarFrame.Desktop/Configuracao/RegistroDependencias.cs`) e ADRs (`docs/adr/`) definidas em AGENTS.md, mantendo paridade arquitetural com o Codex e o Claude Code.
- As skills compartilhadas estão em `.agents/skills`: `code-reviewer`, `desktop-accessible-ui`, `sdd` e `novidades`. Carregar o SKILL.md correspondente à tarefa; não criar cópias em `.agent/skills` ou regras paralelas com o mesmo conteúdo.
- Planos e relatórios internos da ferramenta ajudam a acompanhar a tarefa, mas não substituem os planos versionados em `docs/plano-*.md` quando a skill sdd se aplica.
- Conferir branch, diff e validações no terminal do checkout em uso. Uma tarefa marcada como concluída na interface não comprova build, testes, publicação ou homologação NVDA.
- Aplicar a skill code-reviewer com as ferramentas disponíveis. Não exigir o agente do Claude, modelos específicos ou plugins do Codex para revisar.
- Configurações globais e permissões do Antigravity pertencem ao ambiente do usuário; não alterar essas configurações para sincronizar as regras deste repositório.

## Revisão obrigatória antes de concluir (`code-reviewer`)

Antes de declarar qualquer implementação, correção ou alteração de código como concluída ao usuário, executar obrigatoriamente um ciclo completo de revisão adversarial seguindo `.agents/skills/code-reviewer/SKILL.md`:

1. **Ler a skill e o diff real**: carregar `.agents/skills/code-reviewer/SKILL.md` e inspecionar o `git diff` completo (incluindo arquivos novos), preferencialmente delegando uma passagem independente de auditoria a um subagente (`research` ou `self`) com o papel exclusivo de revisor crítico.
2. **Auditar efeitos colaterais e invariantes do sistema**:
   - **Interação com recursos existentes**: verificar se novos estados (`Ocupado`, flags de bloqueio, modais ou tarefas em segundo plano) bloqueiam indevidamente o salvamento automático (`GP12`), o salvamento manual (`Ctrl+S`), a preservação de foco ou o descarte de recursos.
   - **Orçamento de disco, memória e processos**: verificar se operações longas (prévia, composição, exportação) acumulam arquivos intermediários desnecessários no cache, impõem limites artificiais indevidos ao arquivo final ou deixam processos `ffmpeg`/`ffprobe` órfãos em cancelamentos e falhas.
   - **Isolamento de dados do usuário e nos testes**: garantir que fluxos da aplicação não misturem pastas de projeto (`.sonarframe`) com saídas exportadas e que testes automatizados nunca leiam ou sobrescrevam `%LOCALAPPDATA%/sonarFrame/settings.json` ou pastas reais do usuário.
   - **Acessibilidade e teclado (`desktop-accessible-ui`)**: validar foco explícito ao abrir/fechar diálogos e concluir ações, regiões `LiveSetting` (`Polite`/`Assertive`), navegação por setas/Tab/Escape e ausência de comentários no código.
   - **Rigor de rastreabilidade (`sdd`)**: não classificar como `Atendido (Automatizado)` critérios que exijam validação manual em NVDA real, player externo ou máquina limpa (`Parcial / Com Ressalva` ou `Pendente de Aceite Externo`).
3. **Corrigir antes de entregar**: se a revisão apontar qualquer achado Crítico, Alto ou Médio, corrigir o código e adicionar cobertura de teste imediatamente, reexecutando `./build.ps1` (e `./run-desktop.ps1 -Publish` quando aplicável) antes de responder ao usuário. Nunca entregar código aguardando que outra ferramenta faça o pente-fino.
