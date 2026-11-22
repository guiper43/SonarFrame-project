using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SonarFrame.Core;

namespace SonarFrame.Application;

public static partial class LeitorSrt
{
    [GeneratedRegex(@"^\s*(\d{1,2}):(\d{2}):(\d{2})[,.](\d{1,3})\s*-->\s*(\d{1,2}):(\d{2}):(\d{2})[,.](\d{1,3})(?:\s+.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PadraoIntervalo();

    [GeneratedRegex(@"</?[A-Za-z][^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex PadraoTags();

    public static async Task<ImmutableArray<Legenda>> CarregarAsync(string caminho, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(caminho)) throw new ArgumentException("Informe o caminho do arquivo SRT.");
        if (!string.Equals(Path.GetExtension(caminho), ".srt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Formato não suportado. Selecione um arquivo de legenda .srt.");
        var info = new FileInfo(caminho);
        if (!info.Exists) throw new FileNotFoundException("Arquivo SRT não encontrado.", caminho);
        if (info.Length > 10 * 1024 * 1024)
            throw new InvalidDataException("O arquivo SRT excede o tamanho máximo permitido de 10 MB.");
        var conteudo = await File.ReadAllTextAsync(info.FullName, Encoding.UTF8, token).ConfigureAwait(false);
        return Ler(conteudo);
    }

    public static ImmutableArray<Legenda> Ler(string conteudo)
    {
        if (string.IsNullOrWhiteSpace(conteudo))
            throw new InvalidDataException("O arquivo SRT está vazio ou não contém legendas válidas.");
        var normalizado = conteudo.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var linhas = normalizado.Split('\n');
        var resultado = ImmutableArray.CreateBuilder<Legenda>();
        var indice = 0;
        while (indice < linhas.Length)
        {
            while (indice < linhas.Length && string.IsNullOrWhiteSpace(linhas[indice])) indice++;
            if (indice >= linhas.Length) break;
            if (int.TryParse(linhas[indice].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                && indice + 1 < linhas.Length && PadraoIntervalo().IsMatch(linhas[indice + 1]))
                indice++;
            if (indice >= linhas.Length) break;
            var correspondencia = PadraoIntervalo().Match(linhas[indice]);
            if (!correspondencia.Success)
                throw new InvalidDataException($"Linha de tempo inválida no arquivo SRT: '{linhas[indice].Trim()}'.");
            var inicio = ConverterTempo(correspondencia, 1);
            var fim = ConverterTempo(correspondencia, 5);
            if (fim <= inicio)
                throw new InvalidDataException("O arquivo SRT contém uma legenda com tempo final menor ou igual ao inicial.");
            indice++;
            var textoLinhas = new List<string>();
            while (indice < linhas.Length && !string.IsNullOrWhiteSpace(linhas[indice]))
            {
                textoLinhas.Add(linhas[indice].Trim());
                indice++;
            }
            var bruto = string.Join("\n", textoLinhas).Trim();
            var negrito = false;
            var italico = false;
            var analisado = bruto;
            while (true)
            {
                if (analisado.StartsWith("<b>", StringComparison.OrdinalIgnoreCase)
                    && analisado.EndsWith("</b>", StringComparison.OrdinalIgnoreCase)
                    && analisado.IndexOf("</b>", 3, StringComparison.OrdinalIgnoreCase) == analisado.Length - 4)
                {
                    negrito = true;
                    analisado = analisado[3..^4].Trim();
                    continue;
                }
                if (analisado.StartsWith("<i>", StringComparison.OrdinalIgnoreCase)
                    && analisado.EndsWith("</i>", StringComparison.OrdinalIgnoreCase)
                    && analisado.IndexOf("</i>", 3, StringComparison.OrdinalIgnoreCase) == analisado.Length - 4)
                {
                    italico = true;
                    analisado = analisado[3..^4].Trim();
                    continue;
                }
                break;
            }
            var limpo = PadraoTags().Replace(analisado, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(limpo))
                throw new InvalidDataException("O arquivo SRT contém um bloco de legenda sem texto.");
            var estilo = EstiloLegenda.Padrao with { Negrito = negrito, Italico = italico };
            resultado.Add(new(Guid.NewGuid(), inicio, fim, limpo, estilo));
        }
        if (resultado.Count == 0)
            throw new InvalidDataException("O arquivo SRT não contém legendas válidas.");
        return resultado.OrderBy(l => l.Inicio).ThenBy(l => l.Fim).ToImmutableArray();
    }

    private static TempoMidia ConverterTempo(Match correspondencia, int grupoInicial)
    {
        var horas = long.Parse(correspondencia.Groups[grupoInicial].Value, CultureInfo.InvariantCulture);
        var minutos = long.Parse(correspondencia.Groups[grupoInicial + 1].Value, CultureInfo.InvariantCulture);
        var segundos = long.Parse(correspondencia.Groups[grupoInicial + 2].Value, CultureInfo.InvariantCulture);
        var milissegundosTexto = correspondencia.Groups[grupoInicial + 3].Value.PadRight(3, '0');
        var milissegundos = long.Parse(milissegundosTexto, CultureInfo.InvariantCulture);
        if (minutos >= 60 || segundos >= 60)
            throw new InvalidDataException("Intervalo de tempo inválido no arquivo SRT.");
        var totalMs = ((horas * 3600 + minutos * 60 + segundos) * 1000) + milissegundos;
        return new(totalMs, 1000);
    }
}
