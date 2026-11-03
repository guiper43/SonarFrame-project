# Preferências de desenvolvimento

- Escrever código de domínio em português brasileiro, preservando os padrões existentes.
- Manter APIs, frameworks e termos técnicos estabelecidos em inglês.
- Não adicionar comentários ao código por padrão.

# Entregas para validação

- Publicar a versão de teste sempre em `artifacts/desktop` com `./run-desktop.ps1 -Publish`.
- Sobrescrever a publicação anterior; não criar pastas `desktop-<feature>` ou acumular versões em `artifacts`.
- Manter em `artifacts` somente a última publicação para validação. Amostras, relatórios, pacotes intermediários e laboratórios ficam em `.tools`.
- Se o aplicativo estiver aberto e impedir a substituição, solicitar que seja fechado; não encerrar o processo automaticamente e não criar outra pasta de versão.
- Não apagar projetos ou mídias do usuário ao limpar artefatos. Conferir os caminhos absolutos antes de excluir ou mover diretórios.

# Contexto do produto

- `sonarFrame` é um editor de vídeo não destrutivo para Windows, com interface WPF operável integralmente por teclado, voltado primariamente para usuários de leitor de tela (NVDA).
- A referência funcional detalhada (requisitos, critérios de aceitação e decisões técnicas) é `docs/sonarFrame_ERS_v05.md` — usá-la para orientar implementação e mudanças de escopo.

# Fluxo obrigatório após gerar ou alterar código

Ao final de qualquer código gerado ou alterado, antes de considerar a tarefa entregue:

1. Rodar o build e a suíte de testes (`./build.ps1`, ou `dotnet build SonarFrame.slnx -c Release -m:1` seguido de `dotnet test SonarFrame.slnx -c Release -m:1`).
2. Quando a alteração envolver prévia/reprodução nativa ou formatos de mídia, validar também com `./testar-previa-desktop.ps1` e `./validar-reproducao.ps1`.
3. Revisar o diff aplicando os critérios da skill `code-reviewer` (bugs reais, regressões, concorrência, acessibilidade e ausência de comentários no código).

# Fluxo de branches e governança

- Hierarquia corporativa em quatro camadas: `feat/*` / `fix/*` / `chore/*` -> `develop` -> `homolog` -> `main` (e `hotfix/*` -> `main` com backport para `homolog` e `develop`).
- `develop` concentra a integração contínua das features; `homolog` congela a versão para homologação com NVDA/teclado e gera o pacote `sonarframe-homolog` na CI; `main` representa produção estável onde tags `vX.Y.Z` geram `sonarframe-release`.
- Não fazer push direto em `main`, `homolog` ou `develop`; toda promoção ocorre via Pull Request seguindo `.github/pull_request_template.md` e `CONTRIBUTING.md`.

# Comandos principais

Requer SDK .NET 10 (ou SDK local em `.tools/dotnet`, preferido pelos scripts quando presente).

```powershell
./setup-media.ps1               # baixa FFprobe/FFmpeg fixados em .tools (verifica SHA-256); necessário para testes de mídia real
./build.ps1 [-Pack]              # build + testes em Release (-m:1); -Pack gera os .nupkg em .tools/packages
./run-desktop.ps1 [-Publish]     # roda a interface; -Publish publica win-x64 self-contained em artifacts/desktop
./testar-previa-desktop.ps1      # executa os testes de prévia e reprodução com o motor nativo LibVLC + FFmpeg
./validar-reproducao.ps1         # valida matriz de 10 formatos importáveis (-Benchmark / -BenchmarkBuscas opcionais)
./release.ps1 -ExpectedTag vX.Y.Z # setup-media + build + pack + publish em .tools/releases/<versão>
```

- `-m:1` é deliberado em todos os scripts (evita concorrência de build/teste) — não remover.
- Adicionar ou atualizar pacote NuGet exige atualizar o `packages.lock.json` correspondente (a CI usa `--locked-mode`).
- `Directory.Build.props` fixa `TreatWarningsAsErrors=true` e `Nullable=enable` para todos os projetos.

# Arquitetura e invariantes

```text
SonarFrame.Core           domínio imutável (records), sem dependência de mídia/banco/WPF — net10.0
SonarFrame.Application    SessaoEdicao, comandos, histórico, prévia (PreviaBiblioteca), contrato IPreparadorComposicao — net10.0
SonarFrame.Infrastructure RepositorioProjetosSqlite, LeitorMetadadosFfprobe, PreparadorComposicaoFfmpeg, IndiceQuadros — net10.0
SonarFrame.Desktop        interface WPF e adaptação LibVLCSharp (FabricaPreviaVlc) — net10.0-windows
```

- `SonarFrame.Core`, `SonarFrame.Application` e `SonarFrame.Infrastructure` são empacotados como NuGet e **não podem depender de WPF**.
- **`Projeto.Validar()`** é a única fonte de verdade das invariantes do domínio. Novos comandos (`IComandoEdicao.Aplicar(Projeto) -> Projeto`) são puros e devem confiar em `Validar()`.
- **`TempoMidia`** representa tempo como fração reduzida de inteiros de 64 bits (operações intermediárias em `BigInteger`) — nunca usar ponto flutuante para cortes. Intervalos são semiabertos `[início, fim)`.
- **Compatibilidade JSON/SQLite**: identificadores em C# são em português, mas `[JsonPropertyName]` mantém nomes em inglês (`"Name"`, `"Library"`, `"AssetId"`, etc.) em `Projeto.cs`. Nunca alterar `JsonPropertyName` existentes para não quebrar projetos `.sonarframe` salvos.
- **Persistência e recuperação**: todo salvamento grava primeiro `<projeto>.recovery` e depois o arquivo principal. A UI sempre oferece aceitar ou descartar a recuperação — nunca aplicá-la silenciosamente.
- **Acessibilidade em `SonarFrame.Desktop`**: toda operação precisa funcionar 100% por teclado, com nomes e regiões de UI Automation coerentes para NVDA (ver skill `desktop-accessible-ui` e `docs/desktop.md`).

# Injeção de dependências (Dependency Injection)

- **Composition Root**: `SonarFrame.Desktop` é o único Composition Root da aplicação (`src/SonarFrame.Desktop/Configuracao/RegistroDependencias.cs` e `App.xaml.cs`).
- **Contêiner oficial**: utilizar exclusivamente `Microsoft.Extensions.DependencyInjection` e `Microsoft.Extensions.Hosting` com `ValidateScopes = true` e `ValidateOnBuild = true`. Não introduzir contêineres externos, reflexão por convenção ou frameworks de MVVM.
- **Injeção por construtor**: todos os serviços gerenciados devem receber suas dependências explicitamente pelo construtor.
- **Proibição de Service Locator**: não injetar `IServiceProvider` em classes de domínio, aplicação ou infraestrutura, nem resolver serviços dinamicamente fora do Composition Root.
- **Contêiner único**: manter um único `IHost` por instância da aplicação; não chamar `BuildServiceProvider()` durante o registro de serviços.
- **Independência das camadas**: `SonarFrame.Core`, `SonarFrame.Application` e `SonarFrame.Infrastructure` permanecem independentes do contêiner de DI, de WPF e de LibVLCSharp.
- **Critérios de ciclo de vida**:
  - `Singleton`: apenas serviços sem estado mutável de sessão ou seguros para uso concorrente durante toda a vida do processo (`TimeProvider`, `IRepositorioProjetos`, `IExecutorFfprobe`, `ILeitorMetadadosMidia`, `RepositorioConfiguracoes`).
  - `Scoped`: serviços que mantêm estado ou recursos descartáveis vinculados ao escopo ativo da janela/contexto de edição (`IFabricaReprodutorMidia`, `PreviaBiblioteca`, `IPreparadorComposicao`).
  - `Transient`: coordenadores leves sem recursos nativos/descartáveis retidos (`CatalogoProjetos`, `JanelaPrincipal`). Nunca registrar `IDisposable`/`IAsyncDisposable` como `Transient` quando resolvidos de forma que o contêiner os retenha indevidamente.
  - Objetos de domínio e estado por projeto aberto (`Projeto`, `SessaoEdicao`, `PersistenciaProjeto`, `AcoesEdicao`) continuam instanciados explicitamente conforme o ciclo do projeto, recebendo os serviços injetados na janela.
- **Descarte de recursos**: o `AsyncServiceScope` da janela e o `IHost` encerram deterministicamente os serviços `IDisposable`/`IAsyncDisposable` ao fechar a aplicação (`docs/adr/0001-injecao-de-dependencias.md`).
- **Testes e contratos**: preservar os contratos públicos existentes; qualquer alteração em `RegistroDependencias` exige cobertura em `tests/SonarFrame.Desktop.Tests/TestesInjecaoDependencias.cs`.

# Decisões de interação estabelecidas

- Timeline unificada: Alt+3 foca a lista. Cima/Baixo navega entre itens; Esquerda/Direita e modificadores navegam no tempo. Delete após seleção temporal exclui o intervalo; após navegação entre itens abre a ação de remover da composição, sem remover imediatamente. Shift+F10, Aplicações e botão direito abrem o menu de edição; Escape retorna à timeline. Divisão, exclusão, reordenação e histórico ficam nesse menu, com os atalhos diretos preservados.
- Navegação temporal comum anuncia a direção, o deslocamento e a posição em minutos e segundos: “Avançou 1 segundo. Atualmente em 0 minutos e 22 segundos.” Para deslocamentos menores que um segundo, anunciar “menos de 1 segundo”, sem inventar um segundo inteiro. Anunciar milissegundos na seleção de intervalo e na confirmação de corte.
- Não anunciar mensagens intermediárias sobre quadros, preparação ou retorno à composição durante uma busca bem-sucedida. Erros continuam sendo anunciados.
- Há um único botão Reproduzir/Pausar na área do vídeo. Os anúncios de estado são “Reproduzindo” e “Pausado”, sem distinguir biblioteca e composição. Espaço na timeline ativa a montagem.
- Enter em um elemento da timeline posiciona o vídeo no início desse elemento, inclusive camadas, preservando foco e seleção. Home e End vão aos extremos do vídeo.
- Tab percorre as ações da área antes de mudar de área. As setas navegam dentro das listas; alcançar seus extremos não abre as ações. Delete e ações visíveis permitem remover itens sem apagar as mídias originais.
- Na importação, focar realmente a guia selecionada; as setas trocam o tipo de importação. Tab acessa a escolha de arquivos e, ao concluir, o foco vai para Fechar.
- Em Meus projetos, o nome acessível começa por “Projeto: nome” e só depois informa o caminho.
- Durante a preparação, informar “Carregando vídeo: X%”, por etapas concluídas, sem anunciar blocos/cache ou repetir o mesmo percentual. Não apresentar o percentual como estimativa de tempo restante.
- Camadas visuais preservam proporção e transparência, centralizadas sem corte; a última camada adicionada fica acima das anteriores.

# Contexto compartilhado entre agentes

- Este AGENTS.md é a base comum para Codex, Antigravity e Claude Code. Regras de produto, arquitetura, acessibilidade, validação e Git devem ser alteradas aqui, não duplicadas nos arquivos específicos de cada ferramenta.
- As skills canônicas ficam em `.agents/skills`: `code-reviewer`, `desktop-accessible-ui`, `sdd` e `novidades`. Ler o SKILL.md correspondente quando a tarefa se enquadrar na descrição. As entradas em `.claude/skills` encaminham para esses mesmos arquivos; não manter cópias do conteúdo.
- Usar os estilos de `App.xaml`, cores do tema Windows e os helpers `CriarDialogo`/`AdicionarFechar`; não introduzir identidade visual paralela nem comunicar estado somente por cor.
- Confirmar o estado funcional na branch atual, no código e nos testes. Recurso em uma feature não implica disponibilidade na main ou em uma release. Se um script citado não existir no checkout, registrar essa diferença; não declarar sua execução nem importar uma feature apenas para executar a validação.
- Antes de editar, conferir branch, status e diff. Preservar alterações feitas pelo usuário ou por outro agente. Usar checkout/worktree isolado quando necessário para não misturar tarefas; adicionar ao commit somente arquivos ou trechos pertencentes à entrega.
- Usar Conventional Commits em português: `<tipo>(<escopo>): <descrição imperativa curta sem ponto final>`. Seguir CONTRIBUTING.md e o template de PR quando presentes; nunca contornar checks ou revisões obrigatórias.
- Modelos, plugins, permissões e ferramentas de uma plataforma não são requisitos das outras. Não copiar configurações pessoais ou credenciais entre agentes. A revisão exigida usa os mesmos critérios, com os recursos disponíveis em cada ambiente.

## Contexto do Codex

- O ponto de entrada é este AGENTS.md; descobrir as skills do projeto em `.agents/skills`.
- Aplicar a skill `code-reviewer` na revisão final. Delegar somente quando autorizado pelas instruções da sessão; a revisão não depende de um subagente ou modelo específico.
- Usar as ferramentas disponíveis na sessão para arquivos, terminal e Git. Não interpretar `.claude/settings.json` ou a configuração do Antigravity como configuração do Codex.

## Contexto do Antigravity e Claude Code

- Antigravity: complemento em GEMINI.md, com as mesmas skills de `.agents/skills`.
- Claude Code: complemento em CLAUDE.md, que importa este arquivo, e entradas em `.claude/skills` para o conteúdo canônico.
- Instruções específicas descrevem apenas o uso da ferramenta. Manter decisões de produto nesta base comum.
