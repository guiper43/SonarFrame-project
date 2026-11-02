using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SonarFrame.ProvaReproducao;

internal sealed class FerramentasMidia(string ffmpeg, string ffprobe, string pasta)
{
    public string Arquivo(string nome) => Path.Combine(pasta, nome);

    public static async Task<string> ExecutarAsync(string executavel, IEnumerable<string> argumentos, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var inicio = new ProcessStartInfo(executavel)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argumento in argumentos) inicio.ArgumentList.Add(argumento);
        using var processo = Process.Start(inicio) ?? throw new InvalidOperationException("Processo não iniciado.");
        var saida = processo.StandardOutput.ReadToEndAsync();
        var erro = processo.StandardError.ReadToEndAsync();
        try
        {
            await processo.WaitForExitAsync(token);
            var diagnostico = await erro;
            if (processo.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executavel)}: {diagnostico}");
            return await saida;
        }
        finally
        {
            if (!processo.HasExited) { processo.Kill(entireProcessTree: true); await processo.WaitForExitAsync(); }
            await Task.WhenAll(saida, erro);
        }
    }

    public Task<string> GerarAsync(CancellationToken token, params string[] argumentos) =>
        ExecutarAsync(ffmpeg, new[] { "-hide_banner", "-v", "error", "-nostdin", "-y" }.Concat(argumentos), token);

    public async Task<object> PrepararAsync(CancellationToken token)
    {
        await GerarAsync(token, "-f", "lavfi", "-i", "color=red:s=640x360:r=30:d=8", "-f", "lavfi", "-i",
            "aevalsrc=0.08*sin(2*PI*440*t)+0.6*sin(2*PI*1000*t)*between(t\\,4\\,4.1):s=48000:d=8",
            "-vf", "drawbox=color=blue:t=fill:enable='gte(t,4)'", "-c:v", "libx264", "-preset", "ultrafast",
            "-g", "60", "-keyint_min", "60", "-sc_threshold", "0", "-pix_fmt", "yuv420p", "-c:a", "aac", Arquivo("origem.mp4"));
        await GerarAsync(token, "-i", Arquivo("origem.mp4"), "-c", "copy", Arquivo("origem.mov"));
        await GerarAsync(token, "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=8", "-c:a", "pcm_s16le", Arquivo("camada.wav"));
        await GerarAsync(token, "-f", "lavfi", "-i", "color=green:s=640x360", "-frames:v", "1", "-update", "1", Arquivo("foto.png"));
        var relogio = Stopwatch.StartNew();
        await GerarAsync(token, "-i", Arquivo("origem.mp4"), "-i", Arquivo("camada.wav"), "-loop", "1", "-framerate", "30", "-i", Arquivo("foto.png"),
            "-filter_complex", "[0:v]trim=start=1.3:duration=4,setpts=PTS-STARTPTS[v];[0:a]atrim=start=1.3:duration=4,asetpts=PTS-STARTPTS[a];[1:a]atrim=duration=4,asetpts=PTS-STARTPTS,volume=0.5[b];[a][b]amix=inputs=2:normalize=0:duration=first[m];[2:v]trim=duration=2,setpts=PTS-STARTPTS,format=yuv420p[p];anullsrc=r=48000:cl=mono,atrim=duration=2[s];[v][m][p][s]concat=n=2:v=1:a=1[outv][outa]",
            "-map", "[outv]", "-map", "[outa]", "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-pix_fmt", "yuv420p", "-c:a", "aac", "-movflags", "+faststart", Arquivo("previa.mp4"));
        return new { preparacaoMs = relogio.Elapsed.TotalMilliseconds, bytes = new FileInfo(Arquivo("previa.mp4")).Length, duracaoSegundos = 6, corteNaOrigemSegundos = 1.3 };
    }

    public async Task<object> VerificarAsync(CancellationToken token)
    {
        var metadados = await ExecutarAsync(ffprobe, ["-v", "error", "-show_format", "-show_streams", "-of", "json", Arquivo("previa.mp4")], token);
        using var json = JsonDocument.Parse(metadados);
        var duracao = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Exigir(Math.Abs(duracao - 6) < 0.04, "Duração da composição divergente.");
        await GerarAsync(token, "-i", Arquivo("previa.mp4"), "-vf", "scale=1:1", "-pix_fmt", "rgb24", "-f", "rawvideo", "-an", Arquivo("quadros.rgb"));
        var pixels = await File.ReadAllBytesAsync(Arquivo("quadros.rgb"), token);
        var troca = Enumerable.Range(0, pixels.Length / 3).First(i => pixels[i * 3 + 2] > pixels[i * 3] + 100);
        Exigir(troca == 81, $"Troca de cor esperada no quadro 81, obtida em {troca}.");
        Exigir(pixels[120 * 3 + 1] > pixels[120 * 3] + 50, "Foto não começa em 4 segundos.");
        await GerarAsync(token, "-i", Arquivo("previa.mp4"), "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", Arquivo("audio.f32"));
        var bytes = await File.ReadAllBytesAsync(Arquivo("audio.f32"), token);
        var amostras = new float[bytes.Length / 4]; Buffer.BlockCopy(bytes, 0, amostras, 0, bytes.Length);
        static double Amplitude(float[] dados, int inicio, int tamanho, double frequencia)
        {
            double real = 0, imaginaria = 0;
            for (var i = 0; i < tamanho; i++) { var fase = 2 * Math.PI * frequencia * i / 48000; real += dados[inicio + i] * Math.Cos(fase); imaginaria += dados[inicio + i] * Math.Sin(fase); }
            return 2 * Math.Sqrt(real * real + imaginaria * imaginaria) / tamanho;
        }
        var original = Amplitude(amostras, 24000, 4800, 440);
        var camada = Amplitude(amostras, 24000, 4800, 880);
        Exigir(original is > 0.05 and < 0.11 && camada is > 0.04 and < 0.09, "Mix não preservou as duas frequências/volumes.");
        var pulso = Enumerable.Range(250, 40).First(i => Amplitude(amostras, i * 480, 480, 1000) > 0.3) / 100.0;
        Exigir(Math.Abs(pulso - troca / 30.0) <= 0.02, "Pulso e troca visual perderam sincronismo.");
        Exigir(Amplitude(amostras, 216000, 4800, 440) < 0.001, "Áudio vazou para a foto.");
        var quadrosOrigem = await ExecutarAsync(ffprobe, ["-v", "error", "-select_streams", "v:0", "-show_frames", "-show_entries", "frame=key_frame,best_effort_timestamp_time", "-of", "json", Arquivo("origem.mp4")], token);
        using var origem = JsonDocument.Parse(quadrosOrigem);
        var quadroCorte = origem.RootElement.GetProperty("frames")[39];
        Exigir(quadroCorte.GetProperty("key_frame").GetInt32() == 0, "A amostra não exercitou corte fora de keyframe.");
        return new { duracao, quadroTroca = troca, pulsoSegundos = pulso, amplitudeOriginal = original, amplitudeCamada = camada, corteForaDeKeyframe = true, fotoESilencio = true };
    }

    public async Task<object> MedirPreparacaoAsync(int segundos, CancellationToken token)
    {
        var destino = Arquivo($"benchmark-{segundos}.mp4");
        var relogio = Stopwatch.StartNew();
        await GerarAsync(token, "-stream_loop", "-1", "-i", Arquivo("previa.mp4"), "-t", segundos.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-vf", "scale=640:360", "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-c:a", "aac", destino);
        var resultado = new { duracaoSegundos = segundos, preparacaoMs = relogio.Elapsed.TotalMilliseconds, bytes = new FileInfo(destino).Length, perfil = "640x360/30; repetição sintética, não representa projeto real" };
        File.Delete(destino);
        return resultado;
    }

    public async Task<object> VerificarCancelamentoAsync(CancellationToken token)
    {
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(token);
        prazo.CancelAfter(TimeSpan.FromMilliseconds(500));
        var relogio = Stopwatch.StartNew();
        var cancelado = false;
        try
        {
            await GerarAsync(prazo.Token, "-re", "-stream_loop", "-1", "-i", Arquivo("origem.mp4"), "-f", "null", "-");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { cancelado = true; }
        token.ThrowIfCancellationRequested();
        Exigir(cancelado && relogio.Elapsed < TimeSpan.FromSeconds(5), "Cancelamento não encerrou FFmpeg no prazo.");
        return new { cancelado, encerramentoMs = relogio.Elapsed.TotalMilliseconds };
    }

    internal static void Exigir(bool condicao, string mensagem)
    {
        if (!condicao) throw new InvalidOperationException(mensagem);
    }
}
