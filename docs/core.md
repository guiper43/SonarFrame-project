# Núcleo SonarFrame 0.1.0

O núcleo implementa edição não destrutiva e persistência; ainda não é um aplicativo de edição de vídeo pronto para uso. Os três pacotes NuGet têm como alvo .NET 10 e não dependem de WPF.

## Pacotes e dependências

- `SonarFrame.Core`: projeto imutável, biblioteca, composição, tempo racional e comandos de edição. Sem dependências de processamento de mídia ou banco.
- `SonarFrame.Application`: `SessaoEdicao`, `AcoesEdicao`, histórico, seleção temporal, notificações, importação por contrato, salvamento e recuperação.
- `SonarFrame.Infrastructure`: `RepositorioProjetosSqlite`, `LeitorMetadadosFfprobe`, catálogo de projetos e armazenamento de preferências.

A infraestrutura depende da aplicação; a aplicação depende do domínio. A interface WPF cria e conecta esses componentes. Prévia, FFmpeg e exportação implementarão os contratos da aplicação e receberão snapshots imutáveis. Credenciais de IA não fazem parte do modelo persistido.

## Compilar, testar e gerar pacotes

Instale um SDK .NET 10 (ou utilize um SDK local em `.tools/dotnet`, ignorado pelo Git e preferido pelo script quando disponível):

```powershell
./build.ps1 -Pack
```

O script executa testes em Release e gera três arquivos `.nupkg` em `artifacts/packages`. Nenhum pacote é publicado em serviço externo. Os caches ficam em `.tools`; os arquivos `packages.lock.json` registram as dependências resolvidas.

Com um SDK no PATH, também é possível executar:

```powershell
dotnet restore SonarFrame.slnx --locked-mode
dotnet test SonarFrame.slnx -c Release -m:1
dotnet pack SonarFrame.slnx -c Release --no-build -o artifacts/packages -m:1
dotnet run --project samples/SonarFrame.Demo -- create artifacts/exemplo.sonarframe
dotnet run --project samples/SonarFrame.Demo -- inspect artifacts/exemplo.sonarframe
```

O exemplo usa metadados fictícios, não lê nem renderiza vídeos. `create` exige um arquivo de destino novo. A opção `simulate-crash` é uma ferramenta de teste: abre o projeto, inicia uma alteração SQLite e encerra o processo com código 73 sem commit. Uma abertura posterior deve recuperar o último estado confirmado.

Para consumir os pacotes, adicione `artifacts/packages` como fonte NuGet local e referencie `SonarFrame.Infrastructure` versão `0.1.0`; as dependências SonarFrame.Application e SonarFrame.Core serão resolvidas dessa mesma pasta. Mantenha nuget.org habilitado para as dependências de terceiros. Também é possível usar referências diretas aos projetos.

## Uso da sessão

```csharp
var sessao = new SessaoEdicao(Projeto.Criar("Meu vídeo"));
sessao.Executar(new RegistrarMidia(midia));
sessao.Executar(new InserirClipe(midia.Id, 0));
sessao.Executar(new DividirClipe(sessao.Atual.LinhaDoTempo[0].Id, TempoMidia.Segundos(4)));
sessao.Desfazer();
sessao.Refazer();

var persistencia = new PersistenciaProjeto(sessao, new RepositorioProjetosSqlite(), caminhoProjeto);
await persistencia.SalvarAsync();
```

Cada comando produz um novo projeto validado antes de entrar no histórico. Uma falha preserva estado e histórico e gera uma mensagem consultável. Depois de desfazer, uma nova edição elimina o caminho de refazer. Snapshots compartilham coleções imutáveis; o histórico fica apenas na memória e não tem limite configurável nesta versão.

`Atual`, cursor, intervalo selecionado e item selecionado são conceitos distintos. O foco de teclado pertence à interface. `Posicionar`, `Deslocar` e `AvancarQuadro` não adicionam entradas ao histórico de edição. A posição da sessão é persistida, portanto mudanças de navegação também podem deixar o projeto pendente de salvamento. `Posicionar(..., reproducao: true)` atualiza o cursor sem emitir anúncios. Selecionar um item não reproduz nem desloca o cursor.

`Alterado` comunica resultados na thread que executou a operação. O adaptador WPF deve usar seu Dispatcher para atualizar controles e UI Automation. Há até 100 mensagens recentes. Falhas em observadores não invalidam uma edição já confirmada. A sessão serializa commits; o aplicativo deve coordenar comandos de navegação e reprodução para não disputar o cursor.

## Semântica de tempo e edição

- `TempoMidia` representa segundos como uma fração reduzida de inteiros de 64 bits; operações intermediárias usam BigInteger e rejeitam overflow. Não usa ponto flutuante para cortes. A formatação em milissegundos é apenas para apresentação.
- Intervalos são semiabertos: início incluído e fim excluído. A sequência principal é contígua e sua duração resulta da soma dos clipes.
- Cada clipe tem ID próprio, ID da mídia, início na origem e duração. Divisão e corte preservam os arquivos originais e o vínculo audiovisual representado pelo clipe.
- Navegação CFR usa limites de quadros na origem, inclusive depois de deslocamentos por segundos. Para VFR ou mídia sem taxa de quadros, `INavegadorQuadros` é obrigatório; sua implementação real deve consultar timestamps da mídia.
- `ExcluirIntervalo` remove o intervalo da composição inteira. Camadas que cruzam o corte são recortadas/divididas; camadas posteriores recuam. O cursor fica no início do corte ou no novo fim quando se remove a cauda. Remover toda a composição é permitido.
- `MoverClipe` usa posição final com índice iniciado em zero. Reordenar ou inserir na sequência mantém camadas independentes em seus tempos absolutos. Não existe associação implícita entre uma camada e um clipe principal.
- Camadas devem estar inteiramente dentro da duração da sequência. Imagens sem duração de origem exigem duração explícita na inserção. Volume aceita 0 a 200 e não altera duração.
- `AtualizarCamada` altera início, recorte de origem e duração da camada. `RemoverCamada` remove apenas a camada. Ambas preservam a duração da sequência principal.
- `InserirClipes` aceita uma posição opcional de inserção e executa a seleção múltipla como uma única entrada de histórico.

As regras de camadas representam a composição temporal. Transições, empilhamento visual e renderização não fazem parte da versão 0.1.0.

## SQLite e recuperação

O arquivo sugerido é `.sonarframe`; a validação depende do conteúdo, não da extensão. O banco usa `application_id`, `user_version = 1` e tabela `project`, com uma linha cujo documento JSON contém o estado versionado. Esse formato continua inspecionável e editável com ferramentas SQLite. Mídias são referenciadas, nunca incorporadas. Campos derivados, como duração total, são recalculados.

A versão inicial usa um snapshot JSON transacional para manter a alteração inteira atômica e evitar um esquema relacional prematuro. Versões desconhecidas são rejeitadas, inclusive ao salvar; migrações futuras deverão ser explícitas. Novos arquivos são gravados em arquivo temporário adjacente e movidos para o destino após commit. Atualizações existentes usam transações SQLite. A infraestrutura executa o trabalho de disco em uma thread de trabalho, pois as APIs async do provedor SQLite executam sincronamente.

Crie um `PersistenciaProjeto` por sessão. Inicie `ExecutarSalvamentoAutomaticoAsync` com token cancelável para verificar a cada dois minutos. `SalvarAutomaticamenteSeNecessarioAsync` permite integração com outro agendador ou testes. Sem alterações, não há gravação. Ao salvar, um snapshot é capturado; edições que ocorram durante a gravação continuam pendentes.

O salvamento grava primeiro `<projeto>.recovery`, depois o projeto principal. A recuperação representa o último snapshot gravado de uma sessão que ainda não foi encerrada normalmente. Na próxima abertura, `ObterRecuperacaoAsync` devolve o candidato; a interface deve oferecer aceitar ou descartar, nunca aplicar silenciosamente. Para aceitar, crie uma sessão com o projeto recuperado e salve-a. Para descartar, remova a recuperação pelo fechamento explícito da sessão correspondente. A primeira recuperação só existe após o primeiro salvamento; alterações posteriores ao último salvamento podem ser perdidas numa falha.

Antes de `FecharAsync`, cancele e aguarde o loop de salvamento automático. O fechamento exige salvar alterações ou informar descarte explícito e remove o arquivo de recuperação. Não há garantia de recuperação de alterações que ainda existiam apenas na memória nem de durabilidade diante de falhas físicas do dispositivo.

`ImportacaoMidias.EncontrarAusentes` lista referências ausentes; `ReligarMidia` atualiza o caminho mantendo a montagem. `AcoesEdicao.ReligarAsync` valida tipo, duração e taxa de quadros com o leitor de metadados antes de executar a substituição. A importação em lote preserva os itens válidos quando outro falha.

## Cobertura e limites

Os testes cobrem biblioteca separada da composição (RF01), montagem e histórico (RF02), volume/camadas no modelo (parte de RF03), tempo e seleção (parte de RF05), persistência/salvamento automático/recuperação (parte de GP), e mensagens/estados separados para a interface (base de RNF01).

A interface WPF conecta as operações de montagem e o ciclo de persistência; a correspondência está em [release.md](release.md). `ImportacaoMidias` suporta cancelamento com resultados parciais e progresso por arquivo. Permanecem pendentes: validação completa com NVDA, reprodução, renderização/exportação, legendas, transições, IA e instalador. A leitura de metadados não comprova decodificação completa dos arquivos, sincronização audiovisual ou precisão VFR. RF07 permanece reservado.

Referências técnicas: [instalação do .NET](https://learn.microsoft.com/dotnet/core/install/windows), [transações SQLite](https://learn.microsoft.com/dotnet/standard/data/sqlite/transactions), [limitações async do provedor](https://learn.microsoft.com/dotnet/standard/data/sqlite/async).
