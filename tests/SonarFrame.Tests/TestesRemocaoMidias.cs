using SonarFrame.Application;
using SonarFrame.Core;
using Xunit;

namespace SonarFrame.Tests;

public sealed class TestesRemocaoMidias
{
    [Fact]
    public void RemocaoMultiplaEhAtomicaEPermiteDesfazer()
    {
        var a = new Midia(Guid.NewGuid(), "A", "a.mp4", TipoMidia.Video, TempoMidia.Segundos(10));
        var b = a with { Id = Guid.NewGuid(), Nome = "B" };
        var sessao = new SessaoEdicao(Projeto.Criar("Remoção") with { Biblioteca = [a, b] });
        sessao.Executar(new RemoverMidias([a.Id, b.Id]));
        Assert.Empty(sessao.Atual.Biblioteca);
        sessao.Desfazer();
        Assert.Equal(2, sessao.Atual.Biblioteca.Length);
        sessao.Executar(new InserirClipe(a.Id, 0));
        Assert.Throws<InvalidOperationException>(() => sessao.Executar(new RemoverMidias([a.Id, b.Id])));
        Assert.Equal(2, sessao.Atual.Biblioteca.Length);
        Assert.Single(sessao.Atual.LinhaDoTempo);
    }
}
