using System.Diagnostics;
using System.Text.Json;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;
using static SonarFrame.Tests.TestesEdicao;

namespace SonarFrame.Tests;

public sealed class TestesFades
{
    [Fact]
    public void ValidarRejeitaFadesInvalidosPorTipoSomaOuValorNegativo()
    {
        var sessao = new SessaoEdicao(Projeto.Criar("Fades"));
        var video = Video(10);
        var audio = new Midia(Guid.NewGuid(), "Música", "musica.mp3", TipoMidia.Audio, Segundos(8));
        var imagem = new Midia(Guid.NewGuid(), "Foto", "foto.png", TipoMidia.Imagem, null);
        sessao.Executar(new RegistrarMidia(video));
        sessao.Executar(new RegistrarMidia(audio));
        sessao.Executar(new RegistrarMidia(imagem));
        sessao.Executar(new InserirClipe(video.Id, 0));
        sessao.Executar(new InserirClipe(imagem.Id, 1, Segundos(4)));
        sessao.Executar(new AdicionarCamada(audio.Id, Segundos(0), Segundos(0), Segundos(6)));

        var clipeVideoId = sessao.Atual.LinhaDoTempo[0].Id;
        var clipeImagemId = sessao.Atual.LinhaDoTempo[1].Id;
        var clipeAudioId = sessao.Atual.Camadas[0].Clipe.Id;

        Assert.Throws<ArgumentException>(() => sessao.Executar(new DefinirFades(clipeVideoId, new(-1, 1), TempoMidia.Zero, TempoMidia.Zero, TempoMidia.Zero)));
        Assert.Throws<ArgumentException>(() => sessao.Executar(new DefinirFades(clipeVideoId, Segundos(6), Segundos(5), TempoMidia.Zero, TempoMidia.Zero)));
        Assert.Throws<ArgumentException>(() => sessao.Executar(new DefinirFades(clipeVideoId, TempoMidia.Zero, TempoMidia.Zero, Segundos(7), Segundos(4))));
        Assert.Throws<ArgumentException>(() => sessao.Executar(new DefinirFades(clipeImagemId, Segundos(1), TempoMidia.Zero, TempoMidia.Zero, TempoMidia.Zero)));
        Assert.Throws<ArgumentException>(() => sessao.Executar(new DefinirFades(clipeAudioId, TempoMidia.Zero, TempoMidia.Zero, Segundos(1), TempoMidia.Zero)));

        sessao.Executar(new DefinirFades(clipeVideoId, Segundos(2), Segundos(3), Segundos(1), Segundos(2)));
        sessao.Executar(new DefinirFades(clipeImagemId, TempoMidia.Zero, TempoMidia.Zero, Segundos(1), Segundos(1)));
        sessao.Executar(new DefinirFades(clipeAudioId, Segundos(2), Segundos(2), TempoMidia.Zero, TempoMidia.Zero));

        var clipeVideo = sessao.Atual.LinhaDoTempo[0];
        Assert.Equal(Segundos(2), clipeVideo.FadeInAudio);
        Assert.Equal(Segundos(3), clipeVideo.FadeOutAudio);
        Assert.Equal(Segundos(1), clipeVideo.FadeInVideo);
        Assert.Equal(Segundos(2), clipeVideo.FadeOutVideo);

        sessao.Executar(new AtualizarCamada(clipeAudioId, Segundos(0), Segundos(0), Segundos(3)));
        var camadaAtualizada = sessao.Atual.Camadas[0].Clipe;
        Assert.Equal(Segundos(3), camadaAtualizada.Duracao);
        Assert.Equal(Segundos(2), camadaAtualizada.FadeInAudio);
        Assert.Equal(Segundos(1), camadaAtualizada.FadeOutAudio);
    }

    [Fact]
    public async Task DividirExcluirDesfazerEPersistirPreservamRegrasDeFades()
    {
        var diretorio = Path.Combine(AppContext.BaseDirectory, "test-fades", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(diretorio);
        try
        {
            var sessao = Sessao();
            var clipeId = sessao.Atual.LinhaDoTempo[0].Id;
            sessao.SelecionarItem(clipeId);
            var acoes = new AcoesEdicao(sessao);
            acoes.DefinirFadesSelecionado(Segundos(3), Segundos(4), Segundos(2), Segundos(3));
            Assert.True(acoes.Selecionado().TemFades);

            sessao.Posicionar(Segundos(2));
            acoes.DividirNoCursor();
            var parte1 = sessao.Atual.LinhaDoTempo[0];
            var parte2 = sessao.Atual.LinhaDoTempo[1];

            Assert.Equal(Segundos(2), parte1.Duracao);
            Assert.Equal(Segundos(2), parte1.FadeInAudio);
            Assert.Equal(TempoMidia.Zero, parte1.FadeOutAudio);
            Assert.Equal(Segundos(2), parte1.FadeInVideo);
            Assert.Equal(TempoMidia.Zero, parte1.FadeOutVideo);

            Assert.Equal(Segundos(8), parte2.Duracao);
            Assert.Equal(TempoMidia.Zero, parte2.FadeInAudio);
            Assert.Equal(Segundos(4), parte2.FadeOutAudio);
            Assert.Equal(TempoMidia.Zero, parte2.FadeInVideo);
            Assert.Equal(Segundos(3), parte2.FadeOutVideo);

            sessao.Executar(new ExcluirIntervalo(Segundos(1), Segundos(9)));
            Assert.Equal(2, sessao.Atual.LinhaDoTempo.Length);
            Assert.Equal(Segundos(1), sessao.Atual.LinhaDoTempo[0].FadeInAudio);
            Assert.Equal(TempoMidia.Zero, sessao.Atual.LinhaDoTempo[0].FadeOutAudio);
            Assert.Equal(TempoMidia.Zero, sessao.Atual.LinhaDoTempo[1].FadeInAudio);
            Assert.Equal(Segundos(1), sessao.Atual.LinhaDoTempo[1].FadeOutAudio);

            sessao.Desfazer();
            sessao.Desfazer();
            Assert.Single(sessao.Atual.LinhaDoTempo);
            Assert.Equal(Segundos(3), sessao.Atual.LinhaDoTempo[0].FadeInAudio);
            Assert.Equal(Segundos(4), sessao.Atual.LinhaDoTempo[0].FadeOutAudio);

            var caminho = Path.Combine(diretorio, "fades.sonarframe");
            var repositorio = new RepositorioProjetosSqlite();
            await repositorio.SalvarAsync(caminho, sessao.Atual);
            var reaberto = await repositorio.CarregarAsync(caminho);
            Assert.Equal(JsonSerializer.Serialize(sessao.Atual), JsonSerializer.Serialize(reaberto));

            acoes.RemoverFadesSelecionado();
            Assert.False(acoes.Selecionado().TemFades);
        }
        finally { Directory.Delete(diretorio, true); }
    }

    [MidiasReaisFact]
    public async Task FadesIndependentesSuavizamAudioVideoECamadasReaproveitamCacheEExportamMp4()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-fades-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var video = Path.Combine(pasta, "base.mp4");
            var camadaImg = Path.Combine(pasta, "camada.png");
            await Ffmpeg("-f", "lavfi", "-i", "color=red:s=320x180:r=30:d=4",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", video);
            await Ffmpeg("-f", "lavfi", "-i", "color=lime:s=320x180", "-frames:v", "1", camadaImg);

            var midiaVideo = new Midia(Guid.NewGuid(), "Base", video, TipoMidia.Video, Segundos(4), new(30, 1));
            var midiaCamada = new Midia(Guid.NewGuid(), "Camada", camadaImg, TipoMidia.Imagem, null);
            var clipePrincipal = new Clipe(Guid.NewGuid(), midiaVideo.Id, midiaVideo.Nome, TempoMidia.Zero, Segundos(4), 100,
                FadeInAudio: TempoMidia.Zero, FadeOutAudio: TempoMidia.Zero,
                FadeInVideo: Segundos(1), FadeOutVideo: Segundos(1));
            var clipeCamada = new Clipe(Guid.NewGuid(), midiaCamada.Id, midiaCamada.Nome, TempoMidia.Zero, Segundos(2), 100,
                FadeInVideo: Segundos(1), FadeOutVideo: Segundos(1));
            var projeto = Projeto.Criar("Fades Reais") with
            {
                Biblioteca = [midiaVideo, midiaCamada],
                LinhaDoTempo = [clipePrincipal],
                Camadas = [new(clipeCamada, Segundos(1))]
            };

            var probe = new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!);
            var ffmpeg = Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!;
            await using var preparador = new PreparadorComposicaoFfmpeg(ffmpeg, probe);

            await preparador.PrepararAsync(projeto, null, CancellationToken.None);
            Assert.Equal(1, preparador.UltimoDiagnostico!.VideosRenderizados);
            Assert.Equal(1, preparador.UltimoDiagnostico.AudiosRenderizados);

            var comFadeAudio = projeto with
            {
                LinhaDoTempo = [clipePrincipal with { FadeInAudio = Segundos(1), FadeOutAudio = Segundos(1) }]
            };
            var previa = await preparador.PrepararAsync(comFadeAudio, null, CancellationToken.None);
            Assert.Equal(0, preparador.UltimoDiagnostico!.VideosRenderizados);
            Assert.Equal(1, preparador.UltimoDiagnostico.AudiosRenderizados);

            var quadroInicio = await preparador.ObterQuadroAsync(comFadeAudio, TempoMidia.Zero, CancellationToken.None);
            var quadroMeioCamada = await preparador.ObterQuadroAsync(comFadeAudio, Segundos(2), CancellationToken.None);
            Assert.NotNull(quadroInicio);
            Assert.NotNull(quadroMeioCamada);

            var rgbInicio = await PixelRgbAsync(pasta, "q0.png", quadroInicio!.Png);
            var rgbCamada = await PixelRgbAsync(pasta, "q2.png", quadroMeioCamada!.Png);
            Assert.True(rgbInicio.R < 20 && rgbInicio.G < 20 && rgbInicio.B < 20);
            Assert.True(rgbCamada.G > 180 && rgbCamada.R < 60);

            var som = Path.Combine(pasta, "previa.pcm");
            await Ffmpeg("-i", previa.Caminho, "-vn", "-ac", "1", "-ar", "48000", "-f", "s16le", som);
            var pcm = await File.ReadAllBytesAsync(som);
            double NivelIntervalo(int amostraInicio, int quantidade) => Enumerable.Range(amostraInicio, quantidade)
                .Average(i => Math.Abs((double)BitConverter.ToInt16(pcm, i * 2)));
            var nivelComeco = NivelIntervalo(0, 4800);
            var nivelMeio = NivelIntervalo(96000 - 12000, 24000);
            var nivelFim = NivelIntervalo(4 * 48000 - 4800, 4000);
            Assert.True(nivelComeco < nivelMeio * 0.25);
            Assert.True(nivelFim < nivelMeio * 0.25);

            var mp4 = Path.Combine(pasta, "exportado.mp4");
            await using var exportador = new ExportadorFfmpeg(ffmpeg, probe);
            await exportador.ExportarAsync(comFadeAudio, new(mp4, Orientacao.Horizontal, ModoEnquadramento.Letterbox, 720), new Progress<ProgressoOperacao>(), CancellationToken.None);
            Assert.True(File.Exists(mp4));

            var pixelsMp4 = Path.Combine(pasta, "mp4.rgb");
            await Ffmpeg("-i", mp4, "-vf", "scale=1:1", "-pix_fmt", "rgb24", "-f", "rawvideo", "-an", pixelsMp4);
            var bytesMp4 = await File.ReadAllBytesAsync(pixelsMp4);
            Assert.True(bytesMp4[0] < 30 && bytesMp4[1] < 30);
            Assert.True(bytesMp4[30 * 3] > 150 && bytesMp4[30 * 3 + 1] < 60);
            Assert.True(bytesMp4[60 * 3 + 1] > 150);
            Assert.True(bytesMp4[^3] < 35 && bytesMp4[^2] < 35);

            var videoLongo = Path.Combine(pasta, "longo.mp4");
            await Ffmpeg("-f", "lavfi", "-i", "color=white:s=320x180:r=30:d=12",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=12",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", videoLongo);
            var midiaLonga = new Midia(Guid.NewGuid(), "Longo", videoLongo, TipoMidia.Video, Segundos(12), new(30, 1));
            var clipeLongo = new Clipe(Guid.NewGuid(), midiaLonga.Id, midiaLonga.Nome, TempoMidia.Zero, Segundos(12), 100,
                FadeInAudio: Segundos(6), FadeOutAudio: Segundos(6),
                FadeInVideo: Segundos(6), FadeOutVideo: Segundos(6));
            var projetoLongo = Projeto.Criar("Fades Longos") with
            {
                Biblioteca = [midiaLonga],
                LinhaDoTempo = [clipeLongo]
            };
            var previaLonga = await preparador.PrepararAsync(projetoLongo, null, CancellationToken.None);
            var somLongo = Path.Combine(pasta, "longo.pcm");
            await Ffmpeg("-i", previaLonga.Caminho, "-vn", "-ac", "1", "-ar", "48000", "-f", "s16le", somLongo);
            var pcmLongo = await File.ReadAllBytesAsync(somLongo);
            double NivelLongo(int segInicio) => Enumerable.Range(segInicio * 48000 + 12000, 24000)
                .Average(i => Math.Abs((double)BitConverter.ToInt16(pcmLongo, i * 2)));
            var nivelSeg4 = NivelLongo(4);
            var nivelSeg5 = NivelLongo(5);
            var nivelSeg6 = NivelLongo(6);
            var nivelSeg7 = NivelLongo(7);
            var nivelSeg10 = NivelLongo(10);
            Assert.True(nivelSeg5 > nivelSeg4, $"Esperado nivelSeg5 ({nivelSeg5:F1}) > nivelSeg4 ({nivelSeg4:F1}) sem reiniciar no bloco de 5s");
            Assert.True(nivelSeg6 > nivelSeg7, $"Esperado nivelSeg6 ({nivelSeg6:F1}) > nivelSeg7 ({nivelSeg7:F1})");
            Assert.True(nivelSeg7 > nivelSeg10, $"Esperado nivelSeg7 ({nivelSeg7:F1}) > nivelSeg10 ({nivelSeg10:F1}) sem reiniciar no bloco de 10s");

            var pixelsLongo = Path.Combine(pasta, "longo.rgb");
            await Ffmpeg("-i", previaLonga.Caminho, "-vf", "scale=1:1", "-pix_fmt", "rgb24", "-f", "rawvideo", "-an", pixelsLongo);
            var bytesLongo = await File.ReadAllBytesAsync(pixelsLongo);
            byte BrilhoSeg(double seg) => bytesLongo[(int)(seg * 30) * 3];
            Assert.True(BrilhoSeg(5.2) > BrilhoSeg(4.5), $"Brilho em 5.2s ({BrilhoSeg(5.2)}) deveria superar 4.5s ({BrilhoSeg(4.5)})");
            Assert.True(BrilhoSeg(6.8) > BrilhoSeg(10.2), $"Brilho em 6.8s ({BrilhoSeg(6.8)}) deveria superar 10.2s ({BrilhoSeg(10.2)})");
        }
        finally { Directory.Delete(pasta, true); }
    }

    [MidiasReaisFact]
    public async Task PreviaPreservaFadeVisualEntreClipesComVolumesDiferentesEAtualizaSomenteCacheDeVideo()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-fades-uniao-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var video = Path.Combine(pasta, "branco.mp4");
            await Ffmpeg("-f", "lavfi", "-i", "color=white:s=160x90:r=30:d=4",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", video);
            var midia = new Midia(Guid.NewGuid(), "Base", video, TipoMidia.Video, Segundos(4), new(30, 1));
            var primeiro = new Clipe(Guid.NewGuid(), midia.Id, "Primeiro", TempoMidia.Zero, Segundos(2), 100, FadeInAudio: Segundos(1));
            var segundo = new Clipe(Guid.NewGuid(), midia.Id, "Segundo", Segundos(2), Segundos(2), 50, FadeOutVideo: Segundos(1));
            var projeto = Projeto.Criar("Fades e volumes") with { Biblioteca = [midia], LinhaDoTempo = [primeiro, segundo] };
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!,
                new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!));
            async Task<byte[]> PixelsAsync(Projeto atual, string nome)
            {
                var previa = await preparador.PrepararAsync(atual, null, CancellationToken.None);
                var pixels = Path.Combine(pasta, nome);
                await Ffmpeg("-i", previa.Caminho, "-vf", "scale=1:1", "-pix_fmt", "rgb24", "-f", "rawvideo", "-an", pixels);
                return await File.ReadAllBytesAsync(pixels);
            }
            var inicial = await PixelsAsync(projeto, "inicial.rgb");
            Assert.InRange(inicial[105 * 3], (byte)90, (byte)170);
            var alterado = projeto with { LinhaDoTempo = [primeiro, segundo with { FadeOutVideo = Segundos(2) }] };
            var atualizado = await PixelsAsync(alterado, "atualizado.rgb");
            Assert.Equal(1, preparador.UltimoDiagnostico!.VideosRenderizados);
            Assert.Equal(0, preparador.UltimoDiagnostico.AudiosRenderizados);
            Assert.True(atualizado[105 * 3] < inicial[105 * 3] - 30);
            Assert.InRange(atualizado[45 * 3], (byte)240, (byte)255);
        }
        finally { Directory.Delete(pasta, true); }
    }

    private static async Task<(byte R, byte G, byte B)> PixelRgbAsync(string pasta, string nome, byte[] png)
    {
        var entrada = Path.Combine(pasta, nome);
        var saida = entrada + ".rgb";
        await File.WriteAllBytesAsync(entrada, png);
        await Ffmpeg("-i", entrada, "-vf", "scale=1:1", "-pix_fmt", "rgb24", "-f", "rawvideo", saida);
        var bytes = await File.ReadAllBytesAsync(saida);
        return (bytes[0], bytes[1], bytes[2]);
    }

    private static async Task Ffmpeg(params string[] argumentos)
    {
        var inicio = new ProcessStartInfo(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        inicio.ArgumentList.Add("-y");
        foreach (var argumento in argumentos) inicio.ArgumentList.Add(argumento);
        using var processo = Process.Start(inicio)!;
        var erro = await processo.StandardError.ReadToEndAsync();
        await processo.WaitForExitAsync();
        if (processo.ExitCode != 0) throw new InvalidOperationException(erro);
    }
}
