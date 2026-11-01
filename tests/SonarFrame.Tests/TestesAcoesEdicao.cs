using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;
using static SonarFrame.Tests.TestesEdicao;

namespace SonarFrame.Tests;

public sealed class TestesAcoesEdicao
{
    [Fact] public void DivisaoNoCursorEExclusaoDeIntervaloPodemSerDesfeitasEntreCamadas()
    {
        var sessao = Sessao(); var acoes = new AcoesEdicao(sessao);
        sessao.Executar(new AdicionarCamada(sessao.Atual.Biblioteca[0].Id, Segundos(1), Segundos(0), Segundos(8)));
        sessao.Posicionar(Segundos(4)); acoes.DividirNoCursor();
        Assert.Equal(2, sessao.Atual.LinhaDoTempo.Length); Assert.Equal(3, acoes.Conteudos().Count);
        sessao.Posicionar(Segundos(2)); sessao.Posicionar(Segundos(6), true); var antes = sessao.Atual;
        acoes.ExcluirSelecao(); Assert.Equal(Segundos(6), sessao.Atual.Duracao);
        Assert.Null(sessao.Atual.Selecao); Assert.Equal(2, sessao.Atual.Camadas.Length);
        sessao.Desfazer(); Assert.Equal(antes, sessao.Atual);
    }
    [Fact] public void CursorInvalidoESelecaoVaziaNaoAlteramProjeto()
    {
        var sessao = Sessao(); var antes = sessao.Atual; var acoes = new AcoesEdicao(sessao);
        Assert.Throws<InvalidOperationException>(acoes.DividirNoCursor);
        Assert.Throws<InvalidOperationException>(acoes.ExcluirSelecao);
        Assert.Equal(antes, sessao.Atual);
    }
    [Fact] public void EdicaoVolumeERemocaoDeCamadaPreservamSequenciaPrincipal()
    {
        var sessao = Sessao(); var acoes = new AcoesEdicao(sessao);
        sessao.Executar(new AdicionarCamada(sessao.Atual.Biblioteca[0].Id, Segundos(1), Segundos(0), Segundos(5)));
        var id = sessao.Atual.Camadas[0].Clipe.Id;
        Assert.True(acoes.Selecionado().EhCamada);
        acoes.DefinirVolumeSelecionado(40);
        sessao.Executar(new AtualizarCamada(id, Segundos(2), Segundos(1), Segundos(4)));
        Assert.Equal(40, acoes.Selecionado().Volume); Assert.Equal(Segundos(2), acoes.Selecionado().Inicio);
        Assert.Equal(Segundos(10), sessao.Atual.Duracao);
        var antes = sessao.Atual;
        Assert.Throws<ArgumentException>(() => sessao.Executar(new AtualizarCamada(id, Segundos(9), Segundos(0), Segundos(4))));
        Assert.Equal(antes, sessao.Atual);
        sessao.Executar(new RemoverCamada(id)); Assert.Empty(sessao.Atual.Camadas);
        Assert.Null(sessao.Atual.ItemSelecionadoId); Assert.Equal(Segundos(10), sessao.Atual.Duracao);
        sessao.Desfazer(); Assert.Equal(antes, sessao.Atual);
    }
    [Fact] public void InsercaoEmLoteNaPosicaoPreservaOrdem()
    {
        var sessao = Sessao(); var midia = Video(); sessao.Executar(new RegistrarMidia(midia));
        var original = sessao.Atual.LinhaDoTempo[0];
        sessao.Executar(new InserirClipes([midia.Id, midia.Id], Indice: 0));
        Assert.Equal(original, sessao.Atual.LinhaDoTempo[2]);
        sessao.Desfazer(); Assert.Single(sessao.Atual.LinhaDoTempo);
    }
    [Fact] public async Task ReligacaoRejeitaArquivoIncompativelEPreservaEdicoes()
    {
        var sessao = Sessao(); var acoes = new AcoesEdicao(sessao); var original = sessao.Atual.Biblioteca[0];
        var antes = sessao.Atual;
        await Assert.ThrowsAsync<InvalidOperationException>(() => acoes.ReligarAsync(original.Id, "short.mp4", new LeitorMetadados(original with { Duracao = Segundos(2) })));
        Assert.Equal(antes, sessao.Atual);
        await acoes.ReligarAsync(original.Id, "moved.mp4", new LeitorMetadados(original with { Caminho = "moved.mp4" }));
        Assert.Equal("moved.mp4", sessao.Atual.Biblioteca[0].Caminho);
        Assert.Equal(antes.LinhaDoTempo, sessao.Atual.LinhaDoTempo);
    }
    private sealed class LeitorMetadados(Midia midia) : ILeitorMetadadosMidia
    { public Task<Midia> LerAsync(string caminho, CancellationToken cancellationToken = default) => Task.FromResult(midia); }
}
