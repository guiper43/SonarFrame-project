using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Tests;

public sealed class TestesReproducaoProgressiva
{
    [MidiasReaisFact]
    public async Task FluxoReutilizaBlocosEPreservaAudioEQuadros()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-fluxo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var caminho = Path.Combine(pasta, "origem.mp4");
            await TestesPreparacaoComposicao.Ffmpeg("-f", "lavfi", "-i", "testsrc2=s=160x90:r=30:d=12", "-f", "lavfi", "-i", "sine=frequency=443:sample_rate=48000:duration=12", "-vf", "select='if(lt(t,6),not(mod(n,2)),1)'", "-fps_mode", "vfr", "-c:v", "libx264", "-threads", "2", "-c:a", "aac", caminho);
            var midia = new Midia(Guid.NewGuid(), "Original", caminho, TipoMidia.Video, TempoMidia.Segundos(12));
            var projeto = Projeto.Criar("Fluxo") with { Biblioteca = [midia], LinhaDoTempo = [new(Guid.NewGuid(), midia.Id, "Inteiro", TempoMidia.Zero, TempoMidia.Segundos(12))] };
            projeto = new AdicionarCamada(midia.Id, TempoMidia.Segundos(4), TempoMidia.Segundos(1), TempoMidia.Segundos(3)).Aplicar(projeto);
            projeto = new ExcluirIntervalo(new(499, 100), new(501, 100)).Aplicar(projeto);
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!));
            var referencia = await preparador.PrepararAsync(projeto, null, CancellationToken.None);
            var hashReferencia = Path.Combine(pasta, "referencia.md5");
            await TestesPreparacaoComposicao.Ffmpeg("-i", referencia.Caminho, "-map", "0:v", "-f", "framemd5", hashReferencia);
            var audioReferencia = Path.Combine(pasta, "referencia.f32");
            await TestesPreparacaoComposicao.Ffmpeg("-i", referencia.Caminho, "-map", "0:a", "-c:a", "pcm_f32le", "-f", "f32le", audioReferencia);
            var destino = Path.Combine(pasta, "continua.nut");
            using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await using (var fonte = await preparador.IniciarReproducaoAsync(projeto, TempoMidia.Zero, projeto.Duracao, null, prazo.Token))
            {
                Assert.NotNull(fonte);
                fonte.AtualizarPosicao(projeto.Duracao);
                using var registro = prazo.Token.Register(fonte.Cancelar);
                await using var leitura = fonte.AbrirLeitura();
                await using var escrita = File.Create(destino);
                await Task.Run(() => leitura.CopyTo(escrita), prazo.Token);
                await fonte.Conclusao.WaitAsync(prazo.Token);
            }
            var hashFluxo = Path.Combine(pasta, "fluxo.md5");
            await TestesPreparacaoComposicao.Ffmpeg("-i", destino, "-map", "0:v", "-f", "framemd5", hashFluxo);
            var audioFluxo = Path.Combine(pasta, "fluxo.f32");
            await TestesPreparacaoComposicao.Ffmpeg("-i", destino, "-map", "0:a", "-c:a", "pcm_f32le", "-f", "f32le", audioFluxo);
            Assert.Equal(await File.ReadAllBytesAsync(audioReferencia), await File.ReadAllBytesAsync(audioFluxo));
            var esperado = (await File.ReadAllLinesAsync(hashReferencia)).Where(l => !l.StartsWith('#')).ToArray();
            var obtido = (await File.ReadAllLinesAsync(hashFluxo)).Where(l => !l.StartsWith('#')).ToArray();
            Assert.Equal(esperado, obtido);
            var audioSelecaoEsperado = Path.Combine(pasta, "selecao-esperada.f32");
            await TestesPreparacaoComposicao.Ffmpeg("-f", "f32le", "-ar", "48000", "-ac", "2", "-i", audioReferencia,
                "-af", "atrim=start=7.11:end=10.73", "-c:a", "pcm_f32le", "-f", "f32le", audioSelecaoEsperado);
            var selecao = Path.Combine(pasta, "selecao.nut");
            await using (var fonte = await preparador.IniciarReproducaoAsync(projeto, new(711, 100), new(1073, 100), null, prazo.Token))
            {
                Assert.NotNull(fonte);
                Assert.Equal(new TempoMidia(181, 50), fonte.Duracao);
                using var registro = prazo.Token.Register(fonte.Cancelar);
                await using var leitura = fonte.AbrirLeitura();
                await using var escrita = File.Create(selecao);
                await Task.Run(() => leitura.CopyTo(escrita), prazo.Token);
                await fonte.Conclusao.WaitAsync(prazo.Token);
            }
            var audioSelecao = Path.Combine(pasta, "selecao.f32");
            await TestesPreparacaoComposicao.Ffmpeg("-i", selecao, "-map", "0:a", "-c:a", "pcm_f32le", "-f", "f32le", audioSelecao);
            Assert.Equal(await File.ReadAllBytesAsync(audioSelecaoEsperado), await File.ReadAllBytesAsync(audioSelecao));
        }
        finally { Directory.Delete(pasta, true); }
    }
}
