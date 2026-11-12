using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;
using static SonarFrame.Tests.TestesPreparacaoComposicao;

namespace SonarFrame.Tests;

public sealed class TestesPrecisaoReproducao
{
    [MidiasReaisFact]
    public async Task JanelaComKeyframeDistantePreservaAdjacentes()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-keyframe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var arquivo = Path.Combine(pasta, "longo.mp4");
            await Ffmpeg("-f", "lavfi", "-i", "testsrc2=s=160x90:r=30:d=25", "-c:v", "libx264", "-g", "900", "-sc_threshold", "0", arquivo);
            var midia = new Midia(Guid.NewGuid(), "Vídeo", arquivo, TipoMidia.Video, TempoMidia.Segundos(25));
            var projeto = Projeto.Criar("Keyframe distante") with
            {
                Biblioteca = [midia], LinhaDoTempo = [new(Guid.NewGuid(), midia.Id, midia.Nome, TempoMidia.Zero, midia.Duracao!.Value)], Cursor = TempoMidia.Segundos(15)
            };
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!));
            Assert.Equal(new TempoMidia(451, 30), await preparador.ProximaPosicaoAsync(projeto, 1, default));
            Assert.Equal(new TempoMidia(449, 30), await preparador.ProximaPosicaoAsync(projeto, -1, default));
            Assert.NotNull(await preparador.ObterQuadroAsync(projeto, projeto.Cursor, default));
        }
        finally { Directory.Delete(pasta, true); }
    }

    private sealed class ProbeJanelas : IExecutorFfprobe
    {
        public int Janelas { get; private set; }
        public int Completas { get; private set; }
        public Task<string> InspecionarAsync(string caminhoCompleto, CancellationToken token) => throw new NotSupportedException();
        private static string Dados(string quadros) => "{\"streams\":[{\"time_base\":\"1/10\"}],\"format\":{\"start_time\":\"0\"},\"frames\":[" + quadros + "]}";
        public Task<string> InspecionarJanelaQuadrosAsync(string caminhoCompleto, TempoMidia inicio, TempoMidia fim, CancellationToken token)
        {
            Janelas++;
            return Task.FromResult(Dados(Janelas == 1 ? "{\"best_effort_timestamp\":0}" :
                "{\"best_effort_timestamp\":98},{\"best_effort_timestamp\":99},{\"best_effort_timestamp\":100},{\"best_effort_timestamp\":103}"));
        }
        public Task<string> InspecionarQuadrosAsync(string caminhoCompleto, CancellationToken token)
        {
            Completas++;
            return Task.FromResult(Dados("{\"best_effort_timestamp\":0},{\"best_effort_timestamp\":9990}"));
        }
    }

    [Fact]
    public async Task JanelaExpandeReutilizaInvalidaERecorreAoIndiceCompletoNoExtremo()
    {
        var arquivo = Path.GetTempFileName();
        try
        {
            var midia = new Midia(Guid.NewGuid(), "Vídeo", arquivo, TipoMidia.Video, TempoMidia.Segundos(1000));
            var projeto = Projeto.Criar("Janela") with
            {
                Biblioteca = [midia], LinhaDoTempo = [new(Guid.NewGuid(), midia.Id, midia.Nome, TempoMidia.Zero, midia.Duracao!.Value)], Cursor = TempoMidia.Segundos(10)
            };
            var probe = new ProbeJanelas();
            await using var preparador = new PreparadorComposicaoFfmpeg("inutilizado", probe);
            Assert.Equal(new TempoMidia(103, 10), await preparador.ProximaPosicaoAsync(projeto, 1, default));
            Assert.Equal(2, probe.Janelas);
            Assert.Equal(new TempoMidia(99, 10), await preparador.ProximaPosicaoAsync(projeto, -1, default));
            Assert.Equal(2, probe.Janelas);
            Assert.Equal(0, probe.Completas);
            File.SetLastWriteTimeUtc(arquivo, DateTime.UtcNow.AddMinutes(1));
            await preparador.ProximaPosicaoAsync(projeto, 1, default);
            Assert.Equal(3, probe.Janelas);
            Assert.Equal(TempoMidia.Segundos(999), await preparador.ProximaPosicaoAsync(projeto with { Cursor = projeto.Duracao }, -1, default));
            Assert.Equal(1, probe.Completas);
        }
        finally { File.Delete(arquivo); }
    }

    [Fact]
    public async Task CancelarIndiceEmAndamentoPermiteNovaTentativa()
    {
        var arquivo = Path.GetTempFileName();
        try
        {
            var m = new Midia(Guid.NewGuid(), "Vídeo", arquivo, TipoMidia.Video, TempoMidia.Segundos(1), null, true);
            var p = Projeto.Criar("Cancelar índice") with { Biblioteca = [m], LinhaDoTempo = [new(Guid.NewGuid(), m.Id, m.Nome, TempoMidia.Zero, TempoMidia.Segundos(1))] };
            var probe = new ProbePendente();
            await using var preparador = new PreparadorComposicaoFfmpeg("inutilizado", probe);
            using var cancelamento = new CancellationTokenSource();
            var leitura = preparador.ProximaPosicaoAsync(p, 1, cancelamento.Token);
            await probe.Iniciado.Task;
            cancelamento.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leitura);
            probe.Aguardar = false;
            Assert.Equal(new TempoMidia(1, 10), await preparador.ProximaPosicaoAsync(p, 1, default));
        }
        finally { File.Delete(arquivo); }
    }

    private sealed class ProbePendente : IExecutorFfprobe
    {
        public bool Aguardar { get; set; } = true;
        public TaskCompletionSource Iniciado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> InspecionarAsync(string caminhoCompleto, CancellationToken token) => throw new NotSupportedException();
        public async Task<string> InspecionarQuadrosAsync(string caminhoCompleto, CancellationToken token)
        {
            Iniciado.TrySetResult();
            if (Aguardar) await Task.Delay(Timeout.Infinite, token);
            return """{"streams":[{"time_base":"1/10"}],"format":{"start_time":"0"},"frames":[{"best_effort_timestamp":0},{"best_effort_timestamp":1},{"best_effort_timestamp":3}]}""";
        }
    }

    [MidiasReaisFact]
    public async Task QuadrosVfrSeguemTimestampsEExibemOrigemMesmoAntesDePreparar()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-precisao-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var video = Path.Combine(pasta, "quadros variáveis.mp4");
            await Ffmpeg("-f", "lavfi", "-i", "color=red:s=64x48:r=25:d=0.04", "-f", "lavfi", "-i", "color=lime:s=64x48:r=25:d=0.04", "-f", "lavfi", "-i", "color=blue:s=64x48:r=25:d=0.04",
                "-filter_complex", "[0:v][1:v][2:v]concat=n=3:v=1:a=0,setpts='if(eq(N,0),0,if(eq(N,1),0.04/TB,0.12/TB))'[v]", "-map", "[v]", "-fps_mode", "vfr", "-c:v", "libx264", "-bf", "0", "-video_track_timescale", "1000", video);
            var midia = new Midia(Guid.NewGuid(), "VFR", video, TipoMidia.Video, new(4, 25), new(25, 1), true);
            var clipe = new Clipe(Guid.NewGuid(), midia.Id, midia.Nome, TempoMidia.Zero, midia.Duracao!.Value);
            var projeto = Projeto.Criar("Precisão") with { Biblioteca = [midia], LinhaDoTempo = [clipe] };
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!));
            Assert.Equal(new TempoMidia(1, 25), await preparador.ProximaPosicaoAsync(projeto, 1, default));
            Assert.Equal(new TempoMidia(3, 25), await preparador.ProximaPosicaoAsync(projeto with { Cursor = new(1, 25) }, 1, default));
            Assert.Equal(new TempoMidia(1, 25), await preparador.ProximaPosicaoAsync(projeto with { Cursor = new(3, 25) }, -1, default));
            Assert.Equal(new TempoMidia(3, 25), await preparador.ProximaPosicaoAsync(projeto with { Cursor = projeto.Duracao }, -1, default));
            foreach (var (tempo, canal) in new[] { (TempoMidia.Zero, 0), (new TempoMidia(1, 25), 1), (new TempoMidia(11, 100), 1), (projeto.Duracao, 2) })
            {
                var quadro = await preparador.ObterQuadroAsync(projeto, tempo, default);
                Assert.NotNull(quadro); Assert.Equal(tempo, quadro.Posicao);
                await VerificarCorAsync(quadro.Png, canal, pasta);
            }
            var recorte = projeto with { LinhaDoTempo = [clipe with { InicioNaOrigem = new(1, 50), Duracao = new(7, 50) }] };
            var camada = projeto with { Camadas = [new(clipe with { Id = Guid.NewGuid(), InicioNaOrigem = new(3, 25), Duracao = new(1, 25) }, new(1, 25))] };
            await VerificarCorAsync((await preparador.ObterQuadroAsync(camada, new(1, 25), default))!.Png, 2, pasta);
            await VerificarCorAsync((await preparador.ObterQuadroAsync(camada, new(2, 25), default))!.Png, 1, pasta);
            Assert.Equal(new TempoMidia(1, 50), await preparador.ProximaPosicaoAsync(recorte, 1, default));
            await VerificarCorAsync((await preparador.ObterQuadroAsync(recorte, TempoMidia.Zero, default))!.Png, 0, pasta);
            var dois = projeto with { LinhaDoTempo = [clipe, clipe with { Id = Guid.NewGuid() }], Cursor = clipe.Duracao };
            Assert.Equal(new TempoMidia(3, 25), await preparador.ProximaPosicaoAsync(dois, -1, default));
            Assert.Equal(new TempoMidia(1, 5), await preparador.ProximaPosicaoAsync(dois, 1, default));
            await VerificarCorAsync((await preparador.ObterQuadroAsync(dois, dois.Cursor, default))!.Png, 0, pasta);
            using var cancelado = new CancellationTokenSource(); cancelado.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparador.ProximaPosicaoAsync(projeto, 1, cancelado.Token));
            await Ffmpeg("-f", "lavfi", "-i", "color=blue:s=64x48:r=25:d=0.16", "-c:v", "libx264", video);
            Assert.Equal(new TempoMidia(2, 25), await preparador.ProximaPosicaoAsync(projeto with { Cursor = new(1, 25) }, 1, default));
            await VerificarCorAsync((await preparador.ObterQuadroAsync(projeto, new(1, 25), default))!.Png, 2, pasta);
            File.Delete(video);
            await Assert.ThrowsAsync<FileNotFoundException>(() => preparador.ProximaPosicaoAsync(projeto, 1, default));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [MidiasReaisFact]
    public async Task CfrSessentaQuadrosERecortesCurtosNaoPerdemQuadroIntermediario()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-cfr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var video = Path.Combine(pasta, "60fps.mp4");
            await Ffmpeg("-f", "lavfi", "-i", "color=red:s=64x48:r=60:d=0.016", "-f", "lavfi", "-i", "color=lime:s=64x48:r=60:d=0.016", "-f", "lavfi", "-i", "color=blue:s=64x48:r=60:d=0.016",
                "-filter_complex", "[0:v][1:v][2:v]concat=n=3:v=1:a=0[v]", "-map", "[v]", "-r", "60", "-c:v", "libx264", "-bf", "0", video);
            var m = new Midia(Guid.NewGuid(), "60 fps", video, TipoMidia.Video, new(1, 10), new(60, 1));
            var p = Projeto.Criar("CFR") with { Biblioteca = [m], LinhaDoTempo = [new(Guid.NewGuid(), m.Id, m.Nome, TempoMidia.Zero, new(1, 20))] };
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!));
            Assert.Equal(new TempoMidia(1, 60), await preparador.ProximaPosicaoAsync(p, 1, default));
            await VerificarCorAsync((await preparador.ObterQuadroAsync(p, new(1, 60), default))!.Png, 1, pasta);
            var curto = p with { LinhaDoTempo = [p.LinhaDoTempo[0] with { InicioNaOrigem = new(1, 60), Duracao = new(1, 1000) }] };
            await VerificarCorAsync((await preparador.ObterQuadroAsync(curto, curto.Duracao, default))!.Png, 1, pasta);
            Assert.True(File.Exists((await preparador.PrepararAsync(curto, null, default)).Caminho));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [MidiasReaisFact]
    public async Task CortesFracionariosNaoAcumulamDerivaEIntervaloTemDuracaoPropria()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-sincronia-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var video = Path.Combine(pasta, "som e imagem.mp4"); var foto = Path.Combine(pasta, "fim.png");
            await Ffmpeg("-f", "lavfi", "-i", "color=red:s=64x48:r=30:d=1", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=1", "-c:v", "libx264", "-c:a", "aac", video);
            await Ffmpeg("-f", "lavfi", "-i", "color=lime:s=64x48", "-frames:v", "1", foto);
            var v = new Midia(Guid.NewGuid(), "Vídeo", video, TipoMidia.Video, TempoMidia.Segundos(1), new(30, 1));
            var f = new Midia(Guid.NewGuid(), "Fim", foto, TipoMidia.Imagem, null);
            var clipes = Enumerable.Range(0, 20).Select(_ => new Clipe(Guid.NewGuid(), v.Id, v.Nome, TempoMidia.Zero, new(1, 20))).ToList();
            clipes.Add(new(Guid.NewGuid(), f.Id, f.Nome, TempoMidia.Zero, new(1, 2)));
            var p = Projeto.Criar("Cortes curtos") with { Biblioteca = [v, f], LinhaDoTempo = [.. clipes] };
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!));
            var previa = await preparador.PrepararAsync(p, null, default);
            var pixels = Path.Combine(pasta, "video.rgb"); var audio = Path.Combine(pasta, "som.f32");
            await Ffmpeg("-i", previa.Caminho, "-vf", "scale=1:1", "-pix_fmt", "rgb24", "-an", "-f", "rawvideo", pixels);
            var rgb = await File.ReadAllBytesAsync(pixels);
            Assert.True(rgb[28 * 3] > 200 && rgb[32 * 3 + 1] > 200);
            await Ffmpeg("-i", previa.Caminho, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", audio);
            var pcm = await File.ReadAllBytesAsync(audio);
            double Nivel(int inicio) => Enumerable.Range(inicio, 2400).Average(i => Math.Abs(BitConverter.ToSingle(pcm, i * 4)));
            Assert.True(Nivel(43200) > 0.01); Assert.True(Nivel(52800) < 0.001);
            var intervalo = await preparador.PrepararIntervaloAsync(p, new(4, 5), new(6, 5), default);
            Assert.NotNull(intervalo); Assert.Equal(new TempoMidia(2, 5), intervalo.Duracao);
            Assert.NotEqual(previa.Caminho, intervalo.Caminho);
            await Ffmpeg("-i", intervalo.Caminho, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", audio);
            pcm = await File.ReadAllBytesAsync(audio);
            Assert.InRange(pcm.Length / 4, 19000, 20500);
            Assert.True(Nivel(2400) > 0.01); Assert.True(Nivel(14400) < 0.001);
        }
        finally { Directory.Delete(pasta, true); }
    }

    private static async Task VerificarCorAsync(byte[] png, int canal, string pasta)
    {
        var quadro = Path.Combine(pasta, "quadro.png"); var pixels = Path.Combine(pasta, "quadro.rgb");
        await File.WriteAllBytesAsync(quadro, png);
        await Ffmpeg("-i", quadro, "-vf", "scale=1:1", "-pix_fmt", "rgb24", "-f", "rawvideo", pixels);
        var rgb = await File.ReadAllBytesAsync(pixels);
        Assert.True(rgb[canal] > 200 && rgb.Where((_, i) => i != canal).All(c => c < 40), $"Cor esperada no canal {canal}, obtida {string.Join(',', rgb)}.");
    }
}
