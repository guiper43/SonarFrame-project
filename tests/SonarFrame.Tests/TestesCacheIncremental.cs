using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Tests;

public sealed class TestesCacheIncremental
{
    [MidiasReaisFact]
    public async Task RippleDivisaoEVolumeReutilizamConteudoComCamadasRelativas()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-incremental-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var caminho = Path.Combine(pasta, "video.mp4");
            await TestesPreparacaoComposicao.Ffmpeg("-f", "lavfi", "-i", "testsrc2=s=160x90:r=30:d=15", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=15", "-vf", "select='if(lt(t,7),not(mod(n,2)),1)'", "-fps_mode", "vfr", "-c:v", "libx264", "-threads", "2", "-c:a", "aac", caminho);
            var midia = new Midia(Guid.NewGuid(), "Original", caminho, TipoMidia.Video, TempoMidia.Segundos(15));
            var projeto = Projeto.Criar("Incremental") with { Biblioteca = [midia], LinhaDoTempo = [new(Guid.NewGuid(), midia.Id, "Inteiro", TempoMidia.Zero, TempoMidia.Segundos(15))] };
            projeto = new AdicionarCamada(midia.Id, TempoMidia.Segundos(6), TempoMidia.Segundos(1), TempoMidia.Segundos(2)).Aplicar(projeto);
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!));
            await preparador.PrepararAsync(projeto, null, CancellationToken.None);
            Assert.Equal(3, preparador.UltimoDiagnostico!.BlocosRenderizados);
            var dividido = new DividirClipe(projeto.LinhaDoTempo[0].Id, new TempoMidia(7, 3)).Aplicar(projeto);
            Assert.True(await preparador.EstaAtualizadaAsync(dividido, CancellationToken.None));
            await preparador.PrepararAsync(dividido, null, CancellationToken.None);
            Assert.True(preparador.UltimoDiagnostico!.PreviaReutilizada);
            var renomeado = dividido with { LinhaDoTempo = [.. dividido.LinhaDoTempo.Select(c => c with { Id = Guid.NewGuid(), Nome = "Outro nome" })], ItemSelecionadoId = null };
            Assert.True(await preparador.EstaAtualizadaAsync(renomeado, CancellationToken.None));
            var cortado = new ExcluirIntervalo(TempoMidia.Zero, TempoMidia.Segundos(1)).Aplicar(projeto);
            var reaproveitada = await preparador.PrepararAsync(cortado, null, CancellationToken.None);
            Assert.Equal(2, preparador.UltimoDiagnostico!.CacheHits);
            Assert.Equal(1, preparador.UltimoDiagnostico.BlocosRenderizados);
            Assert.Equal(".mov", Path.GetExtension(reaproveitada.Caminho));
            var audioMontado = Path.Combine(pasta, "montado.f32");
            var audioBlocos = Path.Combine(pasta, "blocos.f32");
            await TestesPreparacaoComposicao.Ffmpeg("-i", reaproveitada.Caminho, "-map", "0:a:0", "-c:a", "pcm_f32le", "-f", "f32le", audioMontado);
            await TestesPreparacaoComposicao.Ffmpeg("-f", "concat", "-safe", "1", "-i", Path.Combine(Path.GetDirectoryName(reaproveitada.Caminho)!, "audios.txt"), "-map", "0:a:0", "-c:a", "pcm_f32le", "-f", "f32le", audioBlocos);
            Assert.Equal(await File.ReadAllBytesAsync(audioBlocos), await File.ReadAllBytesAsync(audioMontado));
            Assert.Equal(14L * 48000 * 2 * sizeof(float), new FileInfo(audioMontado).Length);
            var hashIncremental = Path.Combine(pasta, "incremental.md5");
            var hashFrio = Path.Combine(pasta, "frio.md5");
            await TestesPreparacaoComposicao.Ffmpeg("-i", reaproveitada.Caminho, "-map", "0:v:0", "-map", "0:a:0", "-f", "framemd5", hashIncremental);
            await ValidarContinuidadeAudioAsync(hashIncremental, 14L * 48000);
            await using (var frio = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!)))
            {
                var referencia = await frio.PrepararAsync(cortado, null, CancellationToken.None);
                await TestesPreparacaoComposicao.Ffmpeg("-i", referencia.Caminho, "-map", "0:v:0", "-map", "0:a:0", "-f", "framemd5", hashFrio);
                Assert.Equal(await File.ReadAllTextAsync(hashFrio), await File.ReadAllTextAsync(hashIncremental));
            }
            await preparador.PrepararAsync(projeto, null, CancellationToken.None);
            Assert.Equal(0, preparador.UltimoDiagnostico!.BlocosRenderizados);
            var volume = new DefinirVolume(projeto.LinhaDoTempo[0].Id, 50).Aplicar(projeto);
            await preparador.PrepararAsync(volume, null, CancellationToken.None);
            Assert.Equal(0, preparador.UltimoDiagnostico!.VideosRenderizados);
            Assert.Equal(3, preparador.UltimoDiagnostico.AudiosRenderizados);
            Assert.True(preparador.UltimoDiagnostico.MixagemAudioMs > 0);
            Assert.Equal(0, preparador.UltimoDiagnostico.CodificacaoAudioMs);
            Assert.Equal(4, preparador.UltimoDiagnostico.ProcessosFfmpeg);
            var volumeParcial = new DefinirVolume(dividido.LinhaDoTempo[1].Id, 50).Aplicar(dividido);
            await preparador.PrepararAsync(volumeParcial, null, CancellationToken.None);
            Assert.Equal(0, preparador.UltimoDiagnostico!.VideosRenderizados);
            Assert.True(preparador.UltimoDiagnostico.AudiosRenderizados > 0);
            var deslocada = projeto with { Camadas = [projeto.Camadas[0] with { Inicio = TempoMidia.Segundos(7) }] };
            await preparador.PrepararAsync(deslocada, null, CancellationToken.None);
            Assert.Equal(1, preparador.UltimoDiagnostico!.VideosRenderizados);
            Assert.Equal(1, preparador.UltimoDiagnostico.AudiosRenderizados);
            var fracionario = new ExcluirIntervalo(TempoMidia.Zero, new TempoMidia(1, 100)).Aplicar(projeto);
            await preparador.PrepararAsync(fracionario, null, CancellationToken.None);
            Assert.Equal(0, preparador.UltimoDiagnostico!.CacheHits);
            Assert.Equal(3, preparador.UltimoDiagnostico.BlocosRenderizados);
            var inserido = projeto with
            {
                LinhaDoTempo = projeto.LinhaDoTempo.Insert(0, new Clipe(Guid.NewGuid(), midia.Id, "Inserido", TempoMidia.Zero, TempoMidia.Segundos(1))),
                Camadas = [.. projeto.Camadas.Select(c => c with { Inicio = c.Inicio + TempoMidia.Segundos(1) })]
            };
            await preparador.PrepararAsync(inserido, null, CancellationToken.None);
            Assert.Equal(3, preparador.UltimoDiagnostico!.CacheHits);
            Assert.Equal(1, preparador.UltimoDiagnostico.BlocosRenderizados);
            var concorrentes = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => preparador.PrepararAsync(inserido, null, CancellationToken.None)));
            Assert.All(concorrentes, m => Assert.Equal(concorrentes[0].Caminho, m.Caminho));
            Assert.True(preparador.UltimoDiagnostico!.PreviaReutilizada);
            Assert.Equal(0, preparador.UltimoDiagnostico.ProcessosFfmpeg);
            var fronteira = new ExcluirIntervalo(new TempoMidia(499, 100), new TempoMidia(501, 100)).Aplicar(projeto);
            var previaFronteira = await preparador.PrepararAsync(fronteira, null, CancellationToken.None);
            await TestesPreparacaoComposicao.Ffmpeg("-i", previaFronteira.Caminho, "-map", "0:v:0", "-map", "0:a:0", "-f", "framemd5", hashIncremental);
            await ValidarContinuidadeAudioAsync(hashIncremental, 15L * 48000 - 960);
            await using var referenciaFronteira = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!));
            var friaFronteira = await referenciaFronteira.PrepararAsync(fronteira, null, CancellationToken.None);
            await TestesPreparacaoComposicao.Ffmpeg("-i", friaFronteira.Caminho, "-map", "0:v:0", "-map", "0:a:0", "-f", "framemd5", hashFrio);
            Assert.Equal(await File.ReadAllTextAsync(hashFrio), await File.ReadAllTextAsync(hashIncremental));
        }
        finally { Directory.Delete(pasta, true); }
    }

    private static async Task ValidarContinuidadeAudioAsync(string caminho, long amostrasEsperadas)
    {
        var linhas = await File.ReadAllLinesAsync(caminho);
        Assert.Contains("#tb 1: 1/48000", linhas);
        long proxima = 0;
        foreach (var campos in linhas.Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#')).Select(l => l.Split(',')).Where(c => c[0].Trim() == "1"))
        {
            Assert.Equal(proxima, long.Parse(campos[2].Trim()));
            var duracao = long.Parse(campos[3].Trim());
            Assert.True(duracao > 0);
            proxima += duracao;
        }
        Assert.Equal(amostrasEsperadas, proxima);
    }
}
