using System.Globalization;
using System.Text.Json;
using SonarFrame.Core;

namespace SonarFrame.Infrastructure;

internal sealed class IndiceQuadros(IExecutorFfprobe ffprobe)
{
    private readonly SemaphoreSlim acesso = new(1, 1);
    private readonly Dictionary<string, (long Tamanho, DateTime Modificacao, TempoMidia[] Tempos, bool Completo)> indices = new(StringComparer.OrdinalIgnoreCase);

    public async Task<TempoMidia[]> LerAsync(Midia midia, TempoMidia posicao, CancellationToken token)
    {
        await acesso.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var arquivo = new FileInfo(midia.Caminho);
            if (!arquivo.Exists) throw new FileNotFoundException($"Arquivo ausente: {midia.Nome}. Use Localizar arquivo na biblioteca.");
            var tamanho = arquivo.Length; var modificacao = arquivo.LastWriteTimeUtc;
            if (indices.TryGetValue(arquivo.FullName, out var existente) && existente.Tamanho == tamanho && existente.Modificacao == modificacao && (existente.Completo || Cobre(existente.Tempos, posicao)))
                return existente.Tempos;
            TempoMidia[] tempos = [];
            var completo = false;
            foreach (var margem in new[] { 5, 20, 80 })
            {
                var inicio = TempoMidia.Maximo(TempoMidia.Zero, posicao - TempoMidia.Segundos(margem));
                var fim = posicao + TempoMidia.Segundos(margem);
                var parcial = await ffprobe.InspecionarJanelaQuadrosAsync(arquivo.FullName, inicio, fim, token).ConfigureAwait(false);
                tempos = await Task.Run(() => Interpretar(parcial, token), token).ConfigureAwait(false);
                if (Cobre(tempos, posicao)) break;
            }
            if (!Cobre(tempos, posicao))
            {
                var texto = await ffprobe.InspecionarQuadrosAsync(arquivo.FullName, token).ConfigureAwait(false);
                tempos = await Task.Run(() => Interpretar(texto, token), token).ConfigureAwait(false);
                if (tempos.Length == 0) throw new InvalidDataException("Não foi possível identificar os timestamps dos quadros desta mídia.");
                completo = true;
            }
            arquivo.Refresh();
            if (!arquivo.Exists || arquivo.Length != tamanho || arquivo.LastWriteTimeUtc != modificacao)
                throw new IOException("A mídia mudou durante a leitura dos quadros. Tente novamente.");
            if (indices.Count >= 16 || indices.Values.Sum(i => (long)i.Tempos.Length) + tempos.Length > 1000000) indices.Clear();
            indices[arquivo.FullName] = (tamanho, modificacao, tempos, completo);
            return tempos;
        }
        finally { acesso.Release(); }
    }

    private static bool Cobre(TempoMidia[] tempos, TempoMidia posicao) => tempos.Length > 0 && tempos[^1] > posicao && ((LimiteSuperior(tempos, posicao) >= 2 && tempos[0] < posicao) || (tempos[0] <= TempoMidia.Zero && posicao >= tempos[0]));

    private static TempoMidia[] Interpretar(string texto, CancellationToken token)
    {
        using var json = JsonDocument.Parse(texto);
        var raiz = json.RootElement;
        var partes = raiz.GetProperty("streams")[0].GetProperty("time_base").GetString()!.Split('/');
        var numerador = long.Parse(partes[0], CultureInfo.InvariantCulture);
        var denominador = long.Parse(partes[1], CultureInfo.InvariantCulture);
        var inicio = TempoMidia.Zero;
        if (raiz.TryGetProperty("format", out var formato) && formato.TryGetProperty("start_time", out var valor)
            && decimal.TryParse(valor.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var segundos))
            inicio = new(checked((long)(segundos * 1000000)), 1000000);
        var tempos = new List<TempoMidia>();
        foreach (var quadro in raiz.GetProperty("frames").EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            if (quadro.TryGetProperty("best_effort_timestamp", out var timestamp) && timestamp.TryGetInt64(out var pts))
                tempos.Add(new TempoMidia(checked(pts * numerador), denominador) - inicio);
            if (tempos.Count > 1000000) throw new InvalidDataException("A mídia excede o limite de um milhão de quadros para navegação precisa. Divida o arquivo em partes menores.");
        }

        return tempos.Distinct().Order().ToArray();
    }

    public static int LimiteSuperior(TempoMidia[] tempos, TempoMidia posicao)
    {
        var inicio = 0; var fim = tempos.Length;
        while (inicio < fim)
        {
            var meio = inicio + (fim - inicio) / 2;
            if (tempos[meio] <= posicao) inicio = meio + 1; else fim = meio;
        }
        return inicio;
    }

    public static TempoMidia BuscaParaQuadro(TempoMidia[] tempos, int indice)
    {
        if (indice <= 0) return TempoMidia.Zero;
        var soma = tempos[indice - 1] + tempos[indice];
        return TempoMidia.Maximo(TempoMidia.Zero, new(soma.Numerador, checked(soma.Denominador * 2)));
    }
}
