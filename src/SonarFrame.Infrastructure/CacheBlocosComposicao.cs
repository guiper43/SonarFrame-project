using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SonarFrame.Core;

namespace SonarFrame.Infrastructure;

public sealed partial class PreparadorComposicaoFfmpeg
{
    private readonly Dictionary<string, DateTime> usosBlocos = [];

    public async Task<Midia> PrepararAsync(Projeto projeto, IProgress<string>? progresso, CancellationToken token)
    {
        projeto.Validar();
        if (projeto.LinhaDoTempo.IsEmpty) throw new InvalidOperationException("Adicione conteúdos à composição antes de reproduzir.");
        token.ThrowIfCancellationRequested();
        var chave = await Task.Run(() => Chave(projeto), token);
        if (chave == chaveAtual && preparada is not null && File.Exists(preparada.Caminho))
        {
            progresso?.Report("Carregando vídeo: 100%.");
            return preparada;
        }
        progresso?.Report("Carregando vídeo: 0%.");
        await Task.Run(PrepararPasta, token);
        preparada = null;
        chaveAtual = null;
        foreach (var nome in new[] { "previa.mp4", "intervalo.mp4", "preparando.mp4", "preparando.nut" })
            File.Delete(Path.Combine(pasta, nome));
        var metadados = await LerMetadadosAsync(Fontes(projeto), null, token);
        var (largura, altura) = DimensoesQuadro(projeto, metadados);
        var blocos = new List<(Projeto Projeto, string Arquivo)>();
        for (var inicio = TempoMidia.Zero; inicio < projeto.Duracao; inicio += TempoMidia.Segundos(5))
        {
            token.ThrowIfCancellationRequested();
            var bloco = RecortarBloco(projeto, inicio, TempoMidia.Minimo(inicio + TempoMidia.Segundos(5), projeto.Duracao));
            var identidade = JsonSerializer.SerializeToUtf8Bytes(new { Perfil = 1, largura, altura, Conteudo = Chave(bloco) });
            var arquivo = Path.Combine(pasta, "bloco-" + Convert.ToHexString(SHA256.HashData(identidade)) + ".nut");
            blocos.Add((bloco, arquivo));
        }
        var protegidos = blocos.Select(b => b.Arquivo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var temporario = Path.Combine(pasta, "preparando.nut");
        var destino = Path.Combine(pasta, "preparando.mp4");
        try
        {
            for (var i = 0; i < blocos.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var bloco = blocos[i];
                if (!File.Exists(bloco.Arquivo))
                {
                    LiberarBlocos(protegidos, LimiteBytes / 2 - 32 * 1024 * 1024);
                    var disponivel = LimiteBytes / 2 - TamanhoBlocos();
                    if (disponivel < 2 * 1024 * 1024) throw new IOException("O cache atingiu o limite de 2 GB. Reduza a composição para reproduzir.");
                    await RenderizarBlocoAsync(bloco.Projeto, metadados, largura, altura, temporario, disponivel, token);
                    if (Chave(projeto) != chave) throw new IOException("Uma mídia mudou durante a preparação. Reproduza novamente para atualizar.");
                    File.Move(temporario, bloco.Arquivo);
                }
                usosBlocos[bloco.Arquivo] = DateTime.UtcNow;
                progresso?.Report($"Carregando vídeo: {(i + 1) * 90 / blocos.Count}%.");
            }
            progresso?.Report("Carregando vídeo: 95%.");
            var lista = Path.Combine(pasta, "blocos.txt");
            await File.WriteAllLinesAsync(lista, blocos.SelectMany(b => new[]
            {
                $"file '{Path.GetFileName(b.Arquivo)}'", $"duration {Segundos(b.Projeto.Duracao)}"
            }), token);
            await ExecutarAsync(["-v", "error", "-nostdin", "-y", "-f", "concat", "-safe", "1", "-i", lista,
                "-map", "0:v:0", "-map", "0:a:0", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
                "-t", Segundos(projeto.Duracao), "-fs", (LimiteBytes / 2).ToString(CultureInfo.InvariantCulture),
                "-movflags", "+faststart", destino], token);
            if (new FileInfo(destino).Length >= LimiteBytes / 2 - 1024 * 1024)
                throw new IOException("A prévia excedeu o limite de cache. Reduza a composição para reproduzir.");
            if (Chave(projeto) != chave) throw new IOException("Uma mídia mudou durante a preparação. Reproduza novamente para atualizar.");
            using var verificacao = JsonDocument.Parse(await ffprobe.InspecionarAsync(destino, token));
            var duracao = decimal.Parse(verificacao.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            if (Math.Abs(duracao - (decimal)projeto.Duracao.Numerador / projeto.Duracao.Denominador) > 0.08m)
                throw new InvalidDataException("A duração da prévia não corresponde à composição.");
            token.ThrowIfCancellationRequested();
            var concluido = Path.Combine(pasta, "previa.mp4");
            File.Move(destino, concluido, overwrite: true);
            preparada = new(Guid.NewGuid(), "Composição", concluido, TipoMidia.Video, projeto.Duracao, new(30, 1));
            chaveAtual = chave;
            progresso?.Report("Carregando vídeo: 100%.");
            return preparada;
        }
        finally
        {
            File.Delete(temporario);
            File.Delete(destino);
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
}
