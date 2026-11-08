using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SonarFrame.Application;
using SonarFrame.Core;

namespace SonarFrame.Infrastructure;

internal sealed record PerfilVideo(bool Preencher, string Preset, int Crf, int Threads, bool Exportacao)
{
    public static PerfilVideo Previa { get; } = new(false, "veryfast", 23, 2, false);
    public static PerfilVideo Exportar(ModoEnquadramento enquadramento) => new(enquadramento == ModoEnquadramento.Preencher, "fast", 20, 0, true);
}

public sealed partial class PreparadorComposicaoFfmpeg
{
    private const long MargemDisco = 256L * 1024 * 1024;

    internal async Task ExportarAsync(Projeto projeto, OpcoesExportacao opcoes, IProgress<ProgressoOperacao>? progresso, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(projeto);
        ArgumentNullException.ThrowIfNull(opcoes);
        opcoes.Validar();
        projeto.Validar();
        if (projeto.LinhaDoTempo.IsEmpty) throw new InvalidOperationException("Adicione conteúdos à composição antes de exportar.");
        var destino = Path.GetFullPath(opcoes.Destino);
        var pastaDestino = Path.GetDirectoryName(destino);
        if (pastaDestino is null || !Directory.Exists(pastaDestino)) throw new DirectoryNotFoundException("A pasta de destino não existe. Escolha outra pasta.");
        if (Directory.EnumerateFiles(pastaDestino, "*.sonarframe").Any()
            || Directory.EnumerateFiles(pastaDestino, "*.sonarframe.recovery").Any())
            throw new IOException("A pasta de destino contém arquivos de projeto. Escolha outra pasta para não misturar projetos e vídeos exportados.");
        if (projeto.Biblioteca.Any(m => string.Equals(Path.GetFullPath(m.Caminho), destino, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("O destino é uma mídia usada no projeto. Escolha outro nome para preservar o arquivo original.");
        if (File.Exists(destino) && !opcoes.SubstituirExistente)
            throw new IOException("Já existe um arquivo com este nome. Escolha outro nome ou confirme a substituição.");
        token.ThrowIfCancellationRequested();
        await acessoPreparacao.WaitAsync(token).ConfigureAwait(false);
        try { await Task.Run(() => ExportarProtegidoAsync(projeto, opcoes, destino, pastaDestino, progresso, token), token).ConfigureAwait(false); }
        finally { acessoPreparacao.Release(); }
    }

    private async Task ExportarProtegidoAsync(Projeto projeto, OpcoesExportacao opcoes, string destino, string pastaDestino,
        IProgress<ProgressoOperacao>? progresso, CancellationToken token)
    {
        var relogio = Stopwatch.StartNew();
        progresso?.Report(new("Preparando a exportação", 0, null));
        projeto = Normalizar(projeto);
        var chave = ChaveConteudo(projeto);
        PrepararPasta();
        var metadados = await LerMetadadosAsync(Fontes(projeto), null, token).ConfigureAwait(false);
        var (largura, altura) = opcoes.Dimensoes();
        var perfil = PerfilVideo.Exportar(opcoes.Enquadramento);
        var final = Path.Combine(pastaDestino, $"{Path.GetFileNameWithoutExtension(destino)}.exportando-{Guid.NewGuid():N}.tmp");
        var duracaoTotal = (double)projeto.Duracao.Numerador / projeto.Duracao.Denominador;
        var percentualAnterior = 0;
        void InformarProgresso(TempoMidia posicao)
        {
            var fracao = Math.Clamp((double)posicao.Numerador / posicao.Denominador / duracaoTotal, 0, 1);
            var percentual = (int)(fracao * 95);
            if (percentual <= percentualAnterior) return;
            percentualAnterior = percentual;
            TimeSpan? restante = relogio.Elapsed >= TimeSpan.FromSeconds(3) && fracao > 0
                ? TimeSpan.FromSeconds(relogio.Elapsed.TotalSeconds * (1 - fracao) / fracao) : null;
            progresso?.Report(new("Exportando vídeo", percentual, restante));
        }
        try
        {
            await RenderizarBlocoAsync(projeto, metadados, largura, altura, final, EspacoDisponivel(pastaDestino),
                false, token, perfil, InformarProgresso).ConfigureAwait(false);
            progresso?.Report(new("Finalizando o arquivo", 96, null));
            await VerificarArquivoAsync(final, projeto.Duracao, largura, altura, token).ConfigureAwait(false);
            if (ChaveConteudo(projeto) != chave) throw new IOException("Uma mídia mudou durante a exportação. Exporte novamente.");
            token.ThrowIfCancellationRequested();
            File.Move(final, destino, opcoes.SubstituirExistente);
            progresso?.Report(new("Exportação concluída", 100, TimeSpan.Zero));
        }
        finally
        {
            foreach (var arquivo in new[] { final, Path.Combine(pasta, "filtros.txt") })
            {
                try { File.Delete(arquivo); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static readonly string[] PrefixosProgressoFfmpeg =
    [
        "frame=", "fps=", "stream_", "bitrate=", "total_size=",
        "out_time_us=", "out_time_ms=", "out_time=",
        "dup_frames=", "drop_frames=", "speed=", "progress="
    ];

    internal static bool EhLinhaProgressoFfmpeg(string linha)
        => PrefixosProgressoFfmpeg.Any(p => linha.StartsWith(p, StringComparison.Ordinal));

    internal static async Task<string> LerProgressoExportacaoAsync(TextReader leitor, Action<TempoMidia> progresso)
    {
        var erros = new System.Text.StringBuilder();
        while (await leitor.ReadLineAsync().ConfigureAwait(false) is { } linha)
        {
            if (linha.StartsWith("out_time_us=", StringComparison.Ordinal)
                && long.TryParse(linha[12..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var microssegundos)
                && microssegundos >= 0)
                progresso(new TempoMidia(microssegundos, 1000000));
            else if (!EhLinhaProgressoFfmpeg(linha) && !string.IsNullOrWhiteSpace(linha) && erros.Length < 1500)
                erros.AppendLine(linha[..Math.Min(linha.Length, 1500 - erros.Length)]);
        }
        return erros.ToString();
    }
    private async Task VerificarArquivoAsync(string arquivo, TempoMidia duracaoEsperada, int largura, int altura, CancellationToken token)
    {
        using var json = JsonDocument.Parse(await ffprobe.InspecionarAsync(arquivo, token).ConfigureAwait(false));
        if (!json.RootElement.TryGetProperty("streams", out var elementoStreams))
            throw new InvalidDataException("O arquivo exportado não contém fluxos de mídia válidos.");
        var streams = elementoStreams.EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(s => s.TryGetProperty("codec_type", out var t) && t.GetString() == "video");
        if (video.ValueKind == JsonValueKind.Undefined
            || !video.TryGetProperty("width", out var w) || w.GetInt32() != largura
            || !video.TryGetProperty("height", out var h) || h.GetInt32() != altura
            || !streams.Any(s => s.TryGetProperty("codec_type", out var t) && t.GetString() == "audio"))
            throw new InvalidDataException("O arquivo exportado não corresponde às opções escolhidas.");
        if (!json.RootElement.TryGetProperty("format", out var formato)
            || !formato.TryGetProperty("duration", out var duracaoTexto)
            || !decimal.TryParse(duracaoTexto.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duracao))
            throw new InvalidDataException("Não foi possível verificar a duração do arquivo exportado.");
        if (Math.Abs(duracao - (decimal)duracaoEsperada.Numerador / duracaoEsperada.Denominador) > 0.1m)
            throw new InvalidDataException("A duração do arquivo exportado não corresponde à composição.");
    }

    private static long EspacoDisponivel(string caminho)
    {
        long livre;
        try { livre = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(caminho))!).AvailableFreeSpace; }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { return long.MaxValue / 4; }
        var disponivel = livre - MargemDisco;
        if (disponivel < 64L * 1024 * 1024) throw new IOException("Sem espaço em disco para exportar. Libere espaço e tente novamente.");
        return disponivel;
    }
}
