using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Tests;

public sealed class TestesBibliotecaProjetos : IDisposable
{
    private readonly string diretorio = Path.Combine(AppContext.BaseDirectory, "catalog-tests", Guid.NewGuid().ToString("N"));
    public TestesBibliotecaProjetos() => Directory.CreateDirectory(diretorio);
    public void Dispose() => Directory.Delete(diretorio, true);
    [Fact] public async Task CatalogoEncontraProjetosValidosERecuperacaoOrfaSemDescartarArquivosInvalidos()
    {
        var repositorio = new RepositorioProjetosSqlite();
        var salvo = Path.Combine(diretorio, "saved.sonarframe"); var orfao = Path.Combine(diretorio, "orphan.sonarframe");
        await repositorio.SalvarAsync(salvo, Projeto.Criar("Salvo"));
        await repositorio.SalvarAsync(orfao + ".recovery", Projeto.Criar("Recuperável"));
        var invalido = Path.Combine(diretorio, "invalid.sonarframe"); await File.WriteAllTextAsync(invalido, "invalid");
        var catalogo = await new CatalogoProjetos(repositorio).LerAsync(diretorio);
        Assert.Equal(2, catalogo.Projetos.Count); Assert.Single(catalogo.Erros);
        Assert.True(catalogo.Projetos.Single(p => p.Caminho == orfao).TemRecuperacao);
        Assert.Equal("invalid", await File.ReadAllTextAsync(invalido));
        await repositorio.SalvarAsync(invalido + ".recovery", Projeto.Criar("Recuperação de principal corrompido"));
        catalogo = await new CatalogoProjetos(repositorio).LerAsync(diretorio);
        Assert.Equal(3, catalogo.Projetos.Count); Assert.Empty(catalogo.Erros);
        Assert.True(catalogo.Projetos.Single(p => p.Caminho == invalido).TemRecuperacao);
    }
    [Fact] public void AlteracaoDeConfiguracoesNaoMoveNemExcluiProjetos()
    {
        var arquivo = Path.Combine(diretorio, "original.sonarframe"); File.WriteAllText(arquivo, "original");
        var repositorio = new RepositorioConfiguracoes(Path.Combine(diretorio, "config", "settings.json"));
        var configuracoes = new ConfiguracoesDesktop(Path.Combine(diretorio, "another")); repositorio.Salvar(configuracoes);
        Assert.Equal(configuracoes, repositorio.Carregar()); Assert.Equal("original", File.ReadAllText(arquivo));
        Assert.Throws<ArgumentException>(() => repositorio.Salvar(new ConfiguracoesDesktop("relative")));
    }
    [Fact] public void CaminhosDeNovosProjetosSaoUnicosEPermanecemNoDiretorioConfigurado()
    {
        var primeiro = CatalogoProjetos.GerarCaminho(diretorio, "../áudio: projeto?");
        var segundo = CatalogoProjetos.GerarCaminho(diretorio, "../áudio: projeto?");
        Assert.NotEqual(primeiro, segundo); Assert.Equal(diretorio, Path.GetDirectoryName(primeiro));
        Assert.Equal(".sonarframe", Path.GetExtension(primeiro));
    }
}
