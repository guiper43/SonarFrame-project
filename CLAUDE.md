# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## O que é

sonarFrame é um editor de vídeo não destrutivo para Windows, com interface WPF operável integralmente por teclado, voltado primariamente para usuários de leitor de tela (NVDA). Versão atual: **0.1.0** — montagem, importação real, camadas, histórico, persistência SQLite e recuperação. Reprodução, exportação, legendas e IA ainda não estão implementadas (ver `docs/release.md`).

A visão de produto completa está no `README.md`; a referência funcional detalhada (requisitos, critérios de aceitação, decisões técnicas) é `docs/sonarFrame_ERS_v05.md` — use-a para orientar implementação e mudanças de escopo, não o README.

## Fluxo obrigatório após gerar/alterar código

Ao final de qualquer código gerado ou alterado, antes de considerar a tarefa entregue:

1. Rodar o build do projeto (`./build.ps1`, que já inclui os testes — ou `dotnet build SonarFrame.slnx -c Release -m:1`).
2. Executar a suíte de testes (`dotnet test SonarFrame.slnx -c Release -m:1`, ou o filtro específico do que foi alterado).
3. Acionar o agente `code-reviewer` sobre o código gerado.

Só reportar a tarefa como concluída/entregue quando build, testes e revisão do `code-reviewer` passarem sem apontamentos pendentes. Se algo falhar, corrigir e repetir o fluxo antes de dar como pronto.

## Comandos

Requer SDK .NET 10 (ou um SDK local em `.tools/dotnet`, ignorado pelo Git e preferido pelos scripts quando presente).

```powershell
./setup-media.ps1       # baixa FFprobe/FFmpeg fixados em .tools (verifica SHA-256); necessário para habilitar os testes de mídia real
./build.ps1 [-Pack]      # build + testes em Release (-m:1); -Pack também gera os 3 .nupkg em artifacts/packages
./run-desktop.ps1 [-Publish]  # roda a interface via dotnet run; -Publish gera distribuição win-x64 self-contained em artifacts/desktop
./release.ps1 -ExpectedTag v0.1.0  # setup-media + build + pack + publish da distribuição completa em artifacts/releases/<versão>
```

Equivalentes diretos com SDK no PATH (os scripts apenas configuram `.tools` como cache local de NuGet/dotnet e variáveis `SONARFRAME_FFPROBE`/`SONARFRAME_FFMPEG`):

```powershell
dotnet restore SonarFrame.slnx --locked-mode
dotnet test SonarFrame.slnx -c Release -m:1
dotnet test tests/SonarFrame.Tests --filter "FullyQualifiedName~TestesEdicao" -m:1   # rodar uma classe/teste específico
dotnet pack SonarFrame.slnx -c Release --no-build -o artifacts/packages -m:1
```

**`-m:1` é deliberado em todos os scripts** (evita concorrência de build/teste) — não remova ao "otimizar".

**Sem `./setup-media.ps1` (ou as variáveis `SONARFRAME_FFPROBE`/`SONARFRAME_FFMPEG`), os testes de mídia real em `TestesMidiasReais.cs` se autoignoram** (skip). `dotnet test` direto roda uma suíte menor que a CI, que sempre executa `setup-media.ps1` antes do build (`.github/workflows/build.yml`).

Adicionar/atualizar um pacote NuGet exige regenerar o `packages.lock.json` correspondente — a CI usa `--locked-mode` e falha em divergência.

## Arquitetura

Quatro projetos em camadas estritas, mais testes e um demo de CLI:

```
SonarFrame.Core          domínio imutável (records), sem dependência de mídia/banco/WPF — net10.0
SonarFrame.Application   SessaoEdicao, comandos, histórico, importação, persistência — depende de Core — net10.0
SonarFrame.Infrastructure RepositorioProjetosSqlite, LeitorMetadadosFfprobe, catálogo — depende de Application — net10.0
SonarFrame.Desktop       interface WPF — depende de Infrastructure — net10.0-windows
```

Os três primeiros são empacotados como NuGet (`dotnet pack`) e **não podem depender de WPF**; só `Desktop` é `net10.0-windows`. `samples/SonarFrame.Demo` é um CLI que exercita `create`/`inspect`/`simulate-crash` sobre um `.sonarframe` com metadados fictícios (não lê/renderiza vídeo real) — útil para testar persistência e recuperação sem UI.

### Modelo de domínio (`SonarFrame.Core`)

- `Projeto` é um record imutável: `Biblioteca` (mídias), `LinhaDoTempo` (clipes principais, sequência contígua), `Camadas` (clipes independentes sobrepostos), `Cursor`, `Selecao`. Toda transição de estado passa por `Projeto.Validar()`, que é a única fonte de verdade das invariantes (mídia existe, IDs únicos, clipe não ultrapassa a origem, camada cabe na composição, volume 0–200, etc.). **Novo comando não deve reimplementar validação — deve confiar em `Validar()`.**
- Comandos implementam `IComandoEdicao.Aplicar(Projeto) → Projeto` (em `ComandosEdicao.cs`/`ComandosCamadas.cs`/`InserirClipes.cs`) e são puros: recebem um projeto, devolvem outro.
- `TempoMidia` representa tempo como fração reduzida de inteiros de 64 bits (operações intermediárias em `BigInteger`, overflow rejeitado) — **nunca usar ponto flutuante para cortes**; formatação em ms é só apresentação. Intervalos são semiabertos (início incluso, fim excluso).
- Identificadores dos records são em português, mas a serialização JSON usa `[JsonPropertyName]` em inglês para compatibilidade com arquivos já persistidos (`Nome`→`"Name"`, `Biblioteca`→`"Library"`, `MidiaId`→`"AssetId"`, etc. — ver `Projeto.cs`). **Renomear uma propriedade sem preservar seu `JsonPropertyName` corrompe silenciosamente a leitura de projetos `.sonarframe` existentes.**

### Sessão e aplicação (`SonarFrame.Application`)

- `SessaoEdicao` é a fachada central: mantém `Atual` (snapshot validado), pilhas de desfazer/refazer (snapshots completos, não deltas) e uma fila das últimas 100 `NotificacaoEditor`. `Executar(IComandoEdicao)` aplica, valida e só então empurra para o histórico — se o resultado for igual ao atual (`record` equality), não gera entrada de histórico.
- `Posicionar`, `Deslocar`, `AvancarQuadro` e `SelecionarItem` **não** entram no histórico de desfazer/refazer, mas alteram `atual` e portanto também deixam o projeto "pendente de salvar" — navegação suja o estado de persistência mesmo sem editar a composição.
- `AvancarQuadro` exige taxa de quadros constante conhecida (`Midia.TaxaQuadros`) ou um `INavegadorQuadros` externo; lança `InvalidOperationException` para mídia VFR/sem taxa sem navegador.
- Contratos de integração futura ainda sem implementação: `IReprodutorPrevia`, `IExportador` (ambos em `Contratos.cs`). Não assumir comportamento de reprodução/exportação — essas funcionalidades simplesmente não existem nesta versão.
- `Alterado` (evento de `SessaoEdicao`) dispara na thread que executou a operação — qualquer adaptador de UI deve despachar manualmente para a thread de UI.

### Infraestrutura (`SonarFrame.Infrastructure`)

- `RepositorioProjetosSqlite`: banco com `application_id`, `user_version = 1`, uma linha com snapshot JSON versionado do projeto inteiro (não é um esquema relacional). Versão desconhecida é rejeitada ao salvar e ao carregar. Escrita de projeto novo é atômica via arquivo temporário + rename; atualização usa transação SQLite. Trabalho de disco roda em thread dedicada (o provedor SQLite é sincronamente bloqueante mesmo nas APIs async).
- Recuperação: todo salvamento grava primeiro `<projeto>.recovery`, depois o arquivo principal. A interface deve sempre oferecer aceitar/descartar a recuperação — nunca aplicá-la silenciosamente.
- `LeitorMetadadosFfprobe` invoca `ffprobe.exe` como processo separado (sem shell, argumentos separados, timeout de 30s), nunca envia mídia para a internet. Ver origem/licença em `docs/media-tools.md`.

### Interface (`SonarFrame.Desktop`)

- WPF, conecta `SessaoEdicao`/`PersistenciaProjeto` à UI. `JanelaPrincipal.xaml.cs` é o shell; `JanelaPrincipal.Edicao.cs` e `JanelaPrincipal.Projetos.cs` são partial classes para edição e gestão de projetos.
- **Acessibilidade é requisito de produto, não refinamento**: toda operação precisa funcionar 100% por teclado, com nomes de UI Automation coerentes para NVDA. Mudança de UI que só funciona com mouse é regressão. Ver tabela de atalhos em `docs/desktop.md` e princípios em `README.md`.

## Convenções

- Identificadores de código, comentários, mensagens de commit e documentação são em **português** (nomes de classes, métodos e variáveis incluídos) — siga essa convenção em código novo.
- `Directory.Build.props` fixa `TreatWarningsAsErrors=true` e `Nullable=enable` para todos os projetos — qualquer warning quebra o build.
- `Directory.Build.props` também é a única fonte da versão (`<Version>`) aplicada aos três pacotes NuGet; a tag de release correspondente segue o padrão `v<versão>`.
- Testes usam xUnit (`tests/SonarFrame.Tests` para domínio/aplicação/infraestrutura, `tests/SonarFrame.Desktop.Tests` para ligação WPF↔core, rodando em thread STA sem exibir janelas).
