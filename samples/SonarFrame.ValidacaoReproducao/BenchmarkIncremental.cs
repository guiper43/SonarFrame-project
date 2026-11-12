using System.Diagnostics;
using System.Text.Json;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;

internal static class BenchmarkIncremental
{
    internal static async Task ExecutarAsync(string raiz, string etapa, Func<string[], Task> executar, CancellationToken token)
    {
        var pasta = Path.Combine(raiz, ".tools", "test-artifacts", "incremental");
        Directory.CreateDirectory(pasta);
        var ffmpeg = Path.Combine(raiz, ".tools", "ffmpeg", "ffmpeg.exe");
        var probe = new ExecutorFfprobe(Path.Combine(raiz, ".tools", "ffmpeg", "ffprobe.exe"));
        var resultados = new List<object>();
        foreach (var tipo in new[] { "cfr", "vfr" })
        {
            var arquivo = Path.Combine(pasta, tipo + ".mp4");
            if (!File.Exists(arquivo))
            {
                var argumentos = new List<string> { "-f", "lavfi", "-i", "testsrc2=s=160x90:r=30:d=30", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=30" };
                if (tipo == "vfr") argumentos.AddRange(["-vf", "select='if(lt(t,15),not(mod(n,2)),1)'", "-fps_mode", "vfr"]);
                argumentos.AddRange(["-c:v", "libx264", "-threads", "2", "-g", "60", "-c:a", "aac", arquivo]);
                await executar(argumentos.ToArray());
            }
            var midia = await new LeitorMetadadosFfprobe(probe).LerAsync(arquivo, token);
            var original = Projeto.Criar("Incremental") with
            {
                Biblioteca = [midia],
                LinhaDoTempo = [.. Enumerable.Range(0, 3).Select(i => new Clipe(Guid.NewGuid(), midia.Id, "Clipe " + i, TempoMidia.Segundos(i * 10), TempoMidia.Segundos(10)))]
            };
            await using var preparador = new PreparadorComposicaoFfmpeg(ffmpeg, probe);
            async Task Medir(string cenario, Projeto projeto)
            {
                var relogio = Stopwatch.StartNew();
                await preparador.PrepararAsync(projeto, null, token);
                resultados.Add(new { Tipo = tipo, Cenario = cenario, Duracao = projeto.Duracao.ToString(), ParedeMs = relogio.Elapsed.TotalMilliseconds, Diagnostico = preparador.UltimoDiagnostico });
                Console.WriteLine($"{tipo}/{cenario}: hits {preparador.UltimoDiagnostico!.CacheHits}, renderizados {preparador.UltimoDiagnostico.BlocosRenderizados}, {relogio.ElapsedMilliseconds} ms");
                await File.WriteAllTextAsync(Path.Combine(pasta, etapa + ".json"), JsonSerializer.Serialize(resultados, new JsonSerializerOptions { WriteIndented = true }), token);
            }
            await Medir("primeira", original);
            await Medir("segunda", original);
            await Medir("cursor-selecao", original with { Cursor = TempoMidia.Segundos(2), Selecao = new(TempoMidia.Zero, TempoMidia.Segundos(2)), ItemSelecionadoId = original.LinhaDoTempo[1].Id });
            foreach (var inicio in new[] { 0, 14, 28 })
            {
                await preparador.PrepararAsync(original, null, token);
                await Medir("corte-" + inicio, new ExcluirIntervalo(TempoMidia.Segundos(inicio), TempoMidia.Segundos(inicio + 1)).Aplicar(original));
            }
            await preparador.PrepararAsync(original, null, token);
            await Medir("divisao", new DividirClipe(original.LinhaDoTempo[0].Id, TempoMidia.Segundos(3)).Aplicar(original));
            await Medir("reordenacao", new MoverClipe(original.LinhaDoTempo[2].Id, 0).Aplicar(original));
            await Medir("volume", new DefinirVolume(original.LinhaDoTempo[1].Id, 50).Aplicar(original));
            var camada = new AdicionarCamada(midia.Id, TempoMidia.Segundos(12), TempoMidia.Segundos(1), TempoMidia.Segundos(2)).Aplicar(original);
            await Medir("camada-inclusao", camada);
            await Medir("camada-remocao", new RemoverCamada(camada.Camadas[0].Clipe.Id).Aplicar(camada));
            var sessao = new SessaoEdicao(original);
            sessao.Executar(new ExcluirIntervalo(TempoMidia.Zero, TempoMidia.Segundos(1)));
            await preparador.PrepararAsync(sessao.Atual, null, token);
            sessao.Desfazer(); await Medir("desfazer", sessao.Atual);
            sessao.Refazer(); await Medir("refazer", sessao.Atual);
            sessao.Executar(new ExcluirIntervalo(TempoMidia.Zero, TempoMidia.Segundos(1)));
            sessao.Executar(new ExcluirIntervalo(TempoMidia.Zero, TempoMidia.Segundos(1)));
            await Medir("sucessivas", sessao.Atual);
        }
    }
}
