using SonarFrame.Core;
using System.Globalization;
using System.Text.Json;

namespace SonarFrame.Infrastructure;

public sealed partial class PreparadorComposicaoFfmpeg
{
    public async Task<Midia?> PrepararIntervaloAsync(Projeto projeto, TempoMidia inicio, TempoMidia fim, CancellationToken token)
    {
        if (inicio < TempoMidia.Zero || fim > projeto.Duracao || inicio >= fim) throw new ArgumentException("Intervalo de reprodução inválido.");
        var completa = await PrepararAsync(projeto, null, token).ConfigureAwait(false);
        var destino = Path.Combine(pasta, "intervalo.mp4");
        var duracao = fim - inicio;
        File.Delete(destino);
        var disponivel = LimiteBytes - TamanhoBlocos() - new FileInfo(completa.Caminho).Length - 1024 * 1024;
        if (disponivel < 1024 * 1024) throw new IOException("O cache não tem espaço no limite de 2 GB para preparar este intervalo. Reduza a composição.");
        var filtros = $"[0:v]trim=start={Segundos(inicio)}:end={Segundos(fim)},setpts=PTS-{Segundos(inicio)}/TB,fps=30:start_time=0,tpad=stop_mode=clone:stop_duration={Segundos(duracao)}[v];"
            + $"[0:a]atrim=start={Segundos(inicio)}:end={Segundos(fim)},asetpts=PTS-STARTPTS[a]";
        try
        {
            await ExecutarAsync(["-v", "error", "-nostdin", "-y", "-i", completa.Caminho, "-filter_complex_threads", "1", "-filter_complex", filtros,
                "-map", "[v]", "-map", "[a]", "-c:v", "libx264", "-threads", "2", "-preset", "veryfast", "-crf", "23", "-c:a", "aac", "-b:a", "192k",
                "-t", Segundos(duracao), "-fs", disponivel.ToString(CultureInfo.InvariantCulture), "-movflags", "+faststart", destino], token).ConfigureAwait(false);
            if (new FileInfo(destino).Length >= disponivel - 1024) throw new IOException("O intervalo excedeu o limite de cache. Reduza a seleção para reproduzir.");
            using var json = JsonDocument.Parse(await ffprobe.InspecionarAsync(destino, token).ConfigureAwait(false));
            var gerada = decimal.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            if (Math.Abs(gerada - (decimal)duracao.Numerador / duracao.Denominador) > 0.08m) throw new InvalidDataException("A duração do intervalo preparado não corresponde à seleção.");
            if (!await EstaAtualizadaAsync(projeto, token).ConfigureAwait(false)) throw new IOException("Uma mídia mudou durante a preparação do intervalo. Tente novamente.");
            return new(Guid.NewGuid(), "Intervalo da composição", destino, TipoMidia.Video, duracao, new(30, 1));
        }
        catch { if (File.Exists(destino)) File.Delete(destino); throw; }
    }
}
