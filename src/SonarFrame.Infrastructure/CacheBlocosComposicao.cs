using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SonarFrame.Core;

namespace SonarFrame.Infrastructure;

public sealed partial class PreparadorComposicaoFfmpeg
{
    private readonly Dictionary<string, DateTime> usosBlocos = [];
    public DiagnosticoPreparacao? UltimoDiagnostico { get; private set; }
    private readonly SemaphoreSlim acessoPreparacao = new(1, 1);
    private readonly AsyncLocal<DiagnosticoPreparacao?> medicaoAtual = new();

    public async Task<Midia> PrepararAsync(Projeto projeto, IProgress<string>? progresso, CancellationToken token)
    {
        await acessoPreparacao.WaitAsync(token).ConfigureAwait(false);
        var diagnostico = new DiagnosticoPreparacao();
        var relogio = Stopwatch.StartNew();
        medicaoAtual.Value = diagnostico;
        try
        {
            var resultado = await Task.Run(() => PrepararMedidoAsync(projeto, progresso, diagnostico, token), token).ConfigureAwait(false);
            diagnostico.Concluida = true;
            return resultado;
        }
        finally
        {
            diagnostico.TotalPreparacaoMs = relogio.Elapsed.TotalMilliseconds;
            UltimoDiagnostico = diagnostico;
            medicaoAtual.Value = null;
            acessoPreparacao.Release();
        }
    }

    private async Task<Midia> PrepararMedidoAsync(Projeto projeto, IProgress<string>? progresso, DiagnosticoPreparacao diagnostico, CancellationToken token)
    {
        var resolucao = Stopwatch.StartNew();
        projeto.Validar();
        projeto = Normalizar(projeto);
        diagnostico.MixagemAudioMs = 0;
        if (projeto.LinhaDoTempo.IsEmpty) throw new InvalidOperationException("Adicione conteúdos à composição antes de reproduzir.");
        token.ThrowIfCancellationRequested();
        var chave = Chave(projeto);
        diagnostico.ResolucaoComposicaoMs = resolucao.Elapsed.TotalMilliseconds;
        if (chave == chaveAtual && preparada is not null && File.Exists(preparada.Caminho))
        {
            diagnostico.PreviaReutilizada = true;
            progresso?.Report("Carregando vídeo: 100%.");
            return preparada;
        }
        progresso?.Report("Carregando vídeo: 0%.");
        PrepararPasta();
        preparada = null;
        chaveAtual = null;
        foreach (var nome in new[] { "previa.mp4", "previa.mov", "intervalo.mp4", "preparando.mp4", "preparando.mov", "preparando.nut", "audio.m4a" })
            File.Delete(Path.Combine(pasta, nome));
        var metadados = await LerMetadadosAsync(Fontes(projeto), null, token);
        var (largura, altura) = DimensoesQuadro(projeto, metadados);
        var consulta = Stopwatch.StartNew();
        var blocos = new List<(Projeto Projeto, string Video, string Audio)>();
        foreach (var (inicio, fim) in Segmentos(projeto))
        {
            token.ThrowIfCancellationRequested();
            var bloco = RecortarBloco(projeto, inicio, fim);
            blocos.Add((bloco, CaminhoBloco(bloco, largura, altura, false), CaminhoBloco(bloco, largura, altura, true)));
        }
        var protegidos = blocos.SelectMany(b => new[] { b.Video, b.Audio }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        diagnostico.ConsultaCacheMs = consulta.Elapsed.TotalMilliseconds;
        var temporario = Path.Combine(pasta, "preparando.nut");
        var destino = Path.Combine(pasta, "preparando.mov");
        try
        {
            var percentualAnterior = 0;
            for (var i = 0; i < blocos.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var bloco = blocos[i];
                diagnostico.BlocosConsultados++;
                var renderizou = false;
                consulta.Restart();
                var reutilizado = File.Exists(bloco.Video) && File.Exists(bloco.Audio);
                diagnostico.ConsultaCacheMs += consulta.Elapsed.TotalMilliseconds;
                if (reutilizado) diagnostico.CacheHits++; else diagnostico.CacheMisses++;
                foreach (var audio in new[] { false, true })
                {
                    var arquivo = audio ? bloco.Audio : bloco.Video;
                    if (File.Exists(arquivo)) { usosBlocos[arquivo] = DateTime.UtcNow; continue; }
                    LiberarBlocos(protegidos, LimiteBytes / 2 - 32 * 1024 * 1024);
                    var disponivel = LimiteBytes / 2 - TamanhoBlocos();
                    if (disponivel < 2 * 1024 * 1024) throw new IOException("O cache atingiu o limite de 2 GB. Reduza a composição para reproduzir.");
                    var renderizacao = Stopwatch.StartNew();
                    try { await RenderizarBlocoAsync(bloco.Projeto, metadados, largura, altura, temporario, disponivel, audio, token); }
                    finally
                    {
                        diagnostico.RenderizacaoComMixagemMs += renderizacao.Elapsed.TotalMilliseconds;
                        if (audio) diagnostico.MixagemAudioMs += renderizacao.Elapsed.TotalMilliseconds;
                        else diagnostico.RenderizacaoVideoMs += renderizacao.Elapsed.TotalMilliseconds;
                    }
                    if (audio) diagnostico.AudiosRenderizados++; else diagnostico.VideosRenderizados++;
                    renderizou = true;
                    if (Chave(projeto) != chave) throw new IOException("Uma mídia mudou durante a preparação. Reproduza novamente para atualizar.");
                    File.Move(temporario, arquivo);
                    usosBlocos[arquivo] = DateTime.UtcNow;
                }
                if (renderizou) diagnostico.BlocosRenderizados++;
                var percentual = (i + 1) * 90 / blocos.Count;
                if (percentual != percentualAnterior) progresso?.Report($"Carregando vídeo: {percentual}%.");
                percentualAnterior = percentual;
            }
            progresso?.Report("Carregando vídeo: 95%.");
            var lista = Path.Combine(pasta, "blocos.txt");
            var listaAudio = Path.Combine(pasta, "audios.txt");
            await File.WriteAllLinesAsync(lista, blocos.SelectMany(b => new[]
            {
                $"file '{Path.GetFileName(b.Video)}'", $"duration {Segundos(b.Projeto.Duracao)}"
            }), token);
            await File.WriteAllLinesAsync(listaAudio, blocos.SelectMany(b => new[]
            {
                $"file '{Path.GetFileName(b.Audio)}'", $"duration {Segundos(b.Projeto.Duracao)}"
            }), token);
            var disponivelFinal = LimiteBytes - TamanhoBlocos() - 1024 * 1024;
            if (disponivelFinal < 1024 * 1024) throw new IOException("O cache não tem espaço para montar a prévia.");
            var montagem = Stopwatch.StartNew();
            await ExecutarAsync(["-v", "error", "-nostdin", "-y", "-f", "concat", "-safe", "1", "-i", lista,
                "-f", "concat", "-safe", "1", "-i", listaAudio, "-map", "0:v:0", "-map", "1:a:0", "-c", "copy",
                "-t", Segundos(projeto.Duracao), "-fs", disponivelFinal.ToString(CultureInfo.InvariantCulture),
                "-movflags", "+faststart", destino], token);
            if (new FileInfo(destino).Length >= disponivelFinal - 1024 * 1024)
                throw new IOException("A prévia excedeu o limite de cache. Reduza a composição para reproduzir.");
            if (Chave(projeto) != chave) throw new IOException("Uma mídia mudou durante a preparação. Reproduza novamente para atualizar.");
            using var verificacao = JsonDocument.Parse(await ffprobe.InspecionarAsync(destino, token));
            var duracao = decimal.Parse(verificacao.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            if (Math.Abs(duracao - (decimal)projeto.Duracao.Numerador / projeto.Duracao.Denominador) > 0.08m)
                throw new InvalidDataException("A duração da prévia não corresponde à composição.");
            token.ThrowIfCancellationRequested();
            var concluido = Path.Combine(pasta, "previa.mov");
            File.Move(destino, concluido, overwrite: true);
            diagnostico.MontagemFinalMs = montagem.Elapsed.TotalMilliseconds;
            preparada = new(Guid.NewGuid(), "Composição", concluido, TipoMidia.Video, projeto.Duracao, new(30, 1));
            chaveAtual = chave;
            progresso?.Report("Carregando vídeo: 100%.");
            return preparada;
        }
        finally
        {
            File.Delete(temporario);
            File.Delete(destino);
            File.Delete(Path.Combine(pasta, "audio.m4a"));
        }
    }

    private static Projeto RecortarBloco(Projeto projeto, TempoMidia inicio, TempoMidia fim)
    {
        Clipe Recortar(Clipe clipe, TempoMidia posicao) => clipe with
        {
            InicioNaOrigem = clipe.InicioNaOrigem + TempoMidia.Maximo(inicio - posicao, TempoMidia.Zero),
            Duracao = TempoMidia.Minimo(posicao + clipe.Duracao, fim) - TempoMidia.Maximo(posicao, inicio)
        };
        var clipes = ImmutableArray.CreateBuilder<Clipe>();
        var posicao = TempoMidia.Zero;
        foreach (var clipe in projeto.LinhaDoTempo)
        {
            if (posicao < fim && posicao + clipe.Duracao > inicio) clipes.Add(Recortar(clipe, posicao));
            posicao += clipe.Duracao;
        }
        var camadas = projeto.Camadas.Where(c => c.Inicio < fim && c.Inicio + c.Clipe.Duracao > inicio)
            .Select(c => new CamadaTemporal(Recortar(c.Clipe, c.Inicio), TempoMidia.Maximo(c.Inicio - inicio, TempoMidia.Zero))).ToImmutableArray();
        return projeto with { LinhaDoTempo = clipes.ToImmutable(), Camadas = camadas, Cursor = TempoMidia.Zero, Selecao = null, ItemSelecionadoId = null };
    }

    private long TamanhoBlocos() => Directory.GetFiles(pasta, "bloco-*.nut").Sum(a => new FileInfo(a).Length);

    private void LiberarBlocos(HashSet<string> protegidos, long limite)
    {
        var tamanho = TamanhoBlocos();
        foreach (var arquivo in Directory.GetFiles(pasta, "bloco-*.nut").Where(a => !protegidos.Contains(a))
            .OrderBy(a => usosBlocos.GetValueOrDefault(a)))
        {
            if (tamanho <= limite) break;
            var bytes = new FileInfo(arquivo).Length;
            File.Delete(arquivo);
            usosBlocos.Remove(arquivo);
            tamanho -= bytes;
        }
    }
    private string CaminhoBloco(Projeto bloco, int largura, int altura, bool audio)
    {
        var identidade = JsonSerializer.SerializeToUtf8Bytes(new { Perfil = 2, Largura = audio ? 0 : largura, Altura = audio ? 0 : altura, Conteudo = ChaveConteudo(bloco, audio) });
        return Path.Combine(pasta, "bloco-" + (audio ? "audio-" : "video-") + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(identidade)) + ".nut");
    }

}
