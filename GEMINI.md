# sonarFrame no Antigravity

## Integração específica

- Aplicar AGENTS.md na raiz como base comum do projeto. Se o ambiente não o tiver carregado, ler esse arquivo antes de planejar ou editar.
- As skills compartilhadas estão em `.agents/skills`: `code-reviewer`, `desktop-accessible-ui`, `sdd` e `novidades`. Carregar o SKILL.md correspondente à tarefa; não criar cópias em `.agent/skills` ou regras paralelas com o mesmo conteúdo.
- Planos e relatórios internos da ferramenta ajudam a acompanhar a tarefa, mas não substituem os planos versionados em `docs/plano-*.md` quando a skill sdd se aplica.
- Conferir branch, diff e validações no terminal do checkout em uso. Uma tarefa marcada como concluída na interface não comprova build, testes, publicação ou homologação NVDA.
- Aplicar a skill code-reviewer com as ferramentas disponíveis. Não exigir o agente do Claude, modelos específicos ou plugins do Codex para revisar.
- Configurações globais e permissões do Antigravity pertencem ao ambiente do usuário; não alterar essas configurações para sincronizar as regras deste repositório.
