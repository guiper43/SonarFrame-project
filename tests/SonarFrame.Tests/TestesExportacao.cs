using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Tests;

public sealed class TestesExportacao
{
    [Theory]
    [InlineData(Orientacao.Horizontal, 1080, 1920, 1080)]
    [InlineData(Orientacao.Vertical, 1080, 1080, 1920)]
    [InlineData(Orientacao.Horizontal, 720, 1280, 720)]
    [InlineData(Orientacao.Vertical, 720, 720, 1280)]
    public void OpcoesCalculamDimensoesCorretasPorOrientacaoEResolucao(Orientacao orientacao, int altura, int larguraEsperada, int alturaEsperada)
    {
        var opcoes = new OpcoesExportacao("video.mp4", orientacao, ModoEnquadramento.Letterbox, altura);
        Assert.Equal((larguraEsperada, alturaEsperada), opcoes.Dimensoes());
    }

    [Theory]
    [InlineData("", Orientacao.Horizontal, 1080)]
    [InlineData("video.mov", Orientacao.Horizontal, 1080)]
    [InlineData("video", Orientacao.Vertical, 720)]
    [InlineData(".mp4", Orientacao.Horizontal, 1080)]
    [InlineData("   .mp4", Orientacao.Horizontal, 1080)]
    [InlineData("CON.mp4", Orientacao.Horizontal, 1080)]
    [InlineData("nul.mp4", Orientacao.Vertical, 720)]
    [InlineData("com1.mp4", Orientacao.Horizontal, 1080)]
    [InlineData("lpt9.mp4", Orientacao.Vertical, 1080)]
    [InlineData("video.mp4", null, 1080)]
    [InlineData("video.mp4", Orientacao.Horizontal, 480)]
    [InlineData("video.mp4", Orientacao.Vertical, 2160)]
    public void OpcoesRejeitamParametrosInvalidos(string destino, Orientacao? orientacao, int altura)
    {
        var opcoes = new OpcoesExportacao(destino, orientacao, ModoEnquadramento.Preencher, altura);
        Assert.Throws<ArgumentException>(opcoes.Validar);
    }

    [Theory]
    [InlineData("video..mp4")]
    [InlineData("v1.2.mp4")]
    public void OpcoesAceitamPontosNoNomeBaseDoArquivo(string destino)
    {
        var opcoes = new OpcoesExportacao(destino, Orientacao.Horizontal, ModoEnquadramento.Letterbox, 1080);
        opcoes.Validar();
        Assert.True(OpcoesExportacao.NomeArquivoValido(destino));
    }

    [Fact]
    public async Task LeituraDeProgressoFiltraTelemetriaDoFfmpegEPreservaErrosComIgual()
    {
        var metodo = typeof(PreparadorComposicaoFfmpeg).GetMethod(
            "LerProgressoExportacaoAsync",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var saida = string.Join('\n',
            "frame=12",
            "fps=30.00",
            "stream_0_0_q=28.0",
            "bitrate= 512.0kbits/s",
            "total_size=48120",
            "out_time_us=1500000",
            "out_time_ms=1500000",
            "out_time=00:00:01.500000",
            "dup_frames=0",
            "drop_frames=0",
            "speed=1.25x",
            "[Parsed_scale_0 @ 000] Invalid size 'w=0:h=0'",
            "Error initializing filter 'scale' with args 'w=1920:h=1080'",
            "progress=end");
        var posicoes = new List<TempoMidia>();
        using var leitor = new StringReader(saida);
        var tarefa = (Task<string>)metodo.Invoke(null, [leitor, (Action<TempoMidia>)posicoes.Add])!;
        var erros = await tarefa;

        Assert.Equal([new TempoMidia(3, 2)], posicoes);
        Assert.Contains("Invalid size 'w=0:h=0'", erros);
        Assert.Contains("args 'w=1920:h=1080'", erros);
        Assert.DoesNotContain("frame=12", erros);
        Assert.DoesNotContain("progress=end", erros);
    }

    [MidiasReaisFact]
    public async Task ExportacaoGeraMp4ComOrientacaoEnquadramentoCortesCamadasEProgresso()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var video = Path.Combine(pasta, "origem.mp4");
            var foto = Path.Combine(pasta, "sobre.png");
            var trilha = Path.Combine(pasta, "trilha.wav");
            await Ffmpeg("-f", "lavfi", "-i", "color=red:s=320x180:r=30:d=3",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=3",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", video);
            await Ffmpeg("-f", "lavfi", "-i", "color=lime:s=80x80", "-frames:v", "1", foto);
            await Ffmpeg("-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=2", trilha);
            var hashVideo = SHA256.HashData(await File.ReadAllBytesAsync(video));

            var midiaVideo = new Midia(Guid.NewGuid(), "Origem", video, TipoMidia.Video, TempoMidia.Segundos(3), new(30, 1));
            var midiaFoto = new Midia(Guid.NewGuid(), "Sobre", foto, TipoMidia.Imagem, null);
            var midiaAudio = new Midia(Guid.NewGuid(), "Trilha", trilha, TipoMidia.Audio, TempoMidia.Segundos(2));
            var projeto = Projeto.Criar("Exportação") with
            {
                Biblioteca = [midiaVideo, midiaFoto, midiaAudio],
                LinhaDoTempo = [new(Guid.NewGuid(), midiaVideo.Id, midiaVideo.Nome, TempoMidia.Segundos(1), TempoMidia.Segundos(2), 100)],
                Camadas =
                [
                    new(new(Guid.NewGuid(), midiaFoto.Id, midiaFoto.Nome, TempoMidia.Zero, TempoMidia.Segundos(1)), TempoMidia.Zero),
                    new(new(Guid.NewGuid(), midiaAudio.Id, midiaAudio.Nome, TempoMidia.Zero, TempoMidia.Segundos(2), 150), TempoMidia.Zero)
                ]
            };

            var probe = new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!);
            await using var exportador = new ExportadorFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, probe);

            var progressos = new List<ProgressoOperacao>();
            var saidaVerticalBordas = Path.Combine(pasta, "vertical-bordas.mp4");
            await exportador.ExportarAsync(
                projeto,
                new OpcoesExportacao(saidaVerticalBordas, Orientacao.Vertical, ModoEnquadramento.Letterbox, 720),
                new ProgressoSincrono(progressos.Add));

            Assert.True(File.Exists(saidaVerticalBordas));
            Assert.Contains(progressos, p => p.Porcentagem == 0);
            Assert.Equal(100, progressos[^1].Porcentagem);
            await VerificarMp4Async(probe, saidaVerticalBordas, 720, 1280, 2.0m);

            var quadroTopo = Path.Combine(pasta, "topo.rgb");
            await Ffmpeg("-ss", "1.5", "-i", saidaVerticalBordas, "-frames:v", "1", "-vf", "crop=2:2:360:20", "-pix_fmt", "rgb24", "-f", "rawvideo", quadroTopo);
            var bytesTopo = await File.ReadAllBytesAsync(quadroTopo);
            Assert.True(bytesTopo[0] < 25 && bytesTopo[1] < 25 && bytesTopo[2] < 25);

            var saidaVerticalPreencher = Path.Combine(pasta, "vertical-preencher.mp4");
            await exportador.ExportarAsync(
                projeto with { Camadas = [] },
                new OpcoesExportacao(saidaVerticalPreencher, Orientacao.Vertical, ModoEnquadramento.Preencher, 1080),
                new ProgressoSincrono(_ => { }));
            await VerificarMp4Async(probe, saidaVerticalPreencher, 1080, 1920, 2.0m);

            var quadroTopoPreenchido = Path.Combine(pasta, "topo-preenchido.rgb");
            await Ffmpeg("-ss", "0.5", "-i", saidaVerticalPreencher, "-frames:v", "1", "-vf", "crop=2:2:540:20", "-pix_fmt", "rgb24", "-f", "rawvideo", quadroTopoPreenchido);
            var bytesPreenchido = await File.ReadAllBytesAsync(quadroTopoPreenchido);
            Assert.True(bytesPreenchido[0] > 150 && bytesPreenchido[1] < 40);

            foreach (var altura in new[] { 720, 1080 })
            {
                var horizontal = Path.Combine(pasta, $"horizontal-{altura}.mp4");
                await exportador.ExportarAsync(projeto, new(horizontal, Orientacao.Horizontal, ModoEnquadramento.Letterbox, altura), new ProgressoSincrono(_ => { }));
                await VerificarMp4Async(probe, horizontal, altura * 16 / 9, altura, 2.0m);
            }

            Assert.Equal(hashVideo, SHA256.HashData(await File.ReadAllBytesAsync(video)));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [MidiasReaisFact]
    public async Task ExportacaoProtegeOriginaisExigeSubstituicaoELimpaParciaisNoCancelamento()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-export-seg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var foto = Path.Combine(pasta, "foto.png");
            await Ffmpeg("-f", "lavfi", "-i", "color=blue:s=160x90", "-frames:v", "1", foto);
            var midia = new Midia(Guid.NewGuid(), "Foto", foto, TipoMidia.Imagem, null);
            var vazio = Projeto.Criar("Vazio") with { Biblioteca = [midia] };
            var projeto = vazio with
            {
                LinhaDoTempo = [new(Guid.NewGuid(), midia.Id, midia.Nome, TempoMidia.Zero, TempoMidia.Segundos(12))]
            };

            var probe = new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!);
            await using var exportador = new ExportadorFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, probe);

            var destino = Path.Combine(pasta, "saida.mp4");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                exportador.ExportarAsync(vazio, new OpcoesExportacao(destino, Orientacao.Horizontal, ModoEnquadramento.Letterbox), new ProgressoSincrono(_ => { })));

            var pastaComProjeto = Path.Combine(pasta, "pasta-projeto");
            Directory.CreateDirectory(pastaComProjeto);
            await File.WriteAllTextAsync(Path.Combine(pastaComProjeto, "projeto.sonarframe"), "sqlite");
            await Assert.ThrowsAsync<IOException>(() =>
                exportador.ExportarAsync(projeto, new OpcoesExportacao(Path.Combine(pastaComProjeto, "saida.mp4"), Orientacao.Horizontal, ModoEnquadramento.Letterbox), new ProgressoSincrono(_ => { })));

            var midiaMp4 = Path.Combine(pasta, "origem.mp4");
            await File.WriteAllTextAsync(midiaMp4, "origem");
            var comMp4 = projeto with { Biblioteca = [midia, new(Guid.NewGuid(), "Vídeo", midiaMp4, TipoMidia.Video, TempoMidia.Segundos(2))] };
            await Assert.ThrowsAsync<IOException>(() =>
                exportador.ExportarAsync(comMp4, new OpcoesExportacao(midiaMp4, Orientacao.Horizontal, ModoEnquadramento.Letterbox, SubstituirExistente: true), new ProgressoSincrono(_ => { })));
            Assert.Equal("origem", await File.ReadAllTextAsync(midiaMp4));

            await File.WriteAllTextAsync(destino, "existente");
            await Assert.ThrowsAsync<IOException>(() =>
                exportador.ExportarAsync(projeto, new OpcoesExportacao(destino, Orientacao.Horizontal, ModoEnquadramento.Letterbox, SubstituirExistente: false), new ProgressoSincrono(_ => { })));
            Assert.Equal("existente", await File.ReadAllTextAsync(destino));

            using var cancelamento = new CancellationTokenSource();
            var tarefa = exportador.ExportarAsync(
                projeto,
                new OpcoesExportacao(destino, Orientacao.Horizontal, ModoEnquadramento.Letterbox, 720, SubstituirExistente: true),
                new ProgressoSincrono(p => { if ((p.Porcentagem ?? 0) > 0) cancelamento.Cancel(); }),
                cancelamento.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tarefa);
            Assert.Equal("existente", await File.ReadAllTextAsync(destino));
            Assert.Empty(Directory.GetFiles(pasta, "*.tmp"));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [MidiasReaisFact]
    public async Task ExportacaoDiretaPreservaCortesVolumesECamadasSemAcumularBlocos()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-export-direta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var video = Path.Combine(pasta, "origem.mp4");
            var foto = Path.Combine(pasta, "camada.png");
            await Ffmpeg("-f", "lavfi", "-i", "color=red:s=160x90:r=30:d=4[r];color=green:s=160x90:r=30:d=4[g];color=blue:s=160x90:r=30:d=4[b];[r][g][b]concat=n=3:v=1:a=0",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=12", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", video);
            await Ffmpeg("-f", "lavfi", "-i", "color=lime:s=80x80", "-frames:v", "1", foto);
            var midia = new Midia(Guid.NewGuid(), "Cenas", video, TipoMidia.Video, TempoMidia.Segundos(12), new(30, 1));
            var imagem = new Midia(Guid.NewGuid(), "Camada", foto, TipoMidia.Imagem, null);
            var projeto = Projeto.Criar("Cortes e volumes") with
            {
                Biblioteca = [midia, imagem],
                LinhaDoTempo =
                [
                    new(Guid.NewGuid(), midia.Id, "Azul", TempoMidia.Segundos(8), TempoMidia.Segundos(4), 0),
                    new(Guid.NewGuid(), midia.Id, "Vermelho", TempoMidia.Zero, TempoMidia.Segundos(4), 50),
                    new(Guid.NewGuid(), midia.Id, "Verde", TempoMidia.Segundos(4), TempoMidia.Segundos(4), 100)
                ],
                Camadas = [new(new(Guid.NewGuid(), imagem.Id, imagem.Nome, TempoMidia.Zero, TempoMidia.Segundos(1)), new TempoMidia(9, 2))]
            };
            var probe = new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!);
            await using var exportador = new ExportadorFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, probe);
            var campos = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var motor = typeof(ExportadorFfmpeg).GetField("motor", campos)!.GetValue(exportador)!;
            var cache = (string)typeof(PreparadorComposicaoFfmpeg).GetField("pasta", campos)!.GetValue(motor)!;
            var picoCache = 0L;
            var intermediarios = new HashSet<string>();
            var destino = Path.Combine(pasta, "final.mp4");
            await exportador.ExportarAsync(projeto, new(destino, Orientacao.Horizontal, ModoEnquadramento.Letterbox, 720), new ProgressoSincrono(_ =>
            {
                if (!Directory.Exists(cache)) return;
                var arquivos = Directory.GetFiles(cache);
                picoCache = Math.Max(picoCache, arquivos.Sum(a => new FileInfo(a).Length));
                foreach (var arquivo in arquivos.Where(a => Path.GetExtension(a) is ".nut" or ".mov" or ".mp4")) intermediarios.Add(arquivo);
            }));
            Assert.Empty(intermediarios);
            Assert.InRange(picoCache, 1, 1024 * 1024);
            await VerificarMp4Async(probe, destino, 1280, 720, 12m);
            foreach (var (segundo, canal) in new[] { (2, 2), (5, 1), (6, 0), (10, 1) })
            {
                var quadro = Path.Combine(pasta, $"quadro-{segundo}.rgb");
                await Ffmpeg("-ss", segundo.ToString(), "-i", destino, "-frames:v", "1", "-vf", "crop=2:2:640:360", "-pix_fmt", "rgb24", "-f", "rawvideo", quadro);
                var rgb = await File.ReadAllBytesAsync(quadro);
                Assert.True(rgb[canal] > 90);
                Assert.All(Enumerable.Range(0, 3).Where(c => c != canal), c => Assert.True(rgb[c] < 40));
            }
            var niveis = new List<double>();
            foreach (var segundo in new[] { 2, 6, 10 })
            {
                var audio = Path.Combine(pasta, $"audio-{segundo}.f32");
                await Ffmpeg("-ss", segundo.ToString(), "-i", destino, "-t", "0.5", "-vn", "-ac", "1", "-f", "f32le", audio);
                var dados = await File.ReadAllBytesAsync(audio);
                var amostras = Enumerable.Range(0, dados.Length / 4).Select(i => (double)BitConverter.ToSingle(dados, i * 4)).ToArray();
                niveis.Add(Math.Sqrt(amostras.Average(v => v * v)));
            }
            Assert.True(niveis[0] < 0.0001);
            Assert.InRange(niveis[2] / niveis[1], 1.8, 2.2);
            Assert.DoesNotContain(Directory.GetFiles(cache), a => Path.GetFileName(a) != "em-uso");
            Assert.Empty(Directory.GetFiles(pasta, "*.tmp"));
        }
        finally { Directory.Delete(pasta, true); }
    }

    private static async Task VerificarMp4Async(ExecutorFfprobe probe, string caminho, int largura, int altura, decimal duracaoEsperada)
    {
        using var json = JsonDocument.Parse(await probe.InspecionarAsync(caminho, CancellationToken.None));
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.Single(s => s.GetProperty("codec_type").GetString() == "video");
        var audio = streams.Single(s => s.GetProperty("codec_type").GetString() == "audio");
        Assert.Equal("h264", video.GetProperty("codec_name").GetString());
        Assert.Equal("aac", audio.GetProperty("codec_name").GetString());
        Assert.Equal(largura, video.GetProperty("width").GetInt32());
        Assert.Equal(altura, video.GetProperty("height").GetInt32());
        var duracao = decimal.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(Math.Abs(duracao - duracaoEsperada), 0m, 0.08m);
    }

    private static async Task Ffmpeg(params string[] argumentos)
    {
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var inicio = new ProcessStartInfo(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        inicio.ArgumentList.Add("-v"); inicio.ArgumentList.Add("error"); inicio.ArgumentList.Add("-y");
        foreach (var argumento in argumentos) inicio.ArgumentList.Add(argumento);
        using var processo = Process.Start(inicio)!;
        try
        {
            var erro = processo.StandardError.ReadToEndAsync(prazo.Token);
            await processo.WaitForExitAsync(prazo.Token);
            Assert.True(processo.ExitCode == 0, await erro);
        }
        finally { if (!processo.HasExited) { processo.Kill(true); await processo.WaitForExitAsync(); } }
    }

    private sealed class ProgressoSincrono(Action<ProgressoOperacao> acao) : IProgress<ProgressoOperacao>
    {
        public void Report(ProgressoOperacao value) => acao(value);
    }
}
