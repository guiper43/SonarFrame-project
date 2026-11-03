# ADR 0001 — Injeção de Dependências e Composition Root no sonarFrame

* **Status**: Aceito
* **Data**: 2026-11-02

## Contexto

O **sonarFrame** é estruturado em quatro camadas (`SonarFrame.Core`, `SonarFrame.Application`, `SonarFrame.Infrastructure` e `SonarFrame.Desktop`), sendo as três primeiras empacotadas como pacotes NuGet independentes de WPF e LibVLCSharp.

Com a evolução das funcionalidades de persistência SQLite, leitura de metadados via FFprobe, preparação de composição com cache em blocos via FFmpeg e prévia nativa via LibVLCSharp, a camada `SonarFrame.Desktop` passou a instanciar manualmente serviços de infraestrutura e aplicação em diferentes pontos de `JanelaPrincipal` e de seus arquivos parciais.

## Problema

A instanciação manual distribuída nos arquivos de code-behind da interface apresentava quatro problemas concretos:

1. **Duplicação de configuração e resolução de caminhos**: a localização de `tools/ffprobe.exe`, `tools/ffmpeg.exe` e `settings.json` era repetida em múltiplos métodos (`AdicionarMidia`, `LocalizarMidia`, `ConfigurarComposicao` e `JanelaPrincipal.Projetos.cs`), com divergência no tratamento de fallback do executável `ffprobe`.
2. **Acoplamento direto a implementações concretas**: `JanelaPrincipal` mantinha campos fortemente tipados para `RepositorioProjetosSqlite` e instanciava `LeitorMetadadosFfprobe`, `ExecutorFfprobe` e `CatalogoProjetos` diretamente durante eventos de interface.
3. **Ausência de gerenciamento unificado de ciclo de vida e encerramento**: o aplicativo iniciava a janela via `StartupUri` em `App.xaml`, sem um host central para validar o grafo de dependências na inicialização ou garantir o descarte assíncrono determinístico do escopo da janela ao encerrar.
4. **Risco de divergência entre agentes de desenvolvimento**: sem um Composition Root explícito e regras documentadas sobre ciclos de vida, diferentes agentes (Codex, Gemini/Antigravity e Claude Code) poderiam introduzir novos serviços com instanciações avulsas ou padrões conflitantes.

## Alternativas consideradas

1. **Manter instanciação manual em `JanelaPrincipal` (Pure DI disperso)**:
   * *Vantagens*: zero pacotes adicionais.
   * *Desvantagens*: mantém acoplamento na classe de janela, dificulta substituição uniforme em testes de integração e não oferece validação automática de escopos (`ValidateScopes`) e de grafo (`ValidateOnBuild`).
2. **Introduzir contêineres de terceiros (Autofac, DryIoc) ou frameworks de MVVM (Prism, CommunityToolkit)**:
   * *Vantagens*: recursos avançados de registro por convenção e navegação.
   * *Desvantagens*: adiciona dependências externas desnecessárias, incentiva reflexão implícita e viola a diretriz do projeto de preservar a arquitetura WPF enxuta e acessível sem reescrever para MVVM.
3. **Adotar `Microsoft.Extensions.DependencyInjection` e `Microsoft.Extensions.Hosting` restritos ao Composition Root (`SonarFrame.Desktop`)**:
   * *Vantagens*: componentes oficiais do ecossistema .NET 10, registros explícitos e auditáveis sem reflexão por convenção, validação nativa de escopos e grafo no build do contêiner, suporte nativo a `IAsyncDisposable` via `AsyncServiceScope` e preservação total da independência de `Core`, `Application` e `Infrastructure`.
   * *Desvantagens*: exige gerenciar explicitamente a criação e o encerramento do `IHost` e do `AsyncServiceScope` em `App.xaml.cs`.

## Decisão adotada

Adotar `Microsoft.Extensions.DependencyInjection` e `Microsoft.Extensions.Hosting` exclusivamente em `SonarFrame.Desktop`, centralizando os registros em `src/SonarFrame.Desktop/Configuracao/RegistroDependencias.cs` (`AdicionarServicosSonarFrame` e `CriarHostSonarFrame`) e gerenciando o ciclo de vida em `App.xaml.cs`.

### Ciclos de vida definidos

| Serviço / Contrato | Implementação | Ciclo de vida | Justificativa técnica |
|---|---|---|---|
| `TimeProvider` | `TimeProvider.System` | `Singleton` | Provedor de tempo imutável e thread-safe do runtime .NET. |
| `IRepositorioProjetos` | `RepositorioProjetosSqlite` | `Singleton` | Sem estado mutável em memória; cada operação abre e descarta sua própria conexão SQLite com `Pooling = false`. |
| `IExecutorFfprobe` | `ExecutorFfprobe` | `Singleton` | Sem estado mutável; armazena apenas o caminho resolvido do `ffprobe` e cria um processo isolado por chamada. |
| `ILeitorMetadadosMidia` | `LeitorMetadadosFfprobe` | `Singleton` | Sem estado mutável; delega a inspeção ao `IExecutorFfprobe`. |
| `RepositorioConfiguracoes` | `RepositorioConfiguracoes` | `Singleton` | Sem estado mutável; lê e grava atomicamente o arquivo de preferências do usuário. |
| `CatalogoProjetos` | `CatalogoProjetos` | `Transient` | Coordenador leve sem estado e sem recursos descartáveis. |
| `IFabricaReprodutorMidia` | `FabricaPreviaVlc` | `Scoped` | Mantém a instância nativa `LibVLC` e o vínculo com a superfície visual da janela ativa; implementa `IAsyncDisposable`. |
| `PreviaBiblioteca` | `PreviaBiblioteca` | `Scoped` | Coordena o estado mutável da prévia e o reprodutor ativo no escopo da janela; implementa `IAsyncDisposable`. |
| `IPreparadorComposicao` | `PreparadorComposicaoFfmpeg` | `Scoped` | Gerencia diretório temporário exclusivo com trava `em-uso`, cache de metadados e `IndiceQuadros` por janela; implementa `IAsyncDisposable`. |
| `JanelaPrincipal` | `JanelaPrincipal` | `Transient` | Janela WPF resolvida dentro do `AsyncServiceScope` da sessão visual, recebendo todas as dependências por construtor. |

### Responsabilidades que permanecem fora do contêiner

O contêiner é o único responsável pelo descarte dos serviços que cria. `PreviaBiblioteca` libera o reprodutor ativo, mas não descarta a fábrica recebida por injeção; consumidores fora do contêiner devem descartar essa fábrica. A janela cancela e aguarda suas operações e limpa a prévia antes de solicitar ao `App` o encerramento do escopo e do host, enquanto o Dispatcher ainda está disponível. O encerramento tenta descartar o escopo, parar o host e descartar o host mesmo quando uma etapa falha, preservando os erros em uma `AggregateException`.

* **Entidades de domínio e tipos de valor (`Projeto`, `Midia`, `Clipe`, `CamadaTemporal`, `TempoMidia`, comandos `IComandoEdicao`)**: permanecem puros e imutáveis em `SonarFrame.Core`, instanciados diretamente pelas operações de domínio.
* **Estado por projeto aberto (`SessaoEdicao`, `PersistenciaProjeto`, `AcoesEdicao`)**: o ciclo de vida de `SessaoEdicao` e `PersistenciaProjeto` acompanha cada projeto aberto ou criado pelo usuário (incluindo transições de retorno à tela inicial e "Salvar como"), e não o tempo de vida do processo ou da janela. Esses objetos continuam sendo criados ao abrir/criar um projeto, recebendo os serviços injetados na janela (`IRepositorioProjetos`, `ILeitorMetadadosMidia`, `TimeProvider`).
* **Diálogos modais contextuais (`DialogoImportacao`)**: recebem a janela proprietária, a `SessaoEdicao` corrente e o `ILeitorMetadadosMidia` injetado.

## Justificativa

O uso do contêiner oficial restrito a `SonarFrame.Desktop` elimina a criação manual repetitiva de serviços de infraestrutura e unifica a resolução dos binários `ffmpeg` e `ffprobe`, sem poluir os pacotes NuGet (`Core`, `Application`, `Infrastructure`) com dependências de hospedagem ou do contêiner. A separação entre serviços `Singleton` (infraestrutura stateless) e `Scoped` (recursos descartáveis e caches por janela) evita retenção indevida de objetos `IAsyncDisposable` e prepara a arquitetura para evolução futura sem acoplamento global.

## Consequências

* `App.xaml` deixa de usar `StartupUri` e `App.xaml.cs` passa a iniciar o `IHost`, criar o `AsyncServiceScope` da janela principal e descartá-los no encerramento.
* `ValidateScopes = true` e `ValidateOnBuild = true` garantem que qualquer dependência ausente ou captura indevida de escopo falhe imediatamente na construção do host e nos testes automatizados.
* Todos os testes de integração em `SonarFrame.Desktop.Tests` passam a construir `JanelaPrincipal` através do Composition Root (`RegistroDependencias.CriarHostSonarFrame`), permitindo substituir `IFabricaReprodutorMidia` e `IPreparadorComposicao` de maneira padronizada.

## Limitações

* A aplicação continua operando com uma única janela principal e um projeto ativo por vez na interface atual; o suporte a múltiplos projetos simultâneos em janelas/abas independentes permanece fora do escopo desta etapa, embora o isolamento em `Scoped` já evite colisão de estado entre escopos.

## Regras de evolução

1. Novos serviços de infraestrutura ou aplicação devem ser registrados explicitamente em `RegistroDependencias.AdicionarServicosSonarFrame`, sem varredura por reflexão.
2. Nunca adicionar referência a `Microsoft.Extensions.DependencyInjection` ou `Microsoft.Extensions.Hosting` em `SonarFrame.Core`.
3. Serviços que implementem `IDisposable` ou `IAsyncDisposable` devem ter seu ciclo de vida (`Singleton` ou `Scoped`) e ordem de descarte validados por teste automatizado em `TestesInjecaoDependencias`.

## Critérios de revisão

Em revisões de código e Pull Requests (por humanos, Codex, Gemini ou Claude Code), rejeitar alterações que:

* Instanciem manualmente `RepositorioProjetosSqlite`, `ExecutorFfprobe`, `LeitorMetadadosFfprobe`, `PreparadorComposicaoFfmpeg`, `RepositorioConfiguracoes` ou `CatalogoProjetos` dentro da camada de apresentação fora de `RegistroDependencias`.
* Injetem `IServiceProvider` em classes de negócio (Service Locator).
* Criem contêineres paralelos ou desabilitem `ValidateScopes` / `ValidateOnBuild`.
