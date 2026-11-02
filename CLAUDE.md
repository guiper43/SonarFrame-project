# sonarFrame no Claude Code

@AGENTS.md

## Integração específica

- As regras do projeto estão no AGENTS.md importado acima. Confirmar a branch atual antes de afirmar quais recursos já estão implementados ou publicados.
- Usar as entradas em `.claude/skills` para descobrir as skills do projeto. Elas encaminham para `.agents/skills`, onde o conteúdo é mantido. Ao ativar uma entrada, ler o arquivo canônico indicado antes de executar a tarefa.
- O agente `.claude/agents/code-reviewer.md` é um adaptador para a mesma skill de revisão. Quando a revisão for delegada, fornecer o diff, a branch base e os resultados de validação. Não exigir um modelo fixo para aplicar os critérios comuns.
- Configurações de plugins do Claude, quando presentes, pertencem somente ao Claude; as skills locais de revisão, acessibilidade, planejamento e novidades funcionam sem exigir esses plugins dos colegas.
- Usar ferramentas nativas disponíveis para leitura, edição, terminal e Git, respeitando as permissões da sessão. Não tratar comandos ou ferramentas exclusivos do Codex ou Antigravity como dependências.
