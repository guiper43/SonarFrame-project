using System.Diagnostics;
using System.IO;
using System.Text.Json;
using LibVLCSharp.WPF;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class BenchmarkLatenciaFactAttribute : FactAttribute
{
    public BenchmarkLatenciaFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SONARFRAME_BENCHMARK_LATENCIA")))
            Skip = "Execute medir-latencia.ps1 para medir a saída nativa.";
    }
}

public sealed class BenchmarkLatencia
{
    [BenchmarkLatenciaFact]
    public async Task MedirSaidaNativa()
    {
        var pasta = Environment.GetEnvironmentVariable("SONARFRAME_BENCHMARK_LATENCIA")!;
        var etapa = Environment.GetEnvironmentVariable("SONARFRAME_BENCHMARK_ETAPA")!;
        var ferramentas = Path.Combine(AppContext.BaseDirectory, "tools");
        var probe = new ExecutorFfprobe(Path.Combine(ferramentas, "ffprobe.exe"));
        var leitor = new LeitorMetadadosFfprobe(probe);
        var resultados = new List<object>();
        foreach (var segundos in new[] { 30, 180, 600 })
        foreach (var tipo in new[] { "cfr", "vfr" })
        {
            var midia = await leitor.LerAsync(Path.Combine(pasta, tipo + ".mp4"), CancellationToken.None);
            var audio = await leitor.LerAsync(Path.Combine(pasta, "audio.wav"), CancellationToken.None);
            var imagem = await leitor.LerAsync(Path.Combine(pasta, "imagem.png"), CancellationToken.None);
            await using var preparador = new PreparadorComposicaoFfmpeg(Path.Combine(ferramentas, "ffmpeg.exe"), probe);
            await TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
            {
                janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
                var original = Projeto.Criar("Latência") with
                {
                    Biblioteca = [midia, audio, imagem],
                    LinhaDoTempo = [.. Enumerable.Range(0, 3).Select(i => new Clipe(Guid.NewGuid(), midia.Id, "Clipe " + i,
                        TempoMidia.Segundos(i * segundos / 3), TempoMidia.Segundos(segundos / 3)))]
                };
                async Task Medir(string cenario, Projeto projeto)
                {
                    if (cenario != "busca-distante") projeto = projeto with { Cursor = TempoMidia.Zero };
                    await janela.LimparPreviaAsync();
                    janela.ExibirProjeto(projeto, salvo: true, caminho: caminho);
                    var relogio = Stopwatch.StartNew();
                    var inicio = Stopwatch.GetTimestamp();
                    await janela.ReproduzirComposicaoAsync(false);
                    var comandoMs = relogio.Elapsed.TotalMilliseconds;
                    Assert.Equal(EstadoPrevia.Reproduzindo, janela.PreviaAtual.Atual.Estado);
                    Assert.IsType<VideoView>(janela.SuperficiePrevia.Content);
                    var saida = await janela.SaidaNativa!;
                    Assert.True(saida.PrimeiroQuadro.HasValue, $"{segundos}/{tipo}/{cenario}: nenhum quadro confirmado pelo motor.");
                    Assert.True(saida.PrimeiroAudio.HasValue, $"{segundos}/{tipo}/{cenario}: nenhum buffer de áudio confirmado pelo motor.");
                    if (cenario is "segunda" or "divisao" or "busca-distante")
                        Assert.Equal(0, preparador.UltimoDiagnostico!.BlocosRenderizados);
                    var quadroMs = Stopwatch.GetElapsedTime(inicio, saida.PrimeiroQuadro.Value).TotalMilliseconds;
                    var audioMs = Stopwatch.GetElapsedTime(inicio, saida.PrimeiroAudio.Value).TotalMilliseconds;
                    resultados.Add(new { Segundos = segundos, Tipo = tipo, Cenario = cenario, ComandoMs = comandoMs,
                        PrimeiroQuadroMs = quadroMs, PrimeiroAudioMs = audioMs, saida.InicializacaoLibVlcMs, Diagnostico = preparador.UltimoDiagnostico });
                    await File.WriteAllTextAsync(Path.Combine(pasta, etapa + ".json"), JsonSerializer.Serialize(resultados, new JsonSerializerOptions { WriteIndented = true }));
                    await janela.LimparPreviaAsync();
                }
                await Medir("primeira", original);
                await Medir("segunda", original);
                await Medir("divisao", new DividirClipe(original.LinhaDoTempo[0].Id, TempoMidia.Segundos(3)).Aplicar(original));
                foreach (var inicio in new[] { 0, segundos / 2, segundos - 2 })
                {
                    await preparador.PrepararAsync(original, null, CancellationToken.None);
                    await Medir("corte-" + (inicio == 0 ? "inicio" : inicio == segundos / 2 ? "meio" : "fim"),
                        new ExcluirIntervalo(TempoMidia.Segundos(inicio), TempoMidia.Segundos(inicio + 1)).Aplicar(original));
                }
                await Medir("reordenacao", new MoverClipe(original.LinhaDoTempo[2].Id, 0).Aplicar(original));
                var dividido = new DividirClipe(original.LinhaDoTempo[0].Id, TempoMidia.Segundos(2)).Aplicar(original);
                await Medir("volume", new DefinirVolume(dividido.LinhaDoTempo[0].Id, 50).Aplicar(dividido));
                await Medir("camada-visual", new AdicionarCamada(imagem.Id, TempoMidia.Segundos(6), TempoMidia.Zero, TempoMidia.Segundos(2)).Aplicar(original));
                await Medir("camada-audio", new AdicionarCamada(audio.Id, TempoMidia.Segundos(6), TempoMidia.Zero, TempoMidia.Segundos(2)).Aplicar(original));
                var sessao = new SessaoEdicao(original);
                sessao.Executar(new ExcluirIntervalo(TempoMidia.Zero, TempoMidia.Segundos(1)));
                await preparador.PrepararAsync(sessao.Atual, null, CancellationToken.None);
                sessao.Desfazer(); await Medir("desfazer", sessao.Atual);
                sessao.Refazer(); await Medir("refazer", sessao.Atual);
                sessao.Executar(new ExcluirIntervalo(TempoMidia.Zero, TempoMidia.Segundos(1)));
                sessao.Executar(new ExcluirIntervalo(TempoMidia.Zero, TempoMidia.Segundos(1)));
                await Medir("sucessivas", sessao.Atual);
                await preparador.PrepararAsync(original, null, CancellationToken.None);
                await Medir("busca-distante", original with { Cursor = TempoMidia.Segundos(segundos - 5) });
            }, prazoSegundos: 1800, preparador: preparador);
        }
    }
}
