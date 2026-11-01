using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using SonarFrame.Application;
using SonarFrame.Core;

namespace SonarFrame.Infrastructure;

public interface IFfprobeRunner
{
    Task<string> InspectAsync(string fullPath, CancellationToken token);
}

/// <summary>Runs a local ffprobe process without a shell, with bounded output and a per-file timeout.</summary>
public sealed class FfprobeRunner(string executable, TimeSpan? timeout = null) : IFfprobeRunner
{
    public async Task<string> InspectAsync(string fullPath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-v", "error", "-protocol_whitelist", "file", "-show_streams", "-show_format", "-of", "json", fullPath })
            info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        var started = false;
        try
        {
            try { started = process.Start(); }
            catch (Win32Exception ex) { throw new InvalidOperationException("Não foi possível iniciar o leitor de mídias. Verifique a instalação do sonarFrame (ffprobe).", ex); }
            using var registration = deadline.Token.Register(() => Kill(process));
            var output = ReadBounded(process.StandardOutput, 2 * 1024 * 1024, deadline.Token);
            var errors = ReadBounded(process.StandardError, 64 * 1024, deadline.Token);
            // A faulting reader must not leave the child blocked writing to a full pipe.
            _ = output.ContinueWith(_ => Kill(process), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _ = errors.ContinueWith(_ => Kill(process), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidDataException("Arquivo ilegível, corrompido ou com formato não reconhecido.");
            return await output.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("A leitura deste arquivo excedeu 30 segundos. Os demais arquivos poderão ser importados."); }
        finally
        {
            if (started)
            {
                Kill(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }
    private static async Task<string> ReadBounded(StreamReader reader, int limit, CancellationToken token)
    {
        var builder = new StringBuilder(); var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            if (builder.Length + count > limit) throw new InvalidDataException("O arquivo produziu metadados excessivos.");
            builder.Append(buffer, 0, count);
        }
        return builder.ToString();
    }
}

public sealed class FfprobeMediaProbe(IFfprobeRunner runner) : IMediaProbe
{
    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov" };
    private static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".m4a", ".ogg" };
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };
    public async Task<MediaAsset> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var extension = Path.GetExtension(fullPath);
        if (!Videos.Contains(extension) && !Audio.Contains(extension) && !Images.Contains(extension))
            throw new InvalidDataException("Formato não suportado. Use MP4, MOV, JPG, PNG, MP3, WAV, M4A ou OGG.");
        if (!File.Exists(fullPath)) throw new FileNotFoundException("O arquivo não foi encontrado.", fullPath);
        var json = await runner.InspectAsync(fullPath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("O arquivo não contém mídia reconhecida.");
            var kind = Images.Contains(extension) ? MediaKind.Image : Videos.Contains(extension) ? MediaKind.Video : MediaKind.Audio;
            var stream = streams.EnumerateArray().FirstOrDefault(s => Text(s, "codec_type") == (kind == MediaKind.Audio ? "audio" : "video") && !IsCover(s));
            if (stream.ValueKind == JsonValueKind.Undefined || string.IsNullOrWhiteSpace(Text(stream, "codec_name")) || Text(stream, "codec_name") == "unknown")
                throw new InvalidDataException("O conteúdo do arquivo não corresponde ao tipo de mídia escolhido ou seu codec não foi reconhecido.");
            if (kind == MediaKind.Image)
            {
                var expected = extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "png" : "mjpeg";
                if (Text(stream, "codec_name") != expected) throw new InvalidDataException("O conteúdo da imagem não corresponde à extensão do arquivo.");
                return new(Guid.NewGuid(), Path.GetFileName(fullPath), fullPath, kind, null);
            }
            var duration = ReadDuration(stream);
            if (duration is null && root.TryGetProperty("format", out var format)) duration = DecimalTime(Text(format, "duration"));
            if (duration is null || duration <= MediaTime.Zero) throw new InvalidDataException("Não foi possível determinar uma duração válida para o arquivo.");
            var average = Rate(Text(stream, "avg_frame_rate"));
            var nominal = Rate(Text(stream, "r_frame_rate"));
            var rate = kind == MediaKind.Video ? average ?? nominal : null;
            // Rate comparison is conservative metadata detection, not a full timestamp scan.
            var variable = kind == MediaKind.Video && (average is null || nominal is null || average.FrameDuration != nominal.FrameDuration);
            return new(Guid.NewGuid(), Path.GetFileName(fullPath), fullPath, kind, duration, rate, variable);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or OverflowException or FormatException)
        { throw new InvalidDataException("Os metadados deste arquivo não puderam ser interpretados.", ex); }
    }
    private static bool IsCover(JsonElement s) => s.TryGetProperty("disposition", out var d) && Text(d, "attached_pic") == "1";
    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.ToString() : null;
    private static FrameRate? Rate(string? text)
    {
        var parts = text?.Split('/');
        return parts?.Length == 2 && int.TryParse(parts[0], out var n) && int.TryParse(parts[1], out var d) && n > 0 && d > 0 ? new(n, d) : null;
    }
    private static MediaTime? ReadDuration(JsonElement stream)
    {
        var basis = Rate(Text(stream, "time_base"));
        if (basis is not null && long.TryParse(Text(stream, "duration_ts"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) && ticks > 0)
        {
            var n = (BigInteger)ticks * basis.Numerator; var d = new BigInteger(basis.Denominator);
            var gcd = BigInteger.GreatestCommonDivisor(n, d);
            return new(checked((long)(n / gcd)), checked((long)(d / gcd)));
        }
        return DecimalTime(Text(stream, "duration"));
    }
    private static MediaTime? DecimalTime(string? text)
    {
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0) return null;
        // ffprobe durations have microsecond resolution. Keep that decimal precision exactly.
        return new(checked((long)decimal.Round(value * 1_000_000, 0, MidpointRounding.AwayFromZero)), 1_000_000);
    }
}
