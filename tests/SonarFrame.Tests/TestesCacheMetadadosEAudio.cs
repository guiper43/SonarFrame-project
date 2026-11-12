using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;
using static SonarFrame.Tests.TestesPreparacaoComposicao;

namespace SonarFrame.Tests;

public sealed class TestesCacheMetadadosEAudio
{
    [MidiasReaisFact]
    public async Task ReordenarPreservaAudioComBlocosReutilizados()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-reordenar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var som = Path.Combine(pasta, "som.mp4");
            var foto = Path.Combine(pasta, "foto.png");
            await Ffmpeg("-f", "lavfi", "-i", "color=red:s=160x90:r=30:d=10", "-f", "lavfi", "-i", "sine=frequency=440:duration=10", "-c:v", "libx264", "-c:a", "aac", som);
            await Ffmpeg("-f", "lavfi", "-i", "color=blue:s=160x90", "-frames:v", "1", foto);
            var a = new Midia(Guid.NewGuid(), "Vídeo", som, TipoMidia.Video, TempoMidia.Segundos(10));
            var f = new Midia(Guid.NewGuid(), "Foto", foto, TipoMidia.Imagem, null);
            var projeto = Projeto.Criar("Ordem") with
            {
                Biblioteca = [a, f], LinhaDoTempo = [new(Guid.NewGuid(), a.Id, a.Nome, TempoMidia.Zero, TempoMidia.Segundos(10)),
                    new(Guid.NewGuid(), f.Id, f.Nome, TempoMidia.Zero, TempoMidia.Segundos(5))]
            };
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!));
            await preparador.PrepararAsync(projeto, null, default);
            var movido = new MoverClipe(projeto.LinhaDoTempo[0].Id, 1).Aplicar(projeto);
            var previa = await preparador.PrepararAsync(movido, null, default);
            var pcm = Path.Combine(pasta, "som.pcm");
            await Ffmpeg("-i", previa.Caminho, "-vn", "-ac", "1", "-ar", "48000", "-f", "s16le", pcm);
            var bytes = await File.ReadAllBytesAsync(pcm);
            double Nivel(int segundo) => Enumerable.Range(segundo * 48000, 24000).Average(i => Math.Abs((double)BitConverter.ToInt16(bytes, i * 2)));
            Assert.True(Nivel(2) < 5);
            Assert.True(Nivel(7) > 500);
            Assert.True(Nivel(12) > 500);
        }
        finally { Directory.Delete(pasta, true); }
    }

    private sealed class ProbeContado(IExecutorFfprobe origem) : IExecutorFfprobe
    {
        public int Leituras { get; private set; }
        public bool Cancelar { get; set; }
        public async Task<string> InspecionarAsync(string caminhoCompleto, CancellationToken token)
        {
            Leituras++;
            if (Cancelar) throw new OperationCanceledException();
            return await origem.InspecionarAsync(caminhoCompleto, token);
        }
        public Task<string> InspecionarQuadrosAsync(string caminhoCompleto, CancellationToken token) => origem.InspecionarQuadrosAsync(caminhoCompleto, token);
    }

    [MidiasReaisFact]
    public async Task BuscaReutilizaMetadadosERevalidaOrigemSemGuardarCancelamento()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-metadados-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var foto = Path.Combine(pasta, "foto.png");
            await Ffmpeg("-f", "lavfi", "-i", "color=blue:s=160x90", "-frames:v", "1", foto);
            var midia = new Midia(Guid.NewGuid(), "Foto", foto, TipoMidia.Imagem, null);
            var projeto = Projeto.Criar("Metadados") with
            {
                Biblioteca = [midia], LinhaDoTempo = [new(Guid.NewGuid(), midia.Id, midia.Nome, TempoMidia.Zero, TempoMidia.Segundos(2))]
            };
            var probe = new ProbeContado(new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!)) { Cancelar = true };
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, probe);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparador.ObterQuadroAsync(projeto, TempoMidia.Zero, default));
            probe.Cancelar = false;
            Assert.NotNull(await preparador.ObterQuadroAsync(projeto, TempoMidia.Zero, default));
            Assert.Equal(2, probe.Leituras);
            Assert.NotNull(await preparador.ObterQuadroAsync(projeto, TempoMidia.Segundos(1), default));
            Assert.Equal(2, probe.Leituras);
            File.SetLastWriteTimeUtc(foto, DateTime.UtcNow.AddMinutes(1));
            Assert.NotNull(await preparador.ObterQuadroAsync(projeto, TempoMidia.Zero, default));
            Assert.Equal(3, probe.Leituras);
            await Ffmpeg("-f", "lavfi", "-i", "color=blue:s=90x160", "-frames:v", "1", foto);
            Assert.NotNull(await preparador.ObterQuadroAsync(projeto, TempoMidia.Zero, default));
            Assert.Equal(4, probe.Leituras);
            File.Delete(foto);
            await Assert.ThrowsAsync<FileNotFoundException>(() => preparador.ObterQuadroAsync(projeto, TempoMidia.Zero, default));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [MidiasReaisFact]
    public async Task AudioComprimidoPreservaAmostrasNasJuncoesComRecorteFracionario()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-audio-comprimido-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            foreach (var (extensao, codec) in new[] { ("mp3", "libmp3lame"), ("m4a", "aac"), ("ogg", "libvorbis"), ("opus", "libopus") })
            {
                var origem = Path.Combine(pasta, "origem." + extensao);
                await Ffmpeg("-f", "lavfi", "-i", "aevalsrc=0.15*sin(2*PI*440*t)+0.15*sin(2*PI*997*t):s=48000:d=13", "-c:a", codec, origem);
                var midia = new Midia(Guid.NewGuid(), extensao, origem, TipoMidia.Audio, TempoMidia.Segundos(13));
                var projeto = Projeto.Criar("Junções") with
                {
                    Biblioteca = [midia], LinhaDoTempo = [new(Guid.NewGuid(), midia.Id, midia.Nome, new(137, 1000), TempoMidia.Segundos(12))]
                };
                await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!,
                    new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!));
                var previa = await preparador.PrepararAsync(projeto, null, default);
                var referencia = Path.Combine(pasta, "referencia.pcm");
                var resultado = Path.Combine(pasta, "resultado.pcm");
                await Ffmpeg("-i", origem, "-af", "atrim=start=0.137:duration=12,asetpts=PTS-STARTPTS", "-ac", "1", "-ar", "48000", "-f", "f32le", referencia);
                await Ffmpeg("-i", previa.Caminho, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", resultado);
                var esperado = await File.ReadAllBytesAsync(referencia);
                var obtido = await File.ReadAllBytesAsync(resultado);
                Assert.InRange(obtido.Length / 4.0 / 48000, 12, 12.08);
                foreach (var segundo in new[] { 5, 10 })
                {
                    double erro = 0, energia = 0;
                    for (var i = segundo * 48000 - 2400; i < segundo * 48000 + 2400; i++)
                    {
                        var a = BitConverter.ToSingle(esperado, i * 4);
                        var b = BitConverter.ToSingle(obtido, i * 4);
                        erro += (a - b) * (a - b);
                        energia += a * a;
                    }
                    Assert.True(erro / energia < 0.02, $"{extensao}: descontinuidade na junção de {segundo}s, erro relativo {erro / energia}.");
                }
            }
        }
        finally { Directory.Delete(pasta, true); }
    }
}
