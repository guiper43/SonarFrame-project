# Instruções dos agentes

O sonarFrame usa uma base comum de regras e adapta apenas os pontos de entrada de cada ferramenta. Abrir o checkout correto do repositório em cada agente; as instruções acompanham a branch e precisam estar presentes nela para serem compartilhadas.

| Agente | Regras | Skills |
|---|---|---|
| Codex | AGENTS.md, incluindo a seção específica do Codex | .agents/skills |
| Antigravity | AGENTS.md e complemento GEMINI.md | .agents/skills |
| Claude Code | CLAUDE.md importa AGENTS.md | .claude/skills encaminha para .agents/skills |

## Onde atualizar

- Regras comuns de produto, código, Git, validação e acessibilidade: AGENTS.md.
- Procedimentos de revisão, interface, SDD e divulgação: o SKILL.md correspondente em .agents/skills.
- Metadados de descoberta: ao mudar name ou description de uma skill, atualizar também o frontmatter da entrada correspondente em .claude/skills.
- Particularidades de ferramentas: seção do Codex no AGENTS.md, GEMINI.md ou CLAUDE.md.
- O agente de revisão do Claude usa a skill comum; não manter outra lista de critérios dentro dele.

As entradas do Claude usam caminhos relativos, sem symlinks, para funcionar em checkouts Windows sem depender de permissões de criação de links. Não importar automaticamente configurações globais, modelos ou plugins pessoais de um colega.

## Conferência em cada ambiente

Após atualizar a branch, abrir uma nova sessão na raiz do checkout e pedir ao agente que identifique a branch, a origem das regras e as quatro skills locais: code-reviewer, desktop-accessible-ui, sdd e novidades. No Claude, conferir que a entrada da skill abre o arquivo canônico; no Antigravity, conferir as skills disponíveis no painel de personalizações. Testar a descoberta não exige gerar um anúncio ou alterar código.

Build e testes não validam o carregamento de instruções pelos três aplicativos. Registrar separadamente a revisão dos arquivos e a conferência em uma sessão real de cada ferramenta.

## Documentação dos formatos

- [Codex: AGENTS.md](https://learn.chatgpt.com/docs/agent-configuration/agents-md) e [skills](https://learn.chatgpt.com/docs/build-skills).
- [Antigravity: regras](https://antigravity.google/docs/rules/) e [skills](https://antigravity.google/docs/skills).
- [Claude Code: importação de instruções](https://code.claude.com/docs/en/memory#share-one-file-with-other-coding-tools) e [skills](https://code.claude.com/docs/en/skills).
