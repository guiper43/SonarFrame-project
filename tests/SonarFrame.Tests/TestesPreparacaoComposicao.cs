using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Tests;

public sealed class TestesPreparacaoComposicao
{
    private sealed class ProgressoBlocos : IProgress<string>
    {
        public List<string> Mensagens { get; } = [];
        public void Report(string mensagem) => Mensagens.Add(mensagem);
    }

    [MidiasReaisFact]
    public async Task EdicaoReaproveitaBlocosIntactosEDesfazerRecuperaPreparacaoAnterior()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var foto = Path.Combine(pasta, "foto.png");
            var audio = Path.Combine(pasta, "som.wav");
            await Ffmpeg("-f", "lavfi", "-i", "color=blue:s=160x90", "-frames:v", "1", foto);
            await Ffmpeg("-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=12", audio);
            var f = new Midia(Guid.NewGuid(), "Foto", foto, TipoMidia.Imagem, null);
            var a = new Midia(Guid.NewGuid(), "Som", audio, TipoMidia.Audio, TempoMidia.Segundos(12));
            var projeto = Projeto.Criar("Cache") with
            {
                Biblioteca = [f, a],
                LinhaDoTempo = [new(Guid.NewGuid(), f.Id, f.Nome, TempoMidia.Zero, TempoMidia.Segundos(12))],
                Camadas = [new(new(Guid.NewGuid(), a.Id, a.Nome, TempoMidia.Zero, TempoMidia.Segundos(2)), TempoMidia.Segundos(6))]
            };
            var probe = new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!);
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, probe);
            var progresso = new ProgressoBlocos();
            var previa = await preparador.PrepararAsync(projeto, progresso, CancellationToken.None);
            Assert.Equal(new[] { "Carregando vídeo: 0%.", "Carregando vídeo: 30%.", "Carregando vídeo: 60%.", "Carregando vídeo: 90%.", "Carregando vídeo: 95%.", "Carregando vídeo: 100%." }, progresso.Mensagens);
            var blocos = Directory.GetFiles(Path.GetDirectoryName(previa.Caminho)!, "bloco-*.nut")
                .ToDictionary(c => c, File.GetLastWriteTimeUtc);
            Assert.Equal(3, blocos.Count);
            var editado = projeto with { Camadas = [projeto.Camadas[0] with { Clipe = projeto.Camadas[0].Clipe with { Volume = 0 } }] };
            progresso.Mensagens.Clear();
            await preparador.PrepararAsync(editado, progresso, CancellationToken.None);
            Assert.Equal(4, Directory.GetFiles(Path.GetDirectoryName(previa.Caminho)!, "bloco-*.nut").Length);
            Assert.All(blocos, b => Assert.Equal(b.Value, File.GetLastWriteTimeUtc(b.Key)));
            progresso.Mensagens.Clear();
            await preparador.PrepararAsync(projeto, progresso, CancellationToken.None);
            Assert.Equal(4, Directory.GetFiles(Path.GetDirectoryName(previa.Caminho)!, "bloco-*.nut").Length);
            Assert.All(blocos, b => Assert.Equal(b.Value, File.GetLastWriteTimeUtc(b.Key)));
            var som = Path.Combine(pasta, "resultado.pcm");
            await Ffmpeg("-i", previa.Caminho, "-vn", "-ac", "1", "-ar", "48000", "-f", "s16le", som);
            var pcm = await File.ReadAllBytesAsync(som);
            double Nivel(int segundo) => Enumerable.Range(segundo * 48000 + 12000, 24000)
                .Average(i => Math.Abs((double)BitConverter.ToInt16(pcm, i * 2)));
            Assert.True(Nivel(4) < 5);
            Assert.True(Nivel(6) > 500);
            Assert.True(Nivel(10) < 5);
            Assert.InRange(pcm.Length / 2.0 / 48000, 11.99, 12.08);
            File.SetLastWriteTimeUtc(audio, DateTime.UtcNow.AddMinutes(1));
            progresso.Mensagens.Clear();
            await preparador.PrepararAsync(projeto, progresso, CancellationToken.None);
            Assert.Equal(5, Directory.GetFiles(Path.GetDirectoryName(previa.Caminho)!, "bloco-*.nut").Length);
        }
        finally { Directory.Delete(pasta, true); }
    }

    [MidiasReaisFact]
    public async Task BlocosPreservamVideoAudioECamadaAtravessandoJuncao()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-juncao-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var video = Path.Combine(pasta, "video.mp4");
            var foto = Path.Combine(pasta, "camada.png");
            await Ffmpeg("-f", "lavfi", "-i", "color=red:s=160x90:r=25:d=5", "-f", "lavfi", "-i", "color=blue:s=160x90:r=25:d=7.08",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=12.08", "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0[v]",
                "-map", "[v]", "-map", "2:a", "-c:v", "libx264", "-g", "100", "-pix_fmt", "yuv420p", "-c:a", "aac", video);
            await Ffmpeg("-f", "lavfi", "-i", "color=lime:s=160x90", "-frames:v", "1", foto);
            var v = new Midia(Guid.NewGuid(), "Vídeo", video, TipoMidia.Video, new(302, 25));
            var f = new Midia(Guid.NewGuid(), "Camada", foto, TipoMidia.Imagem, null);
            var projeto = Projeto.Criar("Junções") with
            {
                Biblioteca = [v, f], LinhaDoTempo = [new(Guid.NewGuid(), v.Id, v.Nome, TempoMidia.Zero, new(241, 20))],
                Camadas = [new(new(Guid.NewGuid(), f.Id, f.Nome, TempoMidia.Zero, TempoMidia.Segundos(1)), new(9, 2))]
            };
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!,
                new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!));
            var previa = await preparador.PrepararAsync(projeto, null, CancellationToken.None);
            var pixels = Path.Combine(pasta, "pixels.rgb");
            await Ffmpeg("-i", previa.Caminho, "-vf", "scale=1:1", "-pix_fmt", "rgb24", "-f", "rawvideo", "-an", pixels);
            var bytes = await File.ReadAllBytesAsync(pixels);
            Assert.True(bytes[120 * 3] > 180);
            Assert.True(bytes[147 * 3 + 1] > 180);
            Assert.True(bytes[153 * 3 + 1] > 180);
            Assert.True(bytes[168 * 3 + 2] > 180);
            Assert.True(bytes[303 * 3 + 2] > 180);
            Assert.InRange(bytes.Length / 3, 361, 363);
            var som = Path.Combine(pasta, "som.pcm");
            await Ffmpeg("-i", previa.Caminho, "-vn", "-ac", "1", "-ar", "48000", "-f", "s16le", som);
            var pcm = await File.ReadAllBytesAsync(som);
            foreach (var segundo in new[] { 5, 10 })
            {
                var nivel = Enumerable.Range(segundo * 48000 - 240, 480).Average(i => Math.Abs((double)BitConverter.ToInt16(pcm, i * 2)));
                Assert.True(nivel > 500);
            }
            Assert.InRange(pcm.Length / 2.0 / 48000, 12.04, 12.10);
        }
        finally { Directory.Delete(pasta, true); }
    }

    [MidiasReaisFact]
    public async Task PreviaRespeitaRecorteFotoAudioVolumeECache()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-teste-composicao-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var video = Path.Combine(pasta, "vídeo teste.mp4");
            var foto = Path.Combine(pasta, "foto.png");
            var audio = Path.Combine(pasta, "audio.wav");
            await Ffmpeg("-f", "lavfi", "-i", "color=red:s=160x90:r=30:d=0.5", "-f", "lavfi", "-i", "color=lime:s=160x90:r=30:d=1.5", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0[v]", "-map", "[v]", "-map", "2:a", "-c:v", "libx264", "-g", "60", "-pix_fmt", "yuv420p", "-c:a", "aac", video);
            await Ffmpeg("-f", "lavfi", "-i", "color=blue:s=160x90", "-frames:v", "1", foto);
            await Ffmpeg("-f", "lavfi", "-i", "sine=frequency=880:duration=2", audio);
            var v = new Midia(Guid.NewGuid(), "Vídeo", video, TipoMidia.Video, TempoMidia.Segundos(2));
            var f = new Midia(Guid.NewGuid(), "Foto", foto, TipoMidia.Imagem, null);
            var a = new Midia(Guid.NewGuid(), "Áudio", audio, TipoMidia.Audio, TempoMidia.Segundos(2));
            var p = Projeto.Criar("Teste") with
            {
                Biblioteca = [v, f, a], LinhaDoTempo = [
                    new(Guid.NewGuid(), v.Id, v.Nome, new(1, 2), TempoMidia.Segundos(1), 0),
                    new(Guid.NewGuid(), f.Id, f.Nome, TempoMidia.Zero, TempoMidia.Segundos(1)),
                    new(Guid.NewGuid(), a.Id, a.Nome, new(1, 2), TempoMidia.Segundos(1))]
            };
            var probe = new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!);
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, probe);
            var previa = await preparador.PrepararAsync(p, null, CancellationToken.None);
            using var json = JsonDocument.Parse(await probe.InspecionarAsync(previa.Caminho, CancellationToken.None));
            var duracao = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            Assert.InRange(duracao, 2.96, 3.08);
            var pixels = Path.Combine(pasta, "pixels.rgb");
            await Ffmpeg("-i", previa.Caminho, "-vf", "scale=1:1", "-pix_fmt", "rgb24", "-f", "rawvideo", "-an", pixels);
            var bytes = await File.ReadAllBytesAsync(pixels);
            Assert.True(bytes[15 * 3 + 1] > 180 && bytes[15 * 3] < 50);
            Assert.True(bytes[45 * 3 + 2] > 180 && bytes[45 * 3] < 50);
            Assert.True(bytes[75 * 3] < 20 && bytes[75 * 3 + 2] < 20);
            var som = Path.Combine(pasta, "som.pcm");
            await Ffmpeg("-i", previa.Caminho, "-vn", "-ac", "1", "-ar", "48000", "-f", "s16le", som);
            var pcm = await File.ReadAllBytesAsync(som);
            double Nivel(int inicio, int fim) => Enumerable.Range(inicio, fim - inicio).Average(i => Math.Abs((double)BitConverter.ToInt16(pcm, i * 2)));
            Assert.True(Nivel(12000, 36000) < 5);
            Assert.True(Nivel(108000, 132000) > 500);
            Assert.Same(previa, await preparador.PrepararAsync(p with { Cursor = TempoMidia.Segundos(1) }, null, CancellationToken.None));
            var editado = p with { LinhaDoTempo = p.LinhaDoTempo.SetItem(0, p.LinhaDoTempo[0] with { Volume = 50 }) };
            Assert.NotEqual(previa.Id, (await preparador.PrepararAsync(editado, null, CancellationToken.None)).Id);
            using var cancelado = new CancellationTokenSource(); cancelado.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparador.PrepararAsync(p, null, cancelado.Token));
            var comCamada = p with { Camadas = [new(p.LinhaDoTempo[0] with { Id = Guid.NewGuid() }, TempoMidia.Zero)] };
            await preparador.PrepararAsync(comCamada, null, CancellationToken.None);
            Assert.True(await preparador.EstaAtualizadaAsync(comCamada, CancellationToken.None));
            File.SetLastWriteTimeUtc(video, DateTime.UtcNow.AddMinutes(1));
            Assert.False(await preparador.EstaAtualizadaAsync(comCamada, CancellationToken.None));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [MidiasReaisFact]
    public async Task CamadasRespeitamIntervaloTransparenciaEmpilhamentoEMixSemNormalizacao()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-camadas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var vermelho = Path.Combine(pasta, "vermelho.png");
            var azul = Path.Combine(pasta, "azul.png");
            var transparente = Path.Combine(pasta, "transparência.png");
            var som1 = Path.Combine(pasta, "440.wav"); var som2 = Path.Combine(pasta, "880.wav");
            await Ffmpeg("-f", "lavfi", "-i", "color=red:s=160x90", "-frames:v", "1", vermelho);
            await Ffmpeg("-f", "lavfi", "-i", "color=blue:s=160x90", "-frames:v", "1", azul);
            await Ffmpeg("-f", "lavfi", "-i", "color=lime@0.5:s=90x90,format=rgba", "-frames:v", "1", transparente);
            await Ffmpeg("-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4", som1);
            await Ffmpeg("-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=4", som2);
            var r = new Midia(Guid.NewGuid(), "Vermelho", vermelho, TipoMidia.Imagem, null);
            var b = r with { Id = Guid.NewGuid(), Nome = "Azul", Caminho = azul };
            var t = r with { Id = Guid.NewGuid(), Nome = "Transparente", Caminho = transparente };
            var a = new Midia(Guid.NewGuid(), "440", som1, TipoMidia.Audio, TempoMidia.Segundos(4));
            var c = a with { Id = Guid.NewGuid(), Nome = "880", Caminho = som2 };
            Clipe Clipe(Midia m, TempoMidia duracao, int volume = 100) => new(Guid.NewGuid(), m.Id, m.Nome, TempoMidia.Zero, duracao, volume);
            var p = Projeto.Criar("Camadas") with
            {
                Biblioteca = [r, b, t, a, c], LinhaDoTempo = [Clipe(r, TempoMidia.Segundos(4))],
                Camadas = [
                    new(Clipe(b, TempoMidia.Segundos(2)), TempoMidia.Segundos(1)),
                    new(Clipe(t, new(1, 2)), TempoMidia.Segundos(2)),
                    new(Clipe(a, TempoMidia.Segundos(2), 50), TempoMidia.Segundos(1)),
                    new(Clipe(c, new(1, 2), 200), TempoMidia.Segundos(2))]
            };
            var probe = new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!);
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, probe);
            var previa = await preparador.PrepararAsync(p, null, CancellationToken.None);
            var pixels = Path.Combine(pasta, "camadas.rgb");
            await Ffmpeg("-i", previa.Caminho, "-vf", "scale=16:9", "-pix_fmt", "rgb24", "-f", "rawvideo", "-an", pixels);
            var bytes = await File.ReadAllBytesAsync(pixels);
            byte Cor(int quadro, int x, int canal) => bytes[(quadro * 16 * 9 + 4 * 16 + x) * 3 + canal];
            Assert.True(Cor(15, 8, 0) > 200);
            Assert.True(Cor(45, 8, 2) > 200 && Cor(45, 8, 0) < 30);
            Assert.InRange(Cor(66, 8, 1), 105, 150); Assert.InRange(Cor(66, 8, 2), 105, 150);
            Assert.True(Cor(66, 0, 2) > 200 && Cor(66, 0, 1) < 30);
            Assert.True(Cor(81, 8, 2) > 200 && Cor(81, 8, 1) < 30);
            Assert.True(Cor(105, 8, 0) > 200 && Cor(105, 8, 2) < 30);
            var quadro = await preparador.ObterQuadroAsync(p, new(22, 10), CancellationToken.None);
            Assert.NotNull(quadro); Assert.Equal(new TempoMidia(66, 30), quadro.Posicao);
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, quadro.Png.Take(4));
            var ultimoQuadro = await preparador.ObterQuadroAsync(p, p.Duracao, CancellationToken.None);
            Assert.Equal(p.Duracao, ultimoQuadro!.Posicao);
            var som = Path.Combine(pasta, "mix.f32");
            await Ffmpeg("-i", previa.Caminho, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", som);
            var pcm = await File.ReadAllBytesAsync(som);
            double Amplitude(double segundo, int frequencia)
            {
                double real = 0, imaginario = 0;
                for (var i = 0; i < 4800; i++)
                {
                    var valor = BitConverter.ToSingle(pcm, ((int)(segundo * 48000) + i) * 4);
                    var fase = 2 * Math.PI * frequencia * i / 48000;
                    real += valor * Math.Cos(fase); imaginario += valor * Math.Sin(fase);
                }
                return 2 * Math.Sqrt(real * real + imaginario * imaginario) / 4800;
            }
            Assert.True(Amplitude(0.2, 440) < 0.001);
            var base440 = Amplitude(1.2, 440);
            Assert.True(base440 > 0.03);
            Assert.InRange(Amplitude(2.2, 440) / base440, 0.85, 1.15);
            Assert.InRange(Amplitude(2.2, 880) / base440, 3.5, 4.5);
            Assert.True(Amplitude(3.2, 440) < 0.001);
            var cortado = new ExcluirIntervalo(new(1, 2), new(3, 2)).Aplicar(p).Validar();
            var aposCorte = await preparador.PrepararAsync(cortado, null, CancellationToken.None);
            Assert.Equal(TempoMidia.Segundos(3), aposCorte.Duracao);
            await Ffmpeg("-i", aposCorte.Caminho, "-vf", "scale=16:9", "-pix_fmt", "rgb24", "-f", "rawvideo", "-an", pixels);
            bytes = await File.ReadAllBytesAsync(pixels);
            Assert.True(Cor(21, 8, 2) > 200 && Cor(21, 8, 0) < 30);
            Assert.InRange(Cor(36, 8, 1), 105, 150);
            Assert.True(Cor(69, 8, 0) > 200 && Cor(69, 8, 2) < 30);
            File.Delete(azul);
            await Assert.ThrowsAsync<FileNotFoundException>(() => preparador.PrepararAsync(cortado, null, CancellationToken.None));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [MidiasReaisFact]
    public async Task PreservaOrientacaoCancelaPreparacaoELiberaCache()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-orientacao-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var horizontal = Path.Combine(pasta, "horizontal.mp4"); var girado = Path.Combine(pasta, "celular.mov");
            await Ffmpeg("-f", "lavfi", "-i", "testsrc2=s=160x90:r=30:d=2", "-c:v", "libx264", horizontal);
            await Ffmpeg("-display_rotation", "90", "-i", horizontal, "-c", "copy", girado);
            var m = new Midia(Guid.NewGuid(), "Celular", girado, TipoMidia.Video, TempoMidia.Segundos(2));
            var p = Projeto.Criar("Rotação") with { Biblioteca = [m], LinhaDoTempo = [new(Guid.NewGuid(), m.Id, m.Nome, new(1, 3), TempoMidia.Segundos(1))] };
            var probe = new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!);
            string caminhoCache;
            await using (var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, probe))
            {
                var previa = await preparador.PrepararAsync(p, null, CancellationToken.None);
                caminhoCache = previa.Caminho;
                using var json = JsonDocument.Parse(await probe.InspecionarAsync(previa.Caminho, CancellationToken.None));
                var video = json.RootElement.GetProperty("streams")[0];
                Assert.Equal(360, video.GetProperty("width").GetInt32());
                Assert.Equal(640, video.GetProperty("height").GetInt32());
                var foto = Path.Combine(pasta, "foto.png");
                await Ffmpeg("-i", horizontal, "-frames:v", "1", foto);
                var f = new Midia(Guid.NewGuid(), "Foto", foto, TipoMidia.Imagem, null);
                var longo = Projeto.Criar("Cancelar") with { Biblioteca = [f], LinhaDoTempo = [new(Guid.NewGuid(), f.Id, f.Nome, TempoMidia.Zero, TempoMidia.Segundos(1800))] };
                using var cancelamento = new CancellationTokenSource();
                var progresso = new AoPreparar(() => cancelamento.CancelAfter(100));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparador.PrepararAsync(longo, progresso, cancelamento.Token));
                Assert.False(await preparador.EstaAtualizadaAsync(longo, CancellationToken.None));
                Assert.True(File.Exists((await preparador.PrepararAsync(p, null, CancellationToken.None)).Caminho));
            }
            Assert.False(Directory.Exists(Path.GetDirectoryName(caminhoCache)));
            using var origem = new FileStream(girado, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { Directory.Delete(pasta, true); }
    }

    [MidiasReaisFact]
    public async Task SomaDeVolumesPodeSaturarSemNormalizacao()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-saturacao-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var som = Path.Combine(pasta, "alto.wav");
            await Ffmpeg("-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2", "-af", "volume=6", som);
            var m = new Midia(Guid.NewGuid(), "Som alto", som, TipoMidia.Audio, TempoMidia.Segundos(2));
            var clipe = new Clipe(Guid.NewGuid(), m.Id, m.Nome, TempoMidia.Zero, m.Duracao!.Value);
            var p = Projeto.Criar("Saturação") with { Biblioteca = [m], LinhaDoTempo = [clipe], Camadas = [new(clipe with { Id = Guid.NewGuid(), Volume = 200 }, TempoMidia.Zero)] };
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!));
            var previa = await preparador.PrepararAsync(p, null, CancellationToken.None);
            var destino = Path.Combine(pasta, "som.pcm");
            await Ffmpeg("-i", previa.Caminho, "-vn", "-ac", "2", "-ar", "48000", "-f", "f32le", destino);
            var pcm = await File.ReadAllBytesAsync(destino);
            var excedentes = Enumerable.Range(0, pcm.Length / 4).Count(i => Math.Abs(BitConverter.ToSingle(pcm, i * 4)) > 1);
            var pico = Enumerable.Range(0, pcm.Length / 4).Max(i => Math.Abs(BitConverter.ToSingle(pcm, i * 4)));
            Assert.True(excedentes > 0 && pico > 1.2, "A soma sem limitador pode exceder a faixa de saída e saturar no dispositivo.");
        }
        finally { Directory.Delete(pasta, true); }
    }

    private sealed class AoPreparar(Action acao) : IProgress<string>
    {
        public void Report(string valor) { if (valor == "Carregando vídeo: 0%.") acao(); }
    }

    internal static async Task Ffmpeg(params string[] argumentos)
    {
        var inicio = new ProcessStartInfo(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-y", "-nostdin" }.Concat(argumentos)) inicio.ArgumentList.Add(arg);
        using var processo = Process.Start(inicio)!;
        var erros = processo.StandardError.ReadToEndAsync();
        await processo.WaitForExitAsync();
        Assert.True(processo.ExitCode == 0, await erros);
    }
}
