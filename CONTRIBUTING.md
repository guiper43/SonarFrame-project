# Fluxo de Trabalho e Governança do Repositório

O `sonarFrame` adota um fluxo corporativo em quatro camadas para isolar o desenvolvimento diário, a validação manual com leitor de tela (NVDA) em versão congelada e as entregas estáveis de produção.

## 1. Arquitetura de Branches

```text
feat/* | fix/* | chore/* ──(PR)──> develop ──(PR)──> homolog ──(PR)──> main (vX.Y.Z)
                                      ▲                 ▲                │
                                      └────(backport)───┴─── hotfix/* <──┘
```

| Branch | Papel no ciclo de vida | Origem | Destino (via PR) | Artefatos de CI |
|---|---|---|---|---|
| `feat/*`, `fix/*`, `chore/*`, `docs/*` | Desenvolvimento isolado de funcionalidades, correções ou manutenção | `develop` | `develop` | Pacotes NuGet de verificação (`sonarframe-packages`) |
| `develop` | Integração contínua das entregas prontas para a próxima versão | `main` (inicial) | `homolog` | Pacotes NuGet de verificação (`sonarframe-packages`) |
| `homolog` | Congelamento de versão (*Release Candidate*) para homologação com NVDA, teclado e bateria de formatos reais | `develop` | `main` | Distribuição portátil completa + pacotes + hashes (`sonarframe-homolog`) |
| `main` | Produção estável; cada versão homologada recebe tag `vX.Y.Z` | `homolog` / `hotfix/*` | — | Release oficial validada contra `Directory.Build.props` (`sonarframe-release`) |
| `hotfix/*` | Correção emergencial de incidente crítico identificado em produção | `main` | `main` (seguido de sincronização em `homolog` e `develop`) | Pacotes NuGet de verificação (`sonarframe-packages`) |

## 2. Convenção de Nomes de Branches e Commits

### Nomes de branches de trabalho

- `feat/<escopo-curto>` — nova funcionalidade (ex.: `feat/reproducao-e-previa`, `feat/exportacao-mp4`)
- `fix/<escopo-curto>` — correção de defeito encontrado em `develop` ou `homolog` (ex.: `fix/foco-dialogo-importacao`)
- `hotfix/<escopo-curto>` — correção urgente originada de `main`
- `chore/<escopo-curto>` ou `docs/<escopo-curto>` — manutenção de build, dependências ou documentação

### Mensagens de commit (Conventional Commits em português)

```text
<tipo>(<escopo>): <descrição imperativa curta sem ponto final>
```

Exemplos:
- `feat(desktop): integra previa da biblioteca e exibicao de quadros`
- `fix(composicao): corrige ordenacao temporal de camadas sobrepostas`
- `test(reproducao): adiciona validacao nativa de formatos e benchmarks`
- `chore(build): padroniza publicacao desktop e empacotamento`
- `docs(fluxo): documenta governanca corporativa de branches`

## 3. Ciclo de Promoção Passo a Passo

### Etapa 1 — Desenvolvimento (`feat/*` -> `develop`)

1. Criar a branch a partir de `develop` atualizada:
   ```powershell
   git checkout develop
   git pull origin develop
   git checkout -b feat/minha-entrega
   ```
2. Antes de abrir o Pull Request para `develop`, executar localmente:
   ```powershell
   ./build.ps1
   # Se alterar prévia, reprodução nativa ou formatos de mídia:
   ./testar-previa-desktop.ps1
   ./validar-reproducao.ps1
   ```
3. Abrir Pull Request tendo `develop` como base e preencher o checklist de `.github/pull_request_template.md`.

### Etapa 2 — Promoção para Homologação (`develop` -> `homolog`)

1. Quando o conjunto de entregas da versão em `develop` estiver completo, abrir Pull Request de `develop` para `homolog`.
2. Ao concluir o merge em `homolog`, o workflow `.github/workflows/release.yml` gera automaticamente o artefato `sonarframe-homolog` contendo:
   - `sonarFrame-<versao>-win-x64.zip` (aplicação desktop portátil self-contained)
   - `packages/*.nupkg` (`SonarFrame.Core`, `SonarFrame.Application`, `SonarFrame.Infrastructure`)
   - `SHA256SUMS.txt`, `CHANGELOG.md` e `release.md`
3. Durante a janela de homologação:
   - Novas funcionalidades para a versão seguinte podem continuar entrando em `develop` sem interferir na homologação.
   - Correções de bugs detectados na homologação são feitas em `fix/*` -> `develop` e promovidas a `homolog` (ou aplicadas em `homolog` e espelhadas em `develop`).
   - Validar os fluxos de teclado e anúncios com o leitor de tela NVDA no pacote de homologação (ou localmente em `artifacts/desktop` gerado por `./run-desktop.ps1 -Publish`).

### Etapa 3 — Promoção para Produção (`homolog` -> `main`)

1. Confirmar que `Directory.Build.props`, `CHANGELOG.md` e `docs/release.md` refletem a versão `X.Y.Z` homologada.
2. Abrir Pull Request de `homolog` para `main`.
3. Após o merge em `main`, criar e publicar a tag da versão:
   ```powershell
   git checkout main
   git pull origin main
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```
4. O push da tag `vX.Y.Z` aciona `.github/workflows/release.yml` com verificação estrita (`-ExpectedTag vX.Y.Z`) e publica o artefato oficial `sonarframe-release`.
5. Sincronizar `main` de volta em `homolog` e `develop` caso haja ajustes de fechamento de release.

## 4. Regras de Proteção de Branches (GitHub Branch Protection)

As branches estruturais (`main`, `homolog` e `develop`) devem permanecer protegidas no GitHub contra push direto, force push e exclusão, exigindo Pull Request com o check obrigatório `test` (definido em `.github/workflows/build.yml`).

Com o [GitHub CLI (`gh`)](https://cli.github.com/) autenticado (`gh auth login`) e permissão de administrador no repositório, execute os comandos abaixo no PowerShell para aplicar as regras nas três branches:

```powershell
$repo = "guiper43/SonarFrame-project"

$regrasProtecao = @{
    required_status_checks = @{
        strict   = $true
        contexts = @("test")
    }
    enforce_admins = $false
    required_pull_request_reviews = @{
        dismiss_stale_reviews           = $true
        require_code_owner_reviews      = $false
        required_approving_review_count = 1
    }
    restrictions                   = $null
    required_linear_history        = $false
    allow_force_pushes             = $false
    allow_deletions                = $false
     required_conversation_resolution = $true
} | ConvertTo-Json -Depth 5

foreach ($branch in @("main", "homolog", "develop")) {
    $regrasProtecao | gh api `
        --method PUT `
        -H "Accept: application/vnd.github+json" `
        "/repos/$repo/branches/$branch/protection" `
        --input -
    Write-Host "Proteção aplicada em $branch"
}
```

> Caso o repositório seja operado por um único mantenedor e você queira permitir aprovar/mesclar os próprios PRs sem um segundo revisor humano (mantendo o bloqueio de push direto e a obrigatoriedade da CI passar no job `test`), altere `required_approving_review_count` para `0` antes de executar o bloco acima.
