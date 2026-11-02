using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SonarFrame.Core;
using SonarFrame.Infrastructure;

var raiz = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? ".");
var pasta = Path.Combine(raiz, ".tools", "test-artifacts", "validacao-reproducao");
Directory.CreateDirectory(pasta);
var ffmpeg = Path.Combine(raiz, ".tools", "ffmpeg", "ffmpeg.exe");
var probe = new ExecutorFfprobe(Path.Combine(raiz, ".tools", "ffmpeg", "ffprobe.exe"));
var leitor = new LeitorMetadadosFfprobe(probe);
var resultados = new List<object>();
var midias = new List<Midia>();
using var prazo = new CancellationTokenSource(TimeSpan.FromMinutes(20));
var token = prazo.Token;

async Task ExecutarAsync(params string[] argumentos)
{
    var inicio = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
    foreach (var valor in new[] { "-v", "error", "-nostdin", "-y" }.Concat(argumentos)) inicio.ArgumentList.Add(valor);
    using var processo = Process.Start(inicio)!;
    var erros = processo.StandardError.ReadToEndAsync();
    try
    {
        await processo.WaitForExitAsync(token);
        if (processo.ExitCode != 0) throw new InvalidOperationException(await erros);
    }
    finally
    {
        if (!processo.HasExited) { processo.Kill(entireProcessTree: true); await processo.WaitForExitAsync(); }
        await erros;
    }
}

try
{
    foreach (var extensao in new[] { "jpg", "jpeg", "png", "mp4", "mov", "mp3", "wav", "m4a", "ogg", "opus.ogg" })
    {
        var arquivo = Path.Combine(pasta, "mídia de teste." + extensao);
        if (extensao is "jpg" or "jpeg" or "png")
            await ExecutarAsync("-f", "lavfi", "-i", "color=blue:s=160x90", "-frames:v", "1", arquivo);
        else if (extensao is "mp4" or "mov")
            await ExecutarAsync("-f", "lavfi", "-i", "testsrc2=s=160x90:r=30:d=2", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-c:v", "libx264", "-threads", "2", "-c:a", "aac", arquivo);
        else
        {
            var codec = extensao switch { "mp3" => "libmp3lame", "wav" => "pcm_s16le", "m4a" => "aac", "ogg" => "libvorbis", _ => "libopus" };
            await ExecutarAsync("-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=2", "-c:a", codec, arquivo);
        }
        var midia = await leitor.LerAsync(arquivo, token);
        midias.Add(midia);
        var projeto = Projeto.Criar(extensao) with { Biblioteca = [midia], LinhaDoTempo = [new(Guid.NewGuid(), midia.Id, midia.Nome, TempoMidia.Zero, TempoMidia.Segundos(1))] };
        await using var preparador = new PreparadorComposicaoFfmpeg(ffmpeg, probe);
        var relogio = Stopwatch.StartNew();
        var previa = await preparador.PrepararAsync(projeto, null, token);
        var quadro = await preparador.ObterQuadroAsync(projeto, new(1, 2), token);
        if (quadro is null || quadro.Png.Length == 0 || !File.Exists(previa.Caminho)) throw new InvalidDataException("Prévia não produzida: " + extensao);
        resultados.Add(new { Formato = extensao, Tipo = midia.Tipo.ToString(), Duracao = previa.Duracao.ToString(), Milissegundos = relogio.ElapsedMilliseconds });
        Console.WriteLine($"Formato {extensao}: aprovado.");
    }
    var demonstracao = Projeto.Criar("Validação da reprodução") with
    {
        Biblioteca = [.. midias], LinhaDoTempo = [.. midias.Select(m => new Clipe(Guid.NewGuid(), m.Id, m.Nome, TempoMidia.Zero, TempoMidia.Segundos(1)))],
        Camadas = [new(new(Guid.NewGuid(), midias[4].Id, "Vídeo sobreposto", TempoMidia.Zero, TempoMidia.Segundos(1), 50), new(1, 2))]
    };
    await new RepositorioProjetosSqlite().SalvarAsync(Path.Combine(pasta, "projeto-validacao.sonarframe"), demonstracao, token);
    if (args.Contains("--benchmark"))
    {
        var longo = Path.Combine(pasta, "origem 30 minutos.mp4");
        await ExecutarAsync("-stream_loop", "-1", "-i", Path.Combine(pasta, "mídia de teste.mp4"), "-t", "1800", "-c", "copy", longo);
        var video = await leitor.LerAsync(longo, token);
        var audio = await leitor.LerAsync(Path.Combine(pasta, "mídia de teste.wav"), token);
        var foto = await leitor.LerAsync(Path.Combine(pasta, "mídia de teste.png"), token);
        foreach (var segundos in new[] { 60, 600, 1800 })
        {
            var duracao = TempoMidia.Segundos(segundos);
            var projeto = Projeto.Criar($"Benchmark {segundos}") with
            {
                Biblioteca = [video, audio, foto], LinhaDoTempo = [new(Guid.NewGuid(), video.Id, video.Nome, TempoMidia.Zero, duracao)],
                Camadas = [new(new(Guid.NewGuid(), audio.Id, audio.Nome, TempoMidia.Zero, TempoMidia.Segundos(2), 50), TempoMidia.Segundos(segundos / 2)),
                    new(new(Guid.NewGuid(), foto.Id, foto.Nome, TempoMidia.Zero, TempoMidia.Segundos(2)), duracao - TempoMidia.Segundos(2))]
            };
            await using var preparador = new PreparadorComposicaoFfmpeg(ffmpeg, probe);
            Console.WriteLine($"Preparando benchmark de {segundos / 60} minuto(s)...");
            var relogio = Stopwatch.StartNew();
            var previa = await preparador.PrepararAsync(projeto, null, token);
            var preparo = relogio.ElapsedMilliseconds;
            relogio.Restart();
            await preparador.PrepararAsync(projeto with { Cursor = new(segundos, 2) }, null, token);
            var cache = relogio.ElapsedMilliseconds;
            relogio.Restart();
            await preparador.ObterQuadroAsync(projeto, new(segundos, 2), token);
            var quadro = relogio.ElapsedMilliseconds;
            resultados.Add(new { DuracaoSegundos = segundos, PreparacaoMs = preparo, ReutilizacaoMs = cache, QuadroMs = quadro, Bytes = new FileInfo(previa.Caminho).Length });
            Console.WriteLine($"{segundos / 60} min: preparação {preparo} ms; cache {cache} ms; quadro {quadro} ms.");
        }
    }
    if (args.Contains("--benchmark-buscas"))
    {
        var longo = Path.Combine(pasta, "origem 30 minutos.mp4");
        if (!File.Exists(longo))
            await ExecutarAsync("-stream_loop", "-1", "-i", Path.Combine(pasta, "mídia de teste.mp4"), "-t", "1800", "-c", "copy", longo);
        var video = await leitor.LerAsync(longo, token);
        var medidas = new List<object>();
        foreach (var segundos in new[] { 60, 600, 1800 })
        {
            var projeto = Projeto.Criar("Busca") with
            {
                Biblioteca = [video], LinhaDoTempo = [new(Guid.NewGuid(), video.Id, video.Nome, TempoMidia.Zero, TempoMidia.Segundos(segundos))]
            };
            await using var preparador = new PreparadorComposicaoFfmpeg(ffmpeg, probe);
            var relogio = Stopwatch.StartNew();
            await preparador.ObterQuadroAsync(projeto, new(segundos, 2), token);
            var primeira = relogio.ElapsedMilliseconds;
            var repetidas = new List<long>();
            for (var i = 1; i <= 5; i++)
            {
                relogio.Restart();
                await preparador.ObterQuadroAsync(projeto, new TempoMidia(segundos, 2) + new TempoMidia(i, 30), token);
                repetidas.Add(relogio.ElapsedMilliseconds);
            }
            medidas.Add(new { DuracaoSegundos = segundos, PrimeiraBuscaMs = primeira, BuscasSeguintesMs = repetidas });
            Console.WriteLine($"Busca {segundos / 60} min: primeira {primeira} ms; seguintes {string.Join(", ", repetidas)} ms.");
        }
        await File.WriteAllTextAsync(Path.Combine(pasta, "buscas.json"), JsonSerializer.Serialize(medidas, new JsonSerializerOptions { WriteIndented = true }));
    }
    var relatorio = new { Sistema = RuntimeInformation.OSDescription, Arquitetura = RuntimeInformation.ProcessArchitecture.ToString(), ProcessadoresLogicos = Environment.ProcessorCount,
        Processador = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), MemoriaDisponivelBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, Resultados = resultados };
    await File.WriteAllTextAsync(Path.Combine(pasta, "resultado.json"), JsonSerializer.Serialize(relatorio, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Validação concluída. Relatório: " + Path.Combine(pasta, "resultado.json"));
    return 0;
}
catch (Exception ex)
{
    await File.WriteAllTextAsync(Path.Combine(pasta, "falha.txt"), ex.ToString());
    Console.Error.WriteLine(ex.Message);
    return 1;
}
