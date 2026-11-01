using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using SonarFrame.Application;
using SonarFrame.Core;

namespace SonarFrame.Infrastructure;

public interface IExecutorFfprobe
{
    Task<string> InspecionarAsync(string caminhoCompleto, CancellationToken token);
}

public sealed class ExecutorFfprobe(string executavel, TimeSpan? timeout = null) : IExecutorFfprobe
{
    public async Task<string> InspecionarAsync(string caminhoCompleto, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(token);
        prazo.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        var info = new ProcessStartInfo(executavel)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argumento in new[] { "-v", "error", "-protocol_whitelist", "file", "-show_streams", "-show_format", "-of", "json", caminhoCompleto })
            info.ArgumentList.Add(argumento);
        using var processo = new Process { StartInfo = info };
        var iniciado = false;
        try
        {
            try { iniciado = processo.Start(); }
            catch (Win32Exception ex) { throw new InvalidOperationException("Não foi possível iniciar o leitor de mídias. Verifique a instalação do sonarFrame (ffprobe).", ex); }
            using var registro = prazo.Token.Register(() => Kill(processo));
            var saida = LerComLimite(processo.StandardOutput, 2 * 1024 * 1024, prazo.Token);
            var erros = LerComLimite(processo.StandardError, 64 * 1024, prazo.Token);

            _ = saida.ContinueWith(_ => Kill(processo), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _ = erros.ContinueWith(_ => Kill(processo), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            await Task.WhenAll(saida, erros, processo.WaitForExitAsync(prazo.Token)).ConfigureAwait(false);
            prazo.Token.ThrowIfCancellationRequested();
            if (processo.ExitCode != 0) throw new InvalidDataException("Arquivo ilegível, corrompido ou com formato não reconhecido.");
            return await saida.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("A leitura deste arquivo excedeu 30 segundos. Os demais arquivos poderão ser importados."); }
        finally
        {
            if (iniciado)
            {
                Kill(processo);
                await processo.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
    private static void Kill(Process processo)
    {
        try { if (!processo.HasExited) processo.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }
    private static async Task<string> LerComLimite(StreamReader leitor, int limite, CancellationToken token)
    {
        var construtor = new StringBuilder(); var buffer = new char[4096];
        int quantidade;
        while ((quantidade = await leitor.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            if (construtor.Length + quantidade > limite) throw new InvalidDataException("O arquivo produziu metadados excessivos.");
            construtor.Append(buffer, 0, quantidade);
        }
        return construtor.ToString();
    }
}

public sealed class LeitorMetadadosFfprobe(IExecutorFfprobe executor) : ILeitorMetadadosMidia
{
    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov" };
    private static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".m4a", ".ogg" };
    private static readonly HashSet<string> Imagens = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };
    public async Task<Midia> LerAsync(string caminho, CancellationToken cancellationToken = default)
    {
        var caminhoCompleto = Path.GetFullPath(caminho);
        var extensao = Path.GetExtension(caminhoCompleto);
        if (!Videos.Contains(extensao) && !Audio.Contains(extensao) && !Imagens.Contains(extensao))
            throw new InvalidDataException("Formato não suportado. Use MP4, MOV, JPG, PNG, MP3, WAV, M4A ou OGG.");
        if (!File.Exists(caminhoCompleto)) throw new FileNotFoundException("O arquivo não foi encontrado.", caminhoCompleto);
        var json = await executor.InspecionarAsync(caminhoCompleto, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var documento = JsonDocument.Parse(json);
            var raiz = documento.RootElement;
            if (!raiz.TryGetProperty("streams", out var fluxos) || fluxos.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("O arquivo não contém mídia reconhecida.");
            var tipo = Imagens.Contains(extensao) ? TipoMidia.Imagem : Videos.Contains(extensao) ? TipoMidia.Video : TipoMidia.Audio;
            var fluxo = fluxos.EnumerateArray().FirstOrDefault(s => Texto(s, "codec_type") == (tipo == TipoMidia.Audio ? "audio" : "video") && !EhCapa(s));
            if (fluxo.ValueKind == JsonValueKind.Undefined || string.IsNullOrWhiteSpace(Texto(fluxo, "codec_name")) || Texto(fluxo, "codec_name") == "unknown")
                throw new InvalidDataException("O conteúdo do arquivo não corresponde ao tipo de mídia escolhido ou seu codec não foi reconhecido.");
            if (tipo == TipoMidia.Imagem)
            {
                var esperado = extensao.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "png" : "mjpeg";
                if (Texto(fluxo, "codec_name") != esperado) throw new InvalidDataException("O conteúdo da imagem não corresponde à extensão do arquivo.");
                return new(Guid.NewGuid(), Path.GetFileName(caminhoCompleto), caminhoCompleto, tipo, null);
            }
            var duracao = LerDuracao(fluxo);
            if (duracao is null && raiz.TryGetProperty("format", out var formato)) duracao = TempoDecimal(Texto(formato, "duration"));
            if (duracao is null || duracao <= TempoMidia.Zero) throw new InvalidDataException("Não foi possível determinar uma duração válida para o arquivo.");
            var media = Taxa(Texto(fluxo, "avg_frame_rate"));
            var nominal = Taxa(Texto(fluxo, "r_frame_rate"));
            var taxa = tipo == TipoMidia.Video ? media ?? nominal : null;

            var variavel = tipo == TipoMidia.Video && (media is null || nominal is null || media.DuracaoQuadro != nominal.DuracaoQuadro);
            return new(Guid.NewGuid(), Path.GetFileName(caminhoCompleto), caminhoCompleto, tipo, duracao, taxa, variavel);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or OverflowException or FormatException)
        { throw new InvalidDataException("Os metadados deste arquivo não puderam ser interpretados.", ex); }
    }
    private static bool EhCapa(JsonElement s) => s.TryGetProperty("disposition", out var d) && Texto(d, "attached_pic") == "1";
    private static string? Texto(JsonElement elemento, string nome) => elemento.TryGetProperty(nome, out var valor) ? valor.ToString() : null;
    private static TaxaQuadros? Taxa(string? texto)
    {
        var partes = texto?.Split('/');
        return partes?.Length == 2 && int.TryParse(partes[0], out var n) && int.TryParse(partes[1], out var d) && n > 0 && d > 0 ? new(n, d) : null;
    }
    private static TempoMidia? LerDuracao(JsonElement fluxo)
    {
        var baseTempo = Taxa(Texto(fluxo, "time_base"));
        if (baseTempo is not null && long.TryParse(Texto(fluxo, "duration_ts"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) && ticks > 0)
        {
            var n = (BigInteger)ticks * baseTempo.Numerador; var d = new BigInteger(baseTempo.Denominador);
            var mdc = BigInteger.GreatestCommonDivisor(n, d);
            return new(checked((long)(n / mdc)), checked((long)(d / mdc)));
        }
        return TempoDecimal(Texto(fluxo, "duration"));
    }
    private static TempoMidia? TempoDecimal(string? texto)
    {
        if (!decimal.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor) || valor <= 0) return null;

        return new(checked((long)decimal.Round(valor * 1_000_000, 0, MidpointRounding.AwayFromZero)), 1_000_000);
    }
}
