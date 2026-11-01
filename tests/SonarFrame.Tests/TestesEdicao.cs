using SonarFrame.Application;
using SonarFrame.Core;
using Xunit;

namespace SonarFrame.Tests;

public class TestesEdicao
{
    internal static TempoMidia Segundos(long valor) => TempoMidia.Segundos(valor);
    internal static Midia Video(long segundos = 10) => new(Guid.NewGuid(), "Vídeo", "original.mp4", TipoMidia.Video, Segundos(segundos), new(30000, 1001));
    internal static SessaoEdicao Sessao()
    {
        var s = new SessaoEdicao(Projeto.Criar("Teste"));
        var video = Video(); s.Executar(new RegistrarMidia(video)); s.Executar(new InserirClipe(video.Id, 0));
        return s;
    }
    [Fact] public void ImportacaoNaoInsereNaLinhaDoTempo()
    {
        var s = new SessaoEdicao(Projeto.Criar("Vazio"));
        s.Executar(new RegistrarMidia(Video())); s.Executar(new RegistrarMidia(Video()));
        Assert.Equal(2, s.Atual.Biblioteca.Length); Assert.Empty(s.Atual.LinhaDoTempo);
    }
    [Fact] public void DividirMoverExcluirDesfazerERefazerPreservamOrigem()
    {
        var s = Sessao(); var midia = s.Atual.Biblioteca[0];
        s.Executar(new DividirClipe(s.Atual.LinhaDoTempo[0].Id, Segundos(4)));
        Assert.Equal(Segundos(4), s.Atual.LinhaDoTempo[1].InicioNaOrigem);
        Assert.Equal(Segundos(10), s.Atual.Duracao);
        s.Executar(new MoverClipe(s.Atual.LinhaDoTempo[1].Id, 0));
        Assert.Equal(Segundos(4), s.Atual.LinhaDoTempo[0].InicioNaOrigem);
        var antes = s.Atual;
        s.Executar(new ExcluirIntervalo(Segundos(2), Segundos(8)));
        Assert.Equal(Segundos(4), s.Atual.Duracao);
        Assert.Equal(Segundos(2), s.Atual.Cursor);
        var depois = s.Atual; s.Desfazer(); Assert.Equal(antes, s.Atual);
        s.Refazer(); Assert.Equal(depois, s.Atual); Assert.Equal(midia, s.Atual.Biblioteca[0]);
    }
    [Fact] public void EdicaoInvalidaEhAtomicaEPreservaRefazer()
    {
        var s = Sessao(); s.Executar(new DividirClipe(s.Atual.LinhaDoTempo[0].Id, Segundos(4))); s.Desfazer();
        var antes = s.Atual;
        Assert.Throws<ArgumentException>(() => s.Executar(new DefinirVolume(s.Atual.LinhaDoTempo[0].Id, 201)));
        Assert.Equal(antes, s.Atual); Assert.True(s.PodeRefazer); Assert.True(s.Mensagens.Last().EhErro);
        s.Executar(new DefinirVolume(s.Atual.LinhaDoTempo[0].Id, 0)); Assert.False(s.PodeRefazer);
    }
    [Theory]
    [InlineData(0, 10, 0)] [InlineData(0, 3, 7)] [InlineData(7, 10, 7)] [InlineData(3, 7, 6)]
    public void ExclusaoDeIntervalosNaoDeixaLacunas(int inicio, int fim, int duracao)
    {
        var s = Sessao(); s.Executar(new ExcluirIntervalo(Segundos(inicio), Segundos(fim)));
        Assert.Equal(Segundos(duracao), s.Atual.Duracao); s.Atual.Validar();
    }
    [Fact] public void ExclusaoComFechamentoDeLacunaRecortaEDeslocaAudioIndependente()
    {
        var s = Sessao(); var audio = Video() with { Tipo = TipoMidia.Audio };
        s.Executar(new RegistrarMidia(audio)); s.Executar(new AdicionarCamada(audio.Id, Segundos(1), Segundos(0), Segundos(8)));
        s.Executar(new ExcluirIntervalo(Segundos(3), Segundos(6)));
        Assert.Equal(2, s.Atual.Camadas.Length);
        Assert.Equal(Segundos(2), s.Atual.Camadas[0].Clipe.Duracao);
        Assert.Equal(Segundos(3), s.Atual.Camadas[1].Inicio);
        Assert.Equal(Segundos(5), s.Atual.Camadas[1].Clipe.InicioNaOrigem);
        Assert.Equal(Segundos(3), s.Atual.Camadas[1].Clipe.Duracao);
    }
    [Fact] public void VolumeDoAudioNaoAlteraDuracaoDoVideo()
    {
        var s = Sessao(); s.Executar(new DefinirVolume(s.Atual.LinhaDoTempo[0].Id, 200));
        Assert.Equal(Segundos(10), s.Atual.Duracao);
    }
    [Fact] public void QuadrosRacionaisNaoAcumulamErrosDeArredondamento()
    {
        var tempo = TempoMidia.Zero;
        for (var i = 0; i < 30000; i++) tempo += new TempoMidia(1001, 30000);
        Assert.Equal(Segundos(1001), tempo); Assert.Equal(TempoMidia.Zero, default(TempoMidia));
        var s = Sessao(); s.AvancarQuadro(1); Assert.Equal(new TempoMidia(1001, 30000), s.Atual.Cursor);
        s.AvancarQuadro(-1); Assert.Equal(TempoMidia.Zero, s.Atual.Cursor);
    }
    [Fact] public void SelecaoMantemAncoraENaoIniciaReproducao()
    {
        var s = Sessao(); s.Posicionar(Segundos(4)); s.Posicionar(Segundos(2), true); s.Posicionar(Segundos(8), true);
        Assert.Equal(Segundos(4), s.Atual.Selecao!.Ancora); Assert.Equal(Segundos(8), s.Atual.Selecao.Fim);
        s.Posicionar(Segundos(4), true); Assert.Null(s.Atual.Selecao);
        var quantidade = s.Mensagens.Count; s.Posicionar(Segundos(5), reproducao: true); Assert.Equal(quantidade, s.Mensagens.Count);
    }
    [Fact] public void SelecaoDeItemNaoPosicionaNemEdita()
    {
        var s = Sessao(); s.Posicionar(Segundos(5)); s.SelecionarItem(s.Atual.LinhaDoTempo[0].Id);
        Assert.Equal(Segundos(5), s.Atual.Cursor); Assert.Single(s.Atual.LinhaDoTempo);
    }
    [Fact] public void FalhaNoObservadorNaoImpedeEdicaoConcluida()
    {
        var s = Sessao(); s.Alterado += _ => throw new Exception("Erro na interface");
        s.Executar(new DefinirVolume(s.Atual.LinhaDoTempo[0].Id, 40));
        Assert.Equal(40, s.Atual.LinhaDoTempo[0].Volume);
    }
    [Fact] public void TaxaVariavelExigeNavegadorDeTimestamps()
    {
        var s = new SessaoEdicao(Projeto.Criar("VFR")); var a = Video() with { TaxaQuadrosVariavel = true };
        s.Executar(new RegistrarMidia(a)); s.Executar(new InserirClipe(a.Id, 0));
        Assert.Throws<InvalidOperationException>(() => s.AvancarQuadro(1));
        s.AvancarQuadro(1, navegador: new Quadros()); Assert.Equal(new TempoMidia(41, 1000), s.Atual.Cursor);
    }
    private sealed class Quadros : INavegadorQuadros
    { public TempoMidia ProximoQuadro(Midia midia, TempoMidia posicaoNaOrigem, int direcao) => new(41, 1000); }
    [Fact] public void NavegacaoPorQuadrosAlinhaAposPosicionamentoPorSegundos()
    {
        var s = Sessao(); s.Posicionar(Segundos(1)); s.AvancarQuadro(-1);
        Assert.Equal(new TempoMidia(29 * 1001, 30000), s.Atual.Cursor);
        s.Posicionar(Segundos(1)); s.AvancarQuadro(1);
        Assert.Equal(new TempoMidia(30 * 1001, 30000), s.Atual.Cursor);
    }
    [Fact] public void NavegacaoPorQuadrosNaDivisaoUsaOrigemCorreta()
    {
        var s = Sessao(); s.Executar(new DividirClipe(s.Atual.LinhaDoTempo[0].Id, Segundos(4)));
        s.Posicionar(Segundos(4)); s.AvancarQuadro(-1); Assert.True(s.Atual.Cursor < Segundos(4));
        s.Posicionar(Segundos(4)); s.AvancarQuadro(1); Assert.True(s.Atual.Cursor > Segundos(4));
    }
    [Fact] public void ImagemExigeDuracaoExplicitaECamadaNaoPodeUltrapassarComposicao()
    {
        var s = Sessao(); var a = new Midia(Guid.NewGuid(), "Foto", "foto.png", TipoMidia.Imagem, null);
        s.Executar(new RegistrarMidia(a));
        Assert.Throws<ArgumentException>(() => s.Executar(new InserirClipe(a.Id, 1)));
        s.Executar(new InserirClipe(a.Id, 1, Segundos(3))); Assert.Equal(Segundos(13), s.Atual.Duracao);
        Assert.Throws<ArgumentException>(() => s.Executar(new AdicionarCamada(a.Id, Segundos(12), Segundos(0), Segundos(3))));
    }
    [Fact] public async Task ImportacaoEmLoteContinuaAposArquivoInvalido()
    {
        var s = new SessaoEdicao(Projeto.Criar("Importação"));
        var resultado = await ImportacaoMidias.ImportarAsync(s, new LeitorMetadados(), ["a.mp4", "bad.mp4", "b.mp4"]);
        Assert.Equal(2, resultado.MidiasImportadasIds.Count); Assert.Single(resultado.Falhas);
        Assert.Equal(2, s.Atual.Biblioteca.Length); Assert.Empty(s.Atual.LinhaDoTempo);
    }
    [Fact] public void ExportacaoExigeOrientacaoExplicita()
    {
        Assert.Throws<ArgumentException>(() => new OpcoesExportacao("out.mp4", null, ModoEnquadramento.Preencher).Validar());
        new OpcoesExportacao("out.mp4", Orientacao.Vertical, ModoEnquadramento.Letterbox).Validar();
    }
    private sealed class LeitorMetadados : ILeitorMetadadosMidia
    {
        public Task<Midia> LerAsync(string caminho, CancellationToken cancellationToken = default) =>
            caminho == "bad.mp4" ? throw new InvalidDataException("Mídia inválida.") : Task.FromResult(Video() with { Caminho = caminho });
    }
}
