# Versão 0.1.0

## Escopo

A primeira versão entrega montagem não destrutiva e persistência de projetos. O core e a interface expõem o mesmo conjunto implementado de operações. A composição ainda não pode ser reproduzida nem exportada como vídeo.

| Capacidade | Core / aplicação | Interface |
|---|---|---|
| Biblioteca e importação | ImportacaoMidias, LeitorMetadadosFfprobe | Adicionar; seleção múltipla; cancelamento |
| Inserção e montagem | InserirClipe, InserirClipes, MoverClipe | Adicionar; inserir antes; mover antes/depois/início/fim |
| Divisão e remoção | DividirClipe, ExcluirIntervalo | Dividir no cursor; excluir seleção |
| Camadas e volume | AdicionarCamada, AtualizarCamada, RemoverCamada, DefinirVolume | Adicionar camada; editar intervalo; remover; ajustar volume |
| Histórico | SessaoEdicao | Desfazer/refazer e Ctrl+Z/Ctrl+Y fora de campos de texto |
| Navegação e seleção | Posicionar, Deslocar, AvancarQuadro | Controle temporal, Shift e consulta da seleção |
| Referências externas | ReligarMidia, AcoesEdicao.ReligarAsync | Localizar arquivo e validação de compatibilidade |
| Persistência | RepositorioProjetosSqlite, PersistenciaProjeto | Salvar; Salvar como; salvamento automático; recuperação; fechamento seguro |
| Catálogo e preferências | CatalogoProjetos, RepositorioConfiguracoes | Meus projetos; Configurações |

Prévia, exportação e geração por IA permanecem fora do escopo. `IReprodutorPrevia` e `IExportador` são pontos de integração sem implementação nesta versão. A navegação por timestamps VFR e a renderização de camadas também não são implementadas.

## Versionamento e artefatos

`Directory.Build.props` é a fonte da versão do aplicativo e dos três pacotes NuGet. A tag correspondente é `v0.1.0`. O esquema SQLite mantém sua versão independente (`user_version = 1`).

No Windows, com SDK .NET 10:

```powershell
./release.ps1 -ExpectedTag v0.1.0
```

O script prepara as ferramentas de mídia, compila, executa os testes, gera os pacotes NuGet e publica uma distribuição Windows x64 com runtime incluído. Os resultados ficam em `artifacts/releases/0.1.0`:

- `sonarFrame-0.1.0-win-x64.zip`: distribuição portátil; extraia todo o conteúdo antes de executar `SonarFrame.Desktop.exe`.
- `packages/`: pacotes Core, Application e Infrastructure.
- `SHA256SUMS.txt`: checksums do ZIP e dos pacotes.
- `CHANGELOG.md` e `release.md`: escopo da versão.

O script não cria tags, commits, releases remotas nem publica pacotes. Para preparar a tag, confirme que o commit contém o código, os arquivos de lock e o changelog correspondentes. `-ExpectedTag` rejeita uma versão incompatível.

## Verificação

A suíte inclui testes de domínio, persistência, importação com mídia real e ligação de controles WPF ao core. Os testes WPF usam uma thread STA sem exibir janelas. A CI prepara FFmpeg/FFprobe para executar a integração real.

Antes de uma distribuição estável, validar os fluxos com NVDA e teclado, codecs de arquivos de origem real e condições de distribuição das dependências. Licenças e origem do FFprobe estão em [media-tools.md](media-tools.md). A preparação de artefatos locais não define a licença do SonarFrame.
