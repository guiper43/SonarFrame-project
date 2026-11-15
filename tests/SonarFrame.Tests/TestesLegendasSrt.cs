using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;
using static SonarFrame.Tests.TestesEdicao;

namespace SonarFrame.Tests;

public sealed class TestesLegendasSrt
{
    [Fact]
    public async Task LeitorSrtCarregaBlocosValidaFormatosERejeitaEntradasInvalidas()
    {
        const string srt = "\uFEFF1\r\n00:00:00,500 --> 00:00:02,500\r\nOlá, mundo!\r\nSegunda linha.\r\n\r\n2\n00:00:03.000 --> 00:00:05.200\n<b>Texto em destaque</b>\n\n3\n00:00:06,000 --> 00:00:08,000\n<i>Texto em itálico</i>\n\n4\n00:00:08,500 --> 00:00:09,500\n<b><i>Ambos</i></b>\n\n5\n00:00:09,600 --> 00:00:10,000\n<b>Aviso:</b> texto normal <b>fim</b>\n";
        var legendas = LeitorSrt.Ler(srt);
        Assert.Equal(5, legendas.Length);

        Assert.Equal(new TempoMidia(500, 1000), legendas[0].Inicio);
        Assert.Equal(new TempoMidia(2500, 1000), legendas[0].Fim);
        Assert.Equal("Olá, mundo!\nSegunda linha.", legendas[0].Texto);
        Assert.Equal(EstiloLegenda.Padrao, legendas[0].Estilo);
        Assert.Equal(PosicaoVisual.InferiorCentro, legendas[0].Estilo.Posicao);

        Assert.Equal("Texto em destaque", legendas[1].Texto);
        Assert.True(legendas[1].Estilo.Negrito);
        Assert.False(legendas[1].Estilo.Italico);

        Assert.Equal("Texto em itálico", legendas[2].Texto);
        Assert.False(legendas[2].Estilo.Negrito);
        Assert.True(legendas[2].Estilo.Italico);

        Assert.Equal("Ambos", legendas[3].Texto);
        Assert.True(legendas[3].Estilo.Negrito);
        Assert.True(legendas[3].Estilo.Italico);

        Assert.Equal("Aviso: texto normal fim", legendas[4].Texto);
        Assert.False(legendas[4].Estilo.Negrito);
        Assert.False(legendas[4].Estilo.Italico);

        Assert.Throws<InvalidDataException>(() => LeitorSrt.Ler("   "));
        Assert.Throws<InvalidDataException>(() => LeitorSrt.Ler("1\n00:00:05,000 --> 00:00:02,000\nInvertido"));
        Assert.Throws<InvalidDataException>(() => LeitorSrt.Ler("1\ntempo invalido\nTexto"));

        var pasta = Path.Combine(AppContext.BaseDirectory, "test-srt", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var arquivoSrt = Path.Combine(pasta, "legendas.srt");
            await File.WriteAllTextAsync(arquivoSrt, srt, Encoding.UTF8);
            var doDisco = await LeitorSrt.CarregarAsync(arquivoSrt);
            Assert.Equal(5, doDisco.Length);

            var arquivoTxt = Path.Combine(pasta, "invalido.txt");
            await File.WriteAllTextAsync(arquivoTxt, srt, Encoding.UTF8);
            await Assert.ThrowsAsync<InvalidDataException>(() => LeitorSrt.CarregarAsync(arquivoTxt));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public async Task EditarLegendaEscopoIndividualGlobalExcluirIntervaloEPersistenciaFuncionam()
    {
        var diretorio = Path.Combine(AppContext.BaseDirectory, "test-legendas-core", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(diretorio);
        try
        {
            var sessao = Sessao();
            var acoes = new AcoesEdicao(sessao);
            var l1 = new Legenda(Guid.NewGuid(), Segundos(0), Segundos(3), "Primeira", EstiloLegenda.Padrao);
            var l2 = new Legenda(Guid.NewGuid(), Segundos(4), Segundos(8), "Segunda", EstiloLegenda.Padrao);
            acoes.DefinirLegendas([l2, l1]);

            Assert.Equal(2, sessao.Atual.Legendas.Length);
            Assert.Equal("Primeira", sessao.Atual.Legendas[0].Texto);

            sessao.Posicionar(Segundos(1));
            Assert.Equal(l1.Id, acoes.LegendaAtiva()?.Id);

            var estiloIndividual = new EstiloLegenda("Verdana", 36, "#FFFF00", Negrito: true, Italico: false, Fundo: false, PosicaoVisual.SuperiorCentro);
            acoes.EditarLegenda(l1.Id, "Primeira editada", estiloIndividual, EscopoEstiloLegenda.ApenasSelecionada);
            Assert.Equal("Primeira editada", sessao.Atual.Legendas[0].Texto);
            Assert.Equal(estiloIndividual, sessao.Atual.Legendas[0].Estilo);
            Assert.Equal(EstiloLegenda.Padrao, sessao.Atual.Legendas[1].Estilo);

            var estiloGlobal = new EstiloLegenda("Tahoma", 32, "#00FFFF", Negrito: false, Italico: true, Fundo: true, PosicaoVisual.SuperiorDireita);
            acoes.EditarLegenda(l1.Id, "Primeira global", estiloGlobal, EscopoEstiloLegenda.TodasDoProjeto);
            Assert.Equal("Primeira global", sessao.Atual.Legendas[0].Texto);
            Assert.Equal("Segunda", sessao.Atual.Legendas[1].Texto);
            Assert.Equal(estiloGlobal, sessao.Atual.Legendas[0].Estilo);
            Assert.Equal(estiloGlobal, sessao.Atual.Legendas[1].Estilo);

            foreach (var posicao in Enum.GetValues<PosicaoVisual>())
            {
                acoes.EditarLegenda(l1.Id, "Posição", estiloGlobal with { Posicao = posicao }, EscopoEstiloLegenda.ApenasSelecionada);
                Assert.Equal(posicao, sessao.Atual.Legendas[0].Estilo.Posicao);
            }

            Assert.Throws<ArgumentException>(() => sessao.Executar(new EditarLegenda(l1.Id, "", estiloGlobal)));
            Assert.Throws<ArgumentException>(() => sessao.Executar(new EditarLegenda(l1.Id, "Ok", estiloGlobal with { Tamanho = 4 })));
            Assert.Throws<ArgumentException>(() => sessao.Executar(new EditarLegenda(l1.Id, "Ok", estiloGlobal with { Cor = "azul" })));
            Assert.Throws<ArgumentException>(() => sessao.Executar(new EditarLegenda(l1.Id, "Ok", estiloGlobal with { Fonte = "Arial\nInjecao" })));

            sessao.Executar(new ExcluirIntervalo(Segundos(2), Segundos(5)));
            Assert.Equal(2, sessao.Atual.Legendas.Length);
            Assert.Equal(Segundos(0), sessao.Atual.Legendas[0].Inicio);
            Assert.Equal(Segundos(2), sessao.Atual.Legendas[0].Fim);
            Assert.Equal(Segundos(2), sessao.Atual.Legendas[1].Inicio);
            Assert.Equal(Segundos(5), sessao.Atual.Legendas[1].Fim);

            sessao.Desfazer();
            Assert.Equal(Segundos(3), sessao.Atual.Legendas[0].Fim);
            Assert.Equal(Segundos(4), sessao.Atual.Legendas[1].Inicio);

            var caminho = Path.Combine(diretorio, "legendas.sonarframe");
            var repositorio = new RepositorioProjetosSqlite();
            await repositorio.SalvarAsync(caminho, sessao.Atual);
            var reaberto = await repositorio.CarregarAsync(caminho);
            Assert.Equal(JsonSerializer.Serialize(sessao.Atual), JsonSerializer.Serialize(reaberto));
        }
        finally { Directory.Delete(diretorio, true); }
    }

    [MidiasReaisFact]
    public async Task LegendasRenderizamNosQuadrosPreviaEExportacaoMp4SemInvalidarCacheDeAudio()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "sonarframe-legendas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var video = Path.Combine(pasta, "preto.mp4");
            await Ffmpeg("-f", "lavfi", "-i", "color=black:s=320x180:r=30:d=4",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", video);

            var midia = new Midia(Guid.NewGuid(), "Base", video, TipoMidia.Video, Segundos(4), new(30, 1));
            var clipe = new Clipe(Guid.NewGuid(), midia.Id, midia.Nome, TempoMidia.Zero, Segundos(4), 100);
            var projetoSemLegenda = Projeto.Criar("Legendas Reais") with
            {
                Biblioteca = [midia],
                LinhaDoTempo = [clipe]
            };

            var probe = new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!);
            var ffmpeg = Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!;
            await using var preparador = new PreparadorComposicaoFfmpeg(ffmpeg, probe);

            await preparador.PrepararAsync(projetoSemLegenda, null, CancellationToken.None);
            Assert.Equal(1, preparador.UltimoDiagnostico!.VideosRenderizados);
            Assert.Equal(1, preparador.UltimoDiagnostico.AudiosRenderizados);

            var legenda = new Legenda(Guid.NewGuid(), TempoMidia.Zero, Segundos(3), "██████████",
                new EstiloLegenda("Arial", 72, "#FFFF00", Negrito: true, Italico: false, Fundo: true, PosicaoVisual.Centro));
            var projetoComLegenda = projetoSemLegenda with { Legendas = [legenda] };

            var previa = await preparador.PrepararAsync(projetoComLegenda, null, CancellationToken.None);
            Assert.Equal(1, preparador.UltimoDiagnostico!.VideosRenderizados);
            Assert.Equal(0, preparador.UltimoDiagnostico.AudiosRenderizados);
            Assert.True(File.Exists(previa.Caminho));

            var quadroComLegenda = await preparador.ObterQuadroAsync(projetoComLegenda, Segundos(1), CancellationToken.None);
            var quadroSemLegenda = await preparador.ObterQuadroAsync(projetoComLegenda, new TempoMidia(35, 10), CancellationToken.None);
            Assert.NotNull(quadroComLegenda);
            Assert.NotNull(quadroSemLegenda);

            var maxComLegenda = await MaximoCanalAsync(pasta, "q-com.png", quadroComLegenda!.Png);
            var maxSemLegenda = await MaximoCanalAsync(pasta, "q-sem.png", quadroSemLegenda!.Png);
            Assert.True(maxComLegenda > 150, $"Esperado pixels claros da legenda no quadro em 1s, obtido max={maxComLegenda}");
            Assert.True(maxSemLegenda < 20, $"Esperado quadro escuro em 3.5s após o fim da legenda, obtido max={maxSemLegenda}");

            var mp4 = Path.Combine(pasta, "exportado.mp4");
            await using var exportador = new ExportadorFfmpeg(ffmpeg, probe);
            await exportador.ExportarAsync(projetoComLegenda, new(mp4, Orientacao.Horizontal, ModoEnquadramento.Letterbox, 720), new Progress<ProgressoOperacao>(), CancellationToken.None);
            Assert.True(File.Exists(mp4));
        }
        finally { Directory.Delete(pasta, true); }
    }

    private static async Task<byte> MaximoCanalAsync(string pasta, string nome, byte[] png)
    {
        var entrada = Path.Combine(pasta, nome);
        var saida = entrada + ".rgb";
        await File.WriteAllBytesAsync(entrada, png);
        await Ffmpeg("-i", entrada, "-pix_fmt", "rgb24", "-f", "rawvideo", saida);
        var bytes = await File.ReadAllBytesAsync(saida);
        return bytes.Max();
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
