using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Tests;

public sealed class TestesBibliotecaProjetos : IDisposable
{
    [Fact]
    public void DownloadsRespeitaLocalizacaoRedirecionadaSemCriarPasta()
    {
        var redirecionada = Path.Combine(diretorio, "outro-volume", "Recebidos");
        Assert.Equal(redirecionada, PastasUsuario.ResolverDownloads(() => redirecionada));
        Assert.False(Directory.Exists(redirecionada));
        Assert.Throws<IOException>(() => PastasUsuario.ResolverDownloads(() => "relative"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfiguracaoAntigaPreservaProjetosEResolveConflitoComDownloads(bool projetosEmDownloads)
    {
        var projetos = projetosEmDownloads ? ConfiguracoesDesktop.PastaDownloadsPadrao : diretorio;
        var arquivo = Path.Combine(diretorio, "settings-antigo.json");
        var original = System.Text.Json.JsonSerializer.Serialize(new { ProjectsDirectory = projetos });
        File.WriteAllText(arquivo, original);
        var repositorio = new RepositorioConfiguracoes(arquivo);
        var configuracoes = repositorio.Carregar();
        Assert.Equal(projetos, configuracoes.DiretorioProjetos);
        Assert.Equal(projetosEmDownloads ? Path.Combine(projetos, "sonarFrame-exportacoes") : ConfiguracoesDesktop.PastaDownloadsPadrao,
            configuracoes.DiretorioExportacoes);
        Assert.False(ConfiguracoesDesktop.MesmaPasta(projetos, configuracoes.DiretorioExportacoes));
        Assert.Equal(original, File.ReadAllText(arquivo));
        repositorio.Salvar(configuracoes);
        Assert.Equal(configuracoes, repositorio.Carregar());
        using var documento = System.Text.Json.JsonDocument.Parse(File.ReadAllText(arquivo));
        Assert.Equal(projetos, documento.RootElement.GetProperty("ProjectsDirectory").GetString());
        Assert.Equal(configuracoes.DiretorioExportacoes, documento.RootElement.GetProperty("ExportsDirectory").GetString());
    }

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
        var pastaProjetos = Path.Combine(diretorio, "another");
        var pastaExportacoes = Path.Combine(diretorio, "exports");
        var configuracoes = new ConfiguracoesDesktop(pastaProjetos, pastaExportacoes); repositorio.Salvar(configuracoes);
        Assert.Equal(configuracoes, repositorio.Carregar()); Assert.Equal("original", File.ReadAllText(arquivo));
        Assert.Equal(ConfiguracoesDesktop.PastaDownloadsPadrao, ConfiguracoesDesktop.Padrao.DiretorioExportacoes);
        Assert.False(ConfiguracoesDesktop.MesmaPasta(ConfiguracoesDesktop.Padrao.DiretorioProjetos, ConfiguracoesDesktop.Padrao.DiretorioExportacoes));
        Assert.Throws<ArgumentException>(() => repositorio.Salvar(new ConfiguracoesDesktop("relative")));
        Assert.Throws<ArgumentException>(() => repositorio.Salvar(new ConfiguracoesDesktop(pastaProjetos, "relative")));
        Assert.Throws<ArgumentException>(() => repositorio.Salvar(new ConfiguracoesDesktop(pastaProjetos, pastaProjetos)));
    }
    [Fact] public void CaminhosDeNovosProjetosSaoUnicosEPermanecemNoDiretorioConfigurado()
    {
        var primeiro = CatalogoProjetos.GerarCaminho(diretorio, "../áudio: projeto?");
        var segundo = CatalogoProjetos.GerarCaminho(diretorio, "../áudio: projeto?");
        Assert.NotEqual(primeiro, segundo); Assert.Equal(diretorio, Path.GetDirectoryName(primeiro));
        Assert.Equal(".sonarframe", Path.GetExtension(primeiro));
    }
}
