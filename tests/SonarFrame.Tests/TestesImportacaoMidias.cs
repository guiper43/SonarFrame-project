using System.Collections.Immutable;
using System.Text.Json;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Tests;

public sealed class TestesImportacaoMidias : IDisposable
{
    private readonly string pasta = Path.Combine(AppContext.BaseDirectory, "probe-tests", Guid.NewGuid().ToString("N"));
    public TestesImportacaoMidias() => Directory.CreateDirectory(pasta);
    public void Dispose() => Directory.Delete(pasta, true);
    private string ArquivoChamado(string nome) { var caminho = Path.Combine(pasta, nome); File.WriteAllText(caminho, "dados de teste"); return caminho; }
    private sealed class ExecutorSimulado(string json) : IExecutorFfprobe
    { public Task<string> InspecionarAsync(string caminhoCompleto, CancellationToken token) => Task.FromResult(json); }

    [Fact] public async Task DuracaoExataETaxaDeQuadrosDoFluxoPrevalecemSobreContainer()
    {
        var leitorMetadados = new LeitorMetadadosFfprobe(new ExecutorSimulado("""
        {"streams":[{"codec_type":"video","codec_name":"h264","duration_ts":30030,"time_base":"1/30000","avg_frame_rate":"30000/1001","r_frame_rate":"30000/1001"}],"format":{"duration":"20.5"}}
        """));
        var midia = await leitorMetadados.LerAsync(ArquivoChamado("vídeo com espaços.mp4"));
        Assert.Equal(new TempoMidia(1001, 1000), midia.Duracao);
        Assert.Equal(new TaxaQuadros(30000, 1001), midia.TaxaQuadros);
        Assert.False(midia.TaxaQuadrosVariavel);
        Assert.True(Path.IsPathFullyQualified(midia.Caminho));
    }
    [Fact] public async Task CapaDoAudioNaoSeTornaVideo()
    {
        var leitorMetadados = new LeitorMetadadosFfprobe(new ExecutorSimulado("""
        {"streams":[{"codec_type":"video","codec_name":"mjpeg","disposition":{"attached_pic":1}},{"codec_type":"audio","codec_name":"opus"}],"format":{"duration":"1.234567"}}
        """));
        var midia = await leitorMetadados.LerAsync(ArquivoChamado("mensagem.ogg"));
        Assert.Equal(TipoMidia.Audio, midia.Tipo); Assert.Null(midia.TaxaQuadros);
        Assert.Equal(new TempoMidia(1234567, 1000000), midia.Duracao);
    }
    [Fact] public async Task ImagemNaoTemDuracaoIntrinsecaNaLinhaDoTempo()
    {
        var leitorMetadados = new LeitorMetadadosFfprobe(new ExecutorSimulado("""{"streams":[{"codec_type":"video","codec_name":"png"}]}"""));
        var midia = await leitorMetadados.LerAsync(ArquivoChamado("foto.PNG"));
        Assert.Equal(TipoMidia.Imagem, midia.Tipo); Assert.Null(midia.Duracao);
    }
    [Theory]
    [InlineData("{}")] [InlineData("JSON inválido")]
    [InlineData("{\"streams\":[{\"codec_type\":\"video\",\"codec_name\":\"unknown\"}]}")]
    [InlineData("{\"streams\":[{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"duration\":\"0\"}]}")]
    public async Task MetadadosInvalidosSaoRejeitados(string json)
    {
        var leitorMetadados = new LeitorMetadadosFfprobe(new ExecutorSimulado(json));
        await Assert.ThrowsAsync<InvalidDataException>(() => leitorMetadados.LerAsync(ArquivoChamado("invalid.mp4")));
    }
    [Fact] public async Task ExtensaoIncorretaEArquivoAusenteSaoRejeitados()
    {
        var leitorMetadados = new LeitorMetadadosFfprobe(new ExecutorSimulado("{}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => leitorMetadados.LerAsync(ArquivoChamado("file.exe")));
        await Assert.ThrowsAsync<FileNotFoundException>(() => leitorMetadados.LerAsync(Path.Combine(pasta, "missing.mov")));
    }
    [Fact] public async Task TaxasDiferentesIndicamPossivelTaxaDeQuadrosVariavel()
    {
        var leitorMetadados = new LeitorMetadadosFfprobe(new ExecutorSimulado("""{"streams":[{"codec_type":"video","codec_name":"hevc","duration":"2","avg_frame_rate":"25/1","r_frame_rate":"30/1"}]}"""));
        Assert.True((await leitorMetadados.LerAsync(ArquivoChamado("iphone.mov"))).TaxaQuadrosVariavel);
    }
    [Fact] public async Task ExecutavelAusenteTemErroUtil()
    {
        var executor = new ExecutorFfprobe(Path.Combine(pasta, "does-not-exist.exe"));
        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.InspecionarAsync(ArquivoChamado("video.mp4"), default));
        Assert.Contains("ffprobe", erro.Message);
    }
    [Fact] public async Task CancelamentoPreservaItensImportadosENaoInsereNaLinhaDoTempo()
    {
        var sessao = new SessaoEdicao(Projeto.Criar("Lote"));
        using var cancelamento = new CancellationTokenSource();
        var resultado = await ImportacaoMidias.ImportarAsync(sessao, new LeitorCancelavel(cancelamento), ["first", "second", "third"], cancelamento.Token);
        Assert.True(resultado.FoiCancelado); Assert.Single(resultado.MidiasImportadasIds);
        Assert.Single(sessao.Atual.Biblioteca); Assert.Empty(sessao.Atual.LinhaDoTempo);
    }
    private sealed class LeitorCancelavel(CancellationTokenSource cancelamento) : ILeitorMetadadosMidia
    {
        public Task<Midia> LerAsync(string caminho, CancellationToken token = default)
        {
            if (caminho == "second") { cancelamento.Cancel(); token.ThrowIfCancellationRequested(); }
            return Task.FromResult(TestesEdicao.Video());
        }
    }
    [Fact] public void InsercaoEmLoteEhAtomicaEDesfazerRestauraSomenteBiblioteca()
    {
        var sessao = new SessaoEdicao(Projeto.Criar("Lote"));
        var video = TestesEdicao.Video();
        var foto = new Midia(Guid.NewGuid(), "Foto", "foto.png", TipoMidia.Imagem, null);
        sessao.Executar(new RegistrarMidia(video)); sessao.Executar(new RegistrarMidia(foto));
        var antes = sessao.Atual;
        Assert.Throws<ArgumentException>(() => sessao.Executar(new InserirClipes([video.Id, foto.Id])));
        Assert.Equal(antes, sessao.Atual);
        sessao.Executar(new InserirClipes([video.Id, foto.Id], TempoMidia.Segundos(5)));
        Assert.Equal(TempoMidia.Segundos(15), sessao.Atual.Duracao);
        sessao.Desfazer(); Assert.Equal(antes, sessao.Atual);
    }
}
