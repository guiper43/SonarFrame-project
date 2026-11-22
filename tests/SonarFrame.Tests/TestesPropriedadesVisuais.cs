using System.Drawing;
using System.IO;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Tests;

public sealed class TestesPropriedadesVisuais
{
    [Fact]
    public void LayoutVisualValidaNovePosicoesTamanhosPredefinidosPersonalizadosESubstituicao()
    {
        var video = new Midia(Guid.NewGuid(), "Vídeo", "video.mp4", TipoMidia.Video, TempoMidia.Segundos(10));
        var foto = new Midia(Guid.NewGuid(), "Foto", "foto.png", TipoMidia.Imagem, null);
        var audio = new Midia(Guid.NewGuid(), "Áudio", "audio.wav", TipoMidia.Audio, TempoMidia.Segundos(10));
        var baseProjeto = Projeto.Criar("Propriedades Visuais") with
        {
            Biblioteca = [video, foto, audio],
            LinhaDoTempo = [new(Guid.NewGuid(), video.Id, video.Nome, TempoMidia.Zero, TempoMidia.Segundos(10))]
        };

        var posicoes = Enum.GetValues<PosicaoVisual>();
        Assert.Equal(9, posicoes.Length);

        foreach (var posicao in posicoes)
        {
            foreach (var escala in new[] { 25, 50, 75, 5, 35, 100 })
            {
                var layout = new LayoutVisual(ModoExibicaoVisual.Sobrepor, posicao, escala, AjusteVisual.PermitirBordas);
                var atualizado = new AdicionarCamada(foto.Id, TempoMidia.Segundos(1), TempoMidia.Zero, TempoMidia.Segundos(2), layout)
                    .Aplicar(baseProjeto)
                    .Validar();
                Assert.Equal(layout, atualizado.Camadas[0].Clipe.Layout);
            }
        }

        Assert.Equal(TamanhoVisual.Pequeno, new LayoutVisual(EscalaPercentual: 25).Tamanho);
        Assert.Equal(TamanhoVisual.Medio, new LayoutVisual(EscalaPercentual: 50).Tamanho);
        Assert.Equal(TamanhoVisual.Grande, new LayoutVisual(EscalaPercentual: 75).Tamanho);
        Assert.Equal(TamanhoVisual.Personalizado, new LayoutVisual(EscalaPercentual: 40).Tamanho);

        foreach (var ajuste in new[] { AjusteVisual.PermitirBordas, AjusteVisual.PreencherInteiro })
        {
            var layoutSubstituir = new LayoutVisual(ModoExibicaoVisual.Substituir, PosicaoVisual.Centro, 100, ajuste);
            var comCamada = new AdicionarCamada(foto.Id, TempoMidia.Segundos(2), TempoMidia.Zero, TempoMidia.Segundos(3), layoutSubstituir)
                .Aplicar(baseProjeto)
                .Validar();
            Assert.Equal(layoutSubstituir, comCamada.Camadas[0].Clipe.Layout);

            var comSequencia = new InserirClipe(foto.Id, 1, TempoMidia.Segundos(4), layoutSubstituir)
                .Aplicar(baseProjeto)
                .Validar();
            Assert.Equal(layoutSubstituir, comSequencia.LinhaDoTempo[1].Layout);
        }

        foreach (var escalaInvalida in new[] { 0, 4, 33, 101, 105, -10 })
        {
            var invalido = new LayoutVisual(ModoExibicaoVisual.Sobrepor, PosicaoVisual.Centro, escalaInvalida);
            Assert.Throws<ArgumentException>(() =>
                new AdicionarCamada(foto.Id, TempoMidia.Zero, TempoMidia.Zero, TempoMidia.Segundos(2), invalido)
                    .Aplicar(baseProjeto)
                    .Validar());
        }

        Assert.Throws<ArgumentException>(() =>
            new AdicionarCamada(audio.Id, TempoMidia.Zero, TempoMidia.Zero, TempoMidia.Segundos(2), LayoutVisual.PadraoCamada)
                .Aplicar(baseProjeto)
                .Validar());

        Assert.Throws<ArgumentException>(() =>
            new InserirClipe(foto.Id, 1, TempoMidia.Segundos(3), new LayoutVisual(ModoExibicaoVisual.Sobrepor))
                .Aplicar(baseProjeto)
                .Validar());

        var projetoFoto = new InserirClipe(foto.Id, 0, TempoMidia.Segundos(10)).Aplicar(Projeto.Criar("Seleção Reversa") with { Biblioteca = [foto] });
        var comSelecaoReversa = projetoFoto with
        {
            Cursor = TempoMidia.Segundos(2),
            Selecao = new SelecaoTemporal(TempoMidia.Segundos(8), TempoMidia.Segundos(2))
        };
        var reduzido = new DefinirLayoutVisual(
            comSelecaoReversa.LinhaDoTempo[0].Id,
            new LayoutVisual(ModoExibicaoVisual.Substituir, PosicaoVisual.Centro, 100, AjusteVisual.PreencherInteiro),
            TempoMidia.Segundos(6)).Aplicar(comSelecaoReversa).Validar();
        Assert.Equal(new SelecaoTemporal(TempoMidia.Segundos(6), TempoMidia.Segundos(2)), reduzido.Selecao);

        var layoutOverlay = new LayoutVisual(ModoExibicaoVisual.Sobrepor, PosicaoVisual.InferiorDireita, 35);
        var comCamadaOverlay = new AdicionarCamada(foto.Id, TempoMidia.Segundos(1), TempoMidia.Zero, TempoMidia.Segundos(4), layoutOverlay)
            .Aplicar(reduzido)
            .Validar();
        var dividido = new DividirClipe(comCamadaOverlay.LinhaDoTempo[0].Id, TempoMidia.Segundos(3)).Aplicar(comCamadaOverlay).Validar();
        Assert.Equal(2, dividido.LinhaDoTempo.Length);
        Assert.All(dividido.LinhaDoTempo, c => Assert.Equal(AjusteVisual.PreencherInteiro, c.Layout!.Ajuste));

        var recortado = new ExcluirIntervalo(TempoMidia.Segundos(2), TempoMidia.Segundos(4)).Aplicar(dividido).Validar();
        Assert.All(recortado.LinhaDoTempo, c => Assert.Equal(AjusteVisual.PreencherInteiro, c.Layout!.Ajuste));
        Assert.Equal(2, recortado.Camadas.Length);
        Assert.All(recortado.Camadas, c => Assert.Equal(layoutOverlay, c.Clipe.Layout));
    }

    [Fact]
    public void EncurtarImagemNasPropriedadesVisuaisAjustaFades()
    {
        var foto = new Midia(Guid.NewGuid(), "Foto", "foto.png", TipoMidia.Imagem, null);
        var clipe = new Clipe(Guid.NewGuid(), foto.Id, foto.Nome, TempoMidia.Zero, TempoMidia.Segundos(6),
            FadeInVideo: TempoMidia.Segundos(2), FadeOutVideo: TempoMidia.Segundos(3));
        var projeto = (Projeto.Criar("Imagem com fades") with { Biblioteca = [foto], LinhaDoTempo = [clipe] }).Validar();
        var layout = LayoutVisual.PadraoSequencia with { Ajuste = AjusteVisual.PreencherInteiro };

        var resultado = new DefinirLayoutVisual(clipe.Id, layout, TempoMidia.Segundos(3)).Aplicar(projeto).Validar();
        var alterado = Assert.Single(resultado.LinhaDoTempo);
        Assert.Equal(layout, alterado.Layout);
        Assert.Equal(TempoMidia.Segundos(2), alterado.FadeInVideo);
        Assert.Equal(TempoMidia.Segundos(1), alterado.FadeOutVideo);
    }

    [Fact]
    public async Task PersistenciaSqlitePreservaPropriedadesVisuaisEComandosDesfazemERefazem()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-prop-visuais-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var caminho = Path.Combine(pasta, "projeto.sonarframe");
            var video = new Midia(Guid.NewGuid(), "Vídeo", "video.mp4", TipoMidia.Video, TempoMidia.Segundos(10));
            var foto = new Midia(Guid.NewGuid(), "Logo", "logo.png", TipoMidia.Imagem, null);
            var sessao = new SessaoEdicao(Projeto.Criar("Persistência Visual"));
            sessao.Executar(new RegistrarMidia(video));
            sessao.Executar(new RegistrarMidia(foto));
            sessao.Executar(new InserirClipe(video.Id, 0));
            var layoutSequencia = new LayoutVisual(ModoExibicaoVisual.Substituir, PosicaoVisual.Centro, 100, AjusteVisual.PreencherInteiro);
            sessao.Executar(new InserirClipe(foto.Id, 1, TempoMidia.Segundos(4), layoutSequencia));

            var layoutCamada = new LayoutVisual(ModoExibicaoVisual.Sobrepor, PosicaoVisual.SuperiorDireita, 25, AjusteVisual.PermitirBordas);
            sessao.Executar(new AdicionarCamada(foto.Id, TempoMidia.Segundos(1), TempoMidia.Zero, TempoMidia.Segundos(3), layoutCamada));
            var camadaId = sessao.Atual.Camadas[0].Clipe.Id;

            var repositorio = new RepositorioProjetosSqlite();
            await repositorio.SalvarAsync(caminho, sessao.Atual);
            var carregado = await repositorio.CarregarAsync(caminho);

            Assert.Equal(layoutSequencia, carregado.LinhaDoTempo[1].Layout);
            Assert.Equal(layoutCamada, carregado.Camadas[0].Clipe.Layout);

            var novoLayout = new LayoutVisual(ModoExibicaoVisual.Sobrepor, PosicaoVisual.InferiorEsquerda, 65, AjusteVisual.PermitirBordas);
            sessao.Executar(new AtualizarCamada(camadaId, TempoMidia.Segundos(2), TempoMidia.Zero, TempoMidia.Segundos(4), novoLayout));
            Assert.Equal(novoLayout, sessao.Atual.Camadas[0].Clipe.Layout);

            sessao.Desfazer();
            Assert.Equal(layoutCamada, sessao.Atual.Camadas[0].Clipe.Layout);
            sessao.Refazer();
            Assert.Equal(novoLayout, sessao.Atual.Camadas[0].Clipe.Layout);
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public void ConflitosVisuaisDetectamSobreposicaoNaMesmaRegiaoSemBloquear()
    {
        var video = new Midia(Guid.NewGuid(), "Vídeo", "video.mp4", TipoMidia.Video, TempoMidia.Segundos(10));
        var foto1 = new Midia(Guid.NewGuid(), "Selo 1", "selo1.png", TipoMidia.Imagem, null);
        var foto2 = new Midia(Guid.NewGuid(), "Selo 2", "selo2.png", TipoMidia.Imagem, null);
        var sessao = new SessaoEdicao(Projeto.Criar("Conflitos"));
        sessao.Executar(new RegistrarMidia(video));
        sessao.Executar(new RegistrarMidia(foto1));
        sessao.Executar(new RegistrarMidia(foto2));
        sessao.Executar(new InserirClipe(video.Id, 0));

        var superiorEsquerda = new LayoutVisual(ModoExibicaoVisual.Sobrepor, PosicaoVisual.SuperiorEsquerda, 25);
        var inferiorDireita = new LayoutVisual(ModoExibicaoVisual.Sobrepor, PosicaoVisual.InferiorDireita, 25);
        var substituir = new LayoutVisual(ModoExibicaoVisual.Substituir, PosicaoVisual.Centro, 100, AjusteVisual.PermitirBordas);

        sessao.Executar(new AdicionarCamada(foto1.Id, TempoMidia.Segundos(2), TempoMidia.Zero, TempoMidia.Segundos(4), superiorEsquerda));
        var acoes = new AcoesEdicao(sessao);

        Assert.Equal(new[] { "Selo 1" }, acoes.ConflitosVisuais(TempoMidia.Segundos(3), TempoMidia.Segundos(2), superiorEsquerda).ToArray());
        Assert.Empty(acoes.ConflitosVisuais(TempoMidia.Segundos(3), TempoMidia.Segundos(2), inferiorDireita));
        Assert.Empty(acoes.ConflitosVisuais(TempoMidia.Segundos(6), TempoMidia.Segundos(2), superiorEsquerda));
        Assert.Equal(new[] { "Selo 1" }, acoes.ConflitosVisuais(TempoMidia.Segundos(3), TempoMidia.Segundos(2), substituir).ToArray());
    }

    [MidiasReaisFact]
    public async Task RenderizacaoRespeitaPosicaoTamanhoSubstituicaoEPreservaAudioECache()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-render-visual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var video = Path.Combine(pasta, "base.mp4");
            var verde = Path.Combine(pasta, "verde.png");
            var quadradoAzul = Path.Combine(pasta, "quadrado-azul.png");
            await TestesPreparacaoComposicao.Ffmpeg(
                "-f", "lavfi", "-i", "color=red:s=160x90:r=30:d=4",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", video);
            await TestesPreparacaoComposicao.Ffmpeg("-f", "lavfi", "-i", "color=lime:s=160x90", "-frames:v", "1", verde);
            await TestesPreparacaoComposicao.Ffmpeg("-f", "lavfi", "-i", "color=blue:s=90x90", "-frames:v", "1", quadradoAzul);

            var mVideo = new Midia(Guid.NewGuid(), "Base", video, TipoMidia.Video, TempoMidia.Segundos(4), new(30, 1));
            var mVerde = new Midia(Guid.NewGuid(), "Verde", verde, TipoMidia.Imagem, null);
            var mAzul = new Midia(Guid.NewGuid(), "Azul", quadradoAzul, TipoMidia.Imagem, null);

            var layoutSupEsq25 = new LayoutVisual(ModoExibicaoVisual.Sobrepor, PosicaoVisual.SuperiorEsquerda, 25);
            var projeto = Projeto.Criar("Renderização Visual") with
            {
                Biblioteca = [mVideo, mVerde, mAzul],
                LinhaDoTempo = [new(Guid.NewGuid(), mVideo.Id, mVideo.Nome, TempoMidia.Zero, TempoMidia.Segundos(4))],
                Camadas = [new(new(Guid.NewGuid(), mVerde.Id, mVerde.Nome, TempoMidia.Zero, TempoMidia.Segundos(2), Layout: layoutSupEsq25), TempoMidia.Segundos(1))]
            };

            var probe = new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!);
            await using var preparador = new PreparadorComposicaoFfmpeg(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!, probe);

            var quadroSupEsq = await preparador.ObterQuadroAsync(projeto, new TempoMidia(3, 2), CancellationToken.None);
            Assert.NotNull(quadroSupEsq);
            var (rTopoEsq, gTopoEsq, _) = await LerPixelPngAsync(pasta, quadroSupEsq!.Png, 10, 10);
            var (rBaseDir, gBaseDir, _) = await LerPixelPngAsync(pasta, quadroSupEsq.Png, 620, 340);
            Assert.True(gTopoEsq > 180 && rTopoEsq < 60);
            Assert.True(rBaseDir > 180 && gBaseDir < 60);

            var previa1 = await preparador.PrepararAsync(projeto, null, CancellationToken.None);
            var projetoComLayoutPadraoSequencia = new DefinirLayoutVisual(projeto.LinhaDoTempo[0].Id, LayoutVisual.PadraoSequencia).Aplicar(projeto);
            await preparador.PrepararAsync(projetoComLayoutPadraoSequencia, null, CancellationToken.None);
            Assert.Equal(0, preparador.UltimoDiagnostico!.VideosRenderizados);
            Assert.Equal(0, preparador.UltimoDiagnostico.AudiosRenderizados);

            var layoutInfDir50 = new LayoutVisual(ModoExibicaoVisual.Sobrepor, PosicaoVisual.InferiorDireita, 50);
            var projetoInfDir = new DefinirLayoutVisual(projeto.Camadas[0].Clipe.Id, layoutInfDir50).Aplicar(projeto);
            await preparador.PrepararAsync(projetoInfDir, null, CancellationToken.None);
            Assert.Equal(1, preparador.UltimoDiagnostico!.VideosRenderizados);
            Assert.Equal(0, preparador.UltimoDiagnostico.AudiosRenderizados);

            var quadroInfDir = await preparador.ObterQuadroAsync(projetoInfDir, new TempoMidia(3, 2), CancellationToken.None);
            var (rTopoEsq2, gTopoEsq2, _) = await LerPixelPngAsync(pasta, quadroInfDir!.Png, 10, 10);
            var (rBaseDir2, gBaseDir2, _) = await LerPixelPngAsync(pasta, quadroInfDir.Png, 620, 340);
            Assert.True(rTopoEsq2 > 180 && gTopoEsq2 < 60);
            Assert.True(gBaseDir2 > 180 && rBaseDir2 < 60);

            var layoutBordas = new LayoutVisual(ModoExibicaoVisual.Substituir, PosicaoVisual.Centro, 100, AjusteVisual.PermitirBordas);
            var projetoBordas = projeto with
            {
                Camadas = [new(new(Guid.NewGuid(), mAzul.Id, mAzul.Nome, TempoMidia.Zero, TempoMidia.Segundos(2), Layout: layoutBordas), TempoMidia.Segundos(1))]
            };
            var quadroBordas = await preparador.ObterQuadroAsync(projetoBordas, new TempoMidia(3, 2), CancellationToken.None);
            var (rLateralBordas, gLateralBordas, bLateralBordas) = await LerPixelPngAsync(pasta, quadroBordas!.Png, 20, 180);
            var (rCentroBordas, _, bCentroBordas) = await LerPixelPngAsync(pasta, quadroBordas.Png, 320, 180);
            Assert.True(rLateralBordas < 30 && gLateralBordas < 30 && bLateralBordas < 30);
            Assert.True(bCentroBordas > 180 && rCentroBordas < 60);

            var layoutPreencher = new LayoutVisual(ModoExibicaoVisual.Substituir, PosicaoVisual.Centro, 100, AjusteVisual.PreencherInteiro);
            var projetoPreencher = new DefinirLayoutVisual(projetoBordas.Camadas[0].Clipe.Id, layoutPreencher).Aplicar(projetoBordas);
            var quadroPreencher = await preparador.ObterQuadroAsync(projetoPreencher, new TempoMidia(3, 2), CancellationToken.None);
            var (rLateralPreencher, _, bLateralPreencher) = await LerPixelPngAsync(pasta, quadroPreencher!.Png, 20, 180);
            Assert.True(bLateralPreencher > 180 && rLateralPreencher < 60);

            var previaSubstituir = await preparador.PrepararAsync(projetoPreencher, null, CancellationToken.None);
            var som = Path.Combine(pasta, "som.pcm");
            await TestesPreparacaoComposicao.Ffmpeg("-i", previaSubstituir.Caminho, "-vn", "-ac", "1", "-ar", "48000", "-f", "s16le", som);
            var pcm = await File.ReadAllBytesAsync(som);
            var nivelDuranteSubstituicao = Enumerable.Range(72000, 24000).Average(i => Math.Abs((double)BitConverter.ToInt16(pcm, i * 2)));
            Assert.True(nivelDuranteSubstituicao > 500);

            var semCamadas = projeto with { Camadas = [] };
            var dividido = new DividirClipe(semCamadas.LinhaDoTempo[0].Id, TempoMidia.Segundos(2)).Aplicar(semCamadas).Validar();
            await preparador.PrepararAsync(dividido, null, CancellationToken.None);
            var ajusteDoSegundoTrecho = new DefinirLayoutVisual(dividido.LinhaDoTempo[1].Id, layoutPreencher).Aplicar(dividido).Validar();
            await preparador.PrepararAsync(ajusteDoSegundoTrecho, null, CancellationToken.None);
            Assert.Equal(0, preparador.UltimoDiagnostico!.AudiosRenderizados);
            Assert.Equal(1, preparador.UltimoDiagnostico.VideosRenderizados);

            var volumeDoSegundoTrecho = ajusteDoSegundoTrecho with
            {
                LinhaDoTempo = ajusteDoSegundoTrecho.LinhaDoTempo.SetItem(1, ajusteDoSegundoTrecho.LinhaDoTempo[1] with { Volume = 50 })
            };
            await preparador.PrepararAsync(volumeDoSegundoTrecho.Validar(), null, CancellationToken.None);
            Assert.Equal(1, preparador.UltimoDiagnostico!.AudiosRenderizados);
            Assert.Equal(0, preparador.UltimoDiagnostico.VideosRenderizados);
        }
        finally { Directory.Delete(pasta, true); }
    }

    private static async Task<(byte R, byte G, byte B)> LerPixelPngAsync(string pasta, byte[] png, int x, int y)
    {
        var entrada = Path.Combine(pasta, Guid.NewGuid().ToString("N") + ".png");
        var saida = Path.Combine(pasta, Guid.NewGuid().ToString("N") + ".rgb");
        await File.WriteAllBytesAsync(entrada, png);
        await TestesPreparacaoComposicao.Ffmpeg("-i", entrada, "-vf", $"crop=1:1:{x}:{y}", "-pix_fmt", "rgb24", "-f", "rawvideo", saida);
        var bytes = await File.ReadAllBytesAsync(saida);
        return (bytes[0], bytes[1], bytes[2]);
    }
}
