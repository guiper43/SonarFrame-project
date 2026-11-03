## Resumo

<!-- Descreva de forma objetiva o problema resolvido ou a funcionalidade entregue. -->

## Tipo de promoção no fluxo

- [ ] `feat/*` / `fix/*` / `chore/*` -> `develop` (integração de desenvolvimento)
- [ ] `develop` -> `homolog` (congelamento de versão / candidato à homologação)
- [ ] `homolog` -> `main` (promoção para produção após homologação aprovada)
- [ ] `hotfix/*` -> `main` (correção crítica em produção — requer backport para `homolog` e `develop`)

## Requisitos e rastreabilidade

<!-- Informe os itens relacionados de docs/sonarFrame_ERS_v05.md (ex.: RF, RNF, CA) ou issue associada, se aplicável. -->

## Checklist técnico e de arquitetura

- [ ] `./build.ps1` (compilação e suíte de testes em `Release` com `-m:1`) executado sem erros ou avisos
- [ ] Quando há mudanças em prévia/reprodução nativa ou formatos de mídia: `./testar-previa-desktop.ps1` e `./validar-reproducao.ps1` validados
- [ ] Quando há adição ou atualização de pacotes NuGet: `packages.lock.json` atualizado (`--locked-mode`)
- [ ] Invariantes do domínio concentradas em `Projeto.Validar()` e operações temporais exatas em `TempoMidia`
- [ ] Compatibilidade de serialização mantida (`[JsonPropertyName]` existentes e esquema SQLite preservados)
- [ ] Sem dependência de WPF em `SonarFrame.Core`, `SonarFrame.Application` ou `SonarFrame.Infrastructure`

## Checklist de acessibilidade (NVDA e teclado)

- [ ] Operação 100% funcional por teclado (`Tab`, `Shift+Tab`, setas, `Enter`, `Space`, `Delete`, `Esc`) sem armadilhas de foco
- [ ] Controles interativos possuem nomes acessíveis e regiões (`AutomationProperties.Name` / `LiveSetting`) coerentes para o NVDA
- [ ] Nenhum estado é comunicado exclusivamente por cor ou pista visual
- [ ] Na promoção `develop -> homolog` ou `homolog -> main`: binário publicado em `artifacts/desktop` (`./run-desktop.ps1 -Publish`) validado manualmente com NVDA
