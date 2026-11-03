using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SonarFrame.Application;
using SonarFrame.Core;

namespace SonarFrame.Infrastructure;

public sealed partial class PreparadorComposicaoFfmpeg(string ffmpeg, IExecutorFfprobe ffprobe) : IPreparadorComposicao
{
    private readonly string pasta = Path.Combine(Path.GetTempPath(), "SonarFrame", "composicao", Guid.NewGuid().ToString("N"));
    private readonly IndiceQuadros indiceQuadros = new(ffprobe);
    private string? chaveAtual;
    private Midia? preparada;
    private FileStream? usoCache;
    private const long LimiteBytes = 2L * 1024 * 1024 * 1024;
    private sealed record Metadados(bool Video, bool Audio, int Largura, int Altura, int IndiceVideo);
    private sealed record IdentidadeMetadados(string Caminho, long Tamanho, DateTime UltimaEscrita, TipoMidia Tipo);
    private readonly Dictionary<IdentidadeMetadados, Metadados> cacheMetadados = [];
    private readonly SemaphoreSlim acessoMetadados = new(1, 1);

    private static IdentidadeMetadados IdentificarMetadados(Midia midia)
    {
        var arquivo = new FileInfo(midia.Caminho);
        if (!arquivo.Exists) throw new FileNotFoundException($"Arquivo ausente: {midia.Nome}. Use Localizar arquivo na biblioteca.");
        return new(arquivo.FullName, arquivo.Length, arquivo.LastWriteTimeUtc, midia.Tipo);
    }

    private static Midia[] Fontes(Projeto projeto) => projeto.LinhaDoTempo.Concat(projeto.Camadas.Select(c => c.Clipe))
        .Select(c => projeto.Biblioteca.Single(m => m.Id == c.MidiaId)).DistinctBy(m => m.Id).ToArray();

    private static string Chave(Projeto projeto)
        => ChaveConteudo(projeto);

    public Task<bool> EstaAtualizadaAsync(Projeto projeto, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        return preparada is not null && File.Exists(preparada.Caminho) && Chave(projeto) == chaveAtual
            || sessaoProgressiva?.EstaAtualizada(projeto) == true;
    }, token);

    private async Task RenderizarBlocoAsync(Projeto projeto, Dictionary<Guid, Metadados> metadados,
        int larguraQuadro, int alturaQuadro, string destino, long disponivel, bool somenteAudio, CancellationToken token)
    {
        if (!somenteAudio) projeto = Normalizar(projeto with { LinhaDoTempo = [.. projeto.LinhaDoTempo.Select(c => c with { Volume = 100 })] });
        var fontes = Fontes(projeto);
        var argumentos = new List<string> { "-hide_banner", "-v", "error", "-nostdin", "-y" };
        var filtrosVideo = new StringBuilder();
        var filtrosAudio = new StringBuilder();
        var buscas = new Dictionary<int, TempoMidia>();
        async Task EntradaAsync(Midia midia, Clipe clipe, int indice)
        {
            var busca = TempoMidia.Zero;
            if (midia.Tipo == TipoMidia.Audio) busca = TempoMidia.Maximo(TempoMidia.Zero, clipe.InicioNaOrigem - TempoMidia.Segundos(1));
            else if (midia.Tipo == TipoMidia.Video)
            {
                var tempos = await indiceQuadros.LerAsync(midia, clipe.InicioNaOrigem, token);
                var anterior = tempos[Math.Max(0, IndiceQuadros.LimiteSuperior(tempos, clipe.InicioNaOrigem) - 1)];
                busca = TempoMidia.Maximo(TempoMidia.Zero, anterior - TempoMidia.Segundos(1));
            }
            buscas[indice] = busca;
            if (midia.Tipo == TipoMidia.Imagem) argumentos.AddRange(["-loop", "1", "-framerate", "30"]);
            if (busca > TempoMidia.Zero) argumentos.AddRange(["-ss", Segundos(busca)]);
            argumentos.AddRange(["-i", Path.GetFullPath(midia.Caminho)]);
        }
        string Audio(Clipe clipe, int i) => $"[{i}:a:0]atrim=start={Segundos(clipe.InicioNaOrigem - buscas[i])}:duration={Segundos(clipe.Duracao)},asetpts=PTS-{Segundos(clipe.InicioNaOrigem - buscas[i])}/TB,aresample=48000:async=1:first_pts=0,aformat=sample_fmts=fltp:channel_layouts=stereo,volume={(clipe.Volume / 100m).ToString(CultureInfo.InvariantCulture)},apad,atrim=duration={Segundos(clipe.Duracao)}";
        async Task<string> VideoAsync(Clipe clipe, Midia midia, int entrada, TempoMidia inicio, bool transparente)
        {
            var origem = midia.Tipo == TipoMidia.Imagem ? TempoMidia.Zero : clipe.InicioNaOrigem;
            var primeiro = origem;
            if (midia.Tipo == TipoMidia.Video)
            {
                var tempos = await indiceQuadros.LerAsync(midia, clipe.InicioNaOrigem, token);
                primeiro = tempos[Math.Max(0, IndiceQuadros.LimiteSuperior(tempos, origem) - 1)];
            }
            origem -= buscas[entrada];
            primeiro -= buscas[entrada];
            var duracao = Segundos(clipe.Duracao);
            return $"[{entrada}:{metadados[midia.Id].IndiceVideo}]trim=start={Segundos(primeiro)}:end={Segundos(origem + clipe.Duracao)},setpts=PTS-{Segundos(origem)}/TB,{Escala(larguraQuadro, alturaQuadro, transparente)},fps=30:start_time=0:round=up,tpad=stop_mode=clone:stop_duration={duracao},trim=duration={duracao},setpts=PTS+{Segundos(inicio)}/TB";
        }
        filtrosVideo.Append($"color=c=black:s={larguraQuadro}x{alturaQuadro}:r=30:d={Segundos(projeto.Duracao)},setsar=1[vbase];");
        var visualAtual = "vbase";
        var inicioClipe = TempoMidia.Zero;
        for (var i = 0; i < projeto.LinhaDoTempo.Length; i++)
        {
            var clipe = projeto.LinhaDoTempo[i];
            var midia = fontes.Single(m => m.Id == clipe.MidiaId);
            var dados = metadados[midia.Id];
            await EntradaAsync(midia, clipe, i);
            var duracao = Segundos(clipe.Duracao);
            if (dados.Video && !somenteAudio)
            {
                filtrosVideo.Append(await VideoAsync(clipe, midia, i, inicioClipe, false) + $"[v{i}];");
                filtrosVideo.Append($"[{visualAtual}][v{i}]overlay=eof_action=pass:repeatlast=0:enable='gte(t,{Segundos(inicioClipe)})*lt(t,{Segundos(inicioClipe + clipe.Duracao)})'[principal{i}];");
                visualAtual = $"principal{i}";
            }
            filtrosAudio.Append(dados.Audio && midia.Tipo != TipoMidia.Imagem
                ? Audio(clipe, i) + $"[a{i}];"
                : $"anullsrc=r=48000:cl=stereo,atrim=duration={duracao}[a{i}];");
            inicioClipe += clipe.Duracao;
        }
        for (var i = 0; i < projeto.LinhaDoTempo.Length; i++) filtrosAudio.Append($"[a{i}]");
        filtrosAudio.Append($"concat=n={projeto.LinhaDoTempo.Length}:v=0:a=1[basea];");
        var audios = new List<string> { "basea" };
        for (var camadaIndice = 0; camadaIndice < projeto.Camadas.Length; camadaIndice++)
        {
            var camada = projeto.Camadas[camadaIndice];
            var clipe = camada.Clipe;
            var midia = fontes.Single(m => m.Id == clipe.MidiaId);
            var dados = metadados[midia.Id];
            var entrada = projeto.LinhaDoTempo.Length + camadaIndice;
            await EntradaAsync(midia, clipe, entrada);
            if (dados.Video && !somenteAudio)
            {
                filtrosVideo.Append(await VideoAsync(clipe, midia, entrada, camada.Inicio, true) + $"[cv{camadaIndice}];");
                filtrosVideo.Append($"[{visualAtual}][cv{camadaIndice}]overlay=eof_action=pass:repeatlast=0:format=auto:enable='gte(t,{Segundos(camada.Inicio)})*lt(t,{Segundos(camada.Inicio + clipe.Duracao)})'[sobre{camadaIndice}];");
                visualAtual = $"sobre{camadaIndice}";
            }
            if (dados.Audio && midia.Tipo != TipoMidia.Imagem)
            {
                var atraso = decimal.Round((decimal)camada.Inicio.Numerador * 48000 / camada.Inicio.Denominador);
                filtrosAudio.Append(Audio(clipe, entrada) + $",adelay={atraso.ToString(CultureInfo.InvariantCulture)}S:all=1[ca{camadaIndice}];");
                audios.Add($"ca{camadaIndice}");
            }
        }
        filtrosVideo.Append($"[{visualAtual}]format=yuv420p[v];");
        foreach (var audio in audios) filtrosAudio.Append($"[{audio}]");
        filtrosAudio.Append($"amix=inputs={audios.Count}:duration=first:normalize=0:dropout_transition=0,atrim=duration={Segundos(projeto.Duracao)}[a]");
        var script = Path.Combine(pasta, "filtros.txt");
        await File.WriteAllTextAsync(script, (somenteAudio ? filtrosAudio : filtrosVideo).ToString().TrimEnd(';'), token);
        argumentos.AddRange(["-filter_complex_threads", "1", "-/filter_complex", script, "-map", somenteAudio ? "[a]" : "[v]", "-c:v", "libx264", "-threads", "2", "-preset", "veryfast", "-crf", "23", "-c:a", "pcm_f32le", "-t", Segundos(projeto.Duracao), "-fs", disponivel.ToString(CultureInfo.InvariantCulture), destino]);
        await ExecutarAsync(argumentos, token);
        if (new FileInfo(destino).Length >= disponivel - 1024 * 1024)
            throw new IOException("A preparação excedeu o limite de cache. Reduza a composição para reproduzir.");
    }

    private async Task<Dictionary<Guid, Metadados>> LerMetadadosAsync(Midia[] fontes, IProgress<string>? progresso, CancellationToken token)
    {
        await acessoMetadados.WaitAsync(token).ConfigureAwait(false);
        try { return await LerMetadadosProtegidosAsync(fontes, progresso, token).ConfigureAwait(false); }
        finally { acessoMetadados.Release(); }
    }

    private async Task<Dictionary<Guid, Metadados>> LerMetadadosProtegidosAsync(Midia[] fontes, IProgress<string>? progresso, CancellationToken token)
    {
        var metadados = new Dictionary<Guid, Metadados>();
        foreach (var midia in fontes)
        {
            token.ThrowIfCancellationRequested();
            var identidade = IdentificarMetadados(midia);
            if (cacheMetadados.TryGetValue(identidade, out var existente))
            {
                metadados[midia.Id] = existente;
                continue;
            }
            progresso?.Report($"Verificando {midia.Nome}.");
            using var json = JsonDocument.Parse(await ffprobe.InspecionarAsync(midia.Caminho, token).ConfigureAwait(false));
            var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            var visual = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video"
                && !(s.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var capa) && capa.GetInt32() != 0));
            var temVideo = midia.Tipo != TipoMidia.Audio && visual.ValueKind != JsonValueKind.Undefined;
            var largura = temVideo ? visual.GetProperty("width").GetInt32() : 640;
            var altura = temVideo ? visual.GetProperty("height").GetInt32() : 360;
            if (temVideo && visual.TryGetProperty("sample_aspect_ratio", out var sar))
            {
                var partes = sar.GetString()?.Split(':');
                if (partes?.Length == 2 && int.TryParse(partes[0], out var n) && int.TryParse(partes[1], out var d) && n > 0 && d > 0)
                    largura = (int)Math.Round((double)largura * n / d);
            }
            if (temVideo && visual.TryGetProperty("side_data_list", out var dados)
                && dados.EnumerateArray().Any(d => d.TryGetProperty("rotation", out var r) && Math.Abs(r.GetDouble()) % 180 > 1))
                (largura, altura) = (altura, largura);
            metadados[midia.Id] = new(temVideo, streams.Any(s => s.GetProperty("codec_type").GetString() == "audio"), largura, altura,
                temVideo ? visual.GetProperty("index").GetInt32() : 0);
            token.ThrowIfCancellationRequested();
            if (IdentificarMetadados(midia) != identidade) throw new IOException("Uma mídia mudou durante a leitura dos metadados. Tente novamente.");
            foreach (var antiga in cacheMetadados.Keys.Where(c => string.Equals(c.Caminho, identidade.Caminho, StringComparison.OrdinalIgnoreCase)).ToArray())
                cacheMetadados.Remove(antiga);
            if (cacheMetadados.Count >= 256) cacheMetadados.Remove(cacheMetadados.Keys.First());
            cacheMetadados[identidade] = metadados[midia.Id];
        }
        return metadados;
    }

    private static (int Largura, int Altura) DimensoesQuadro(Projeto projeto, Dictionary<Guid, Metadados> metadados)
    {
        var primeira = projeto.LinhaDoTempo.Select(c => metadados[c.MidiaId]).FirstOrDefault(m => m.Video);
        var larguraQuadro = primeira is null ? 640 : Math.Max(2, (int)Math.Round(640.0 * primeira.Largura / Math.Max(primeira.Largura, primeira.Altura) / 2) * 2);
        var alturaQuadro = primeira is null ? 360 : Math.Max(2, (int)Math.Round(640.0 * primeira.Altura / Math.Max(primeira.Largura, primeira.Altura) / 2) * 2);
        return (larguraQuadro, alturaQuadro);
    }

    private static string Escala(int larguraQuadro, int alturaQuadro, bool transparente) => $"scale=w='max(2,trunc(iw*sar/2)*2)':h=ih,setsar=1,scale={larguraQuadro}:{alturaQuadro}:force_original_aspect_ratio=decrease:force_divisible_by=2,format={(transparente ? "rgba" : "yuv420p")},pad={larguraQuadro}:{alturaQuadro}:(ow-iw)/2:(oh-ih)/2:color={(transparente ? "black@0" : "black")}";

    private static string Segundos(TempoMidia tempo) => ((decimal)tempo.Numerador / tempo.Denominador).ToString("0.#########", CultureInfo.InvariantCulture);

    private void PrepararPasta()
    {
        if (usoCache is not null) return;
        var raiz = Path.GetFullPath(Path.GetDirectoryName(pasta)!);
        Directory.CreateDirectory(raiz);
        foreach (var diretorio in Directory.GetDirectories(raiz))
        {
            var caminho = Path.GetFullPath(diretorio);
            if (!caminho.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileName(caminho), "N", out _)) continue;
            var marcador = Path.Combine(caminho, "em-uso");
            try
            {
                if ((File.GetAttributes(caminho) & FileAttributes.ReparsePoint) != 0
                    || Directory.GetDirectories(caminho).Length != 0 || !File.Exists(marcador)) continue;
                using (new FileStream(marcador, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    foreach (var arquivo in Directory.GetFiles(caminho).Where(a => a != marcador)) File.Delete(arquivo);
                File.Delete(marcador);
                Directory.Delete(caminho);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Directory.CreateDirectory(pasta);
        usoCache = new FileStream(Path.Combine(pasta, "em-uso"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    private async Task ExecutarAsync(IEnumerable<string> argumentos, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var inicio = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argumento in argumentos) inicio.ArgumentList.Add(argumento);
        var criacao = Stopwatch.StartNew();
        using var processo = IniciarFfmpeg(inicio);
        if (medicaoAtual.Value is { } medicao)
        {
            medicao.CriacaoProcessosFfmpegMs += criacao.Elapsed.TotalMilliseconds;
            medicao.ProcessosFfmpeg++;
        }
        var erros = processo.StandardError.ReadToEndAsync();
        try
        {
            await processo.WaitForExitAsync(token);
            var erro = await erros;
            if (processo.ExitCode != 0 && erro.Contains("No space left on device", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Sem espaço para preparar a prévia. Libere espaço no disco de arquivos temporários e tente novamente.");
            if (processo.ExitCode != 0) throw new InvalidOperationException("Falha ao preparar composição: " + erro[..Math.Min(erro.Length, 1500)]);
        }
        finally
        {
            if (!processo.HasExited) { processo.Kill(entireProcessTree: true); await processo.WaitForExitAsync(CancellationToken.None); }
            await erros;
        }
    }

    private static Process IniciarFfmpeg(ProcessStartInfo inicio)
    {
        try { return Process.Start(inicio) ?? throw new InvalidOperationException("Não foi possível iniciar o FFmpeg da instalação."); }
        catch (Win32Exception ex) { throw new InvalidOperationException("Não foi possível iniciar a reprodução. Verifique se o FFmpeg acompanha a instalação do sonarFrame.", ex); }
    }

    public async ValueTask DisposeAsync()
    {
        if (sessaoProgressiva is { } progressiva) await progressiva.DisposeAsync().ConfigureAwait(false);
        usoCache?.Dispose(); usoCache = null;
        if (Directory.Exists(pasta))
        {
            foreach (var arquivo in Directory.GetFiles(pasta)) File.Delete(arquivo);
            Directory.Delete(pasta);
        }
    }
}
