using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SonarFrame.Core;

public enum TipoMidia { Video, Audio, Imagem }
public sealed record Midia(Guid Id,
    [property: JsonPropertyName("Name")] string Nome,
    [property: JsonPropertyName("Path")] string Caminho,
    [property: JsonPropertyName("Kind")] TipoMidia Tipo,
    [property: JsonPropertyName("Duration")] TempoMidia? Duracao,
    [property: JsonPropertyName("FrameRate")] TaxaQuadros? TaxaQuadros = null,
    [property: JsonPropertyName("VariableFrameRate")] bool TaxaQuadrosVariavel = false);
public sealed record Clipe(Guid Id,
    [property: JsonPropertyName("AssetId")] Guid MidiaId,
    [property: JsonPropertyName("Name")] string Nome,
    [property: JsonPropertyName("SourceStart")] TempoMidia InicioNaOrigem,
    [property: JsonPropertyName("Duration")] TempoMidia Duracao, int Volume = 100);
public sealed record CamadaTemporal([property: JsonPropertyName("Clip")] Clipe Clipe, [property: JsonPropertyName("Start")] TempoMidia Inicio);
public sealed record SelecaoTemporal([property: JsonPropertyName("Anchor")] TempoMidia Ancora, [property: JsonPropertyName("Active")] TempoMidia ExtremidadeAtiva)
{
    [JsonIgnore] public TempoMidia Inicio => TempoMidia.Minimo(Ancora, ExtremidadeAtiva);
    [JsonIgnore] public TempoMidia Fim => TempoMidia.Maximo(Ancora, ExtremidadeAtiva);
}
public sealed record Projeto(Guid Id,
    [property: JsonPropertyName("Name")] string Nome,
    [property: JsonPropertyName("Library")] ImmutableArray<Midia> Biblioteca,
    [property: JsonPropertyName("Timeline")] ImmutableArray<Clipe> LinhaDoTempo,
    [property: JsonPropertyName("Layers")] ImmutableArray<CamadaTemporal> Camadas, TempoMidia Cursor,
    [property: JsonPropertyName("Selection")] SelecaoTemporal? Selecao = null,
    [property: JsonPropertyName("SelectedItemId")] Guid? ItemSelecionadoId = null)
{
    [JsonIgnore] public TempoMidia Duracao => LinhaDoTempo.Aggregate(TempoMidia.Zero, (t, c) => t + c.Duracao);
    public static Projeto Criar(string nome) => new Projeto(Guid.NewGuid(), nome, [], [], [], TempoMidia.Zero).Validar();
    public Projeto Validar()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Nome)) throw new ArgumentException("Projeto sem identificação.");
        if (Biblioteca.IsDefault || LinhaDoTempo.IsDefault || Camadas.IsDefault) throw new ArgumentException("Coleções ausentes.");
        if (Biblioteca.Any(a => a is null) || LinhaDoTempo.Any(c => c is null) || Camadas.Any(l => l is null || l.Clipe is null)) throw new ArgumentException("Elementos ausentes.");
        if (Biblioteca.Select(a => a.Id).Distinct().Count() != Biblioteca.Length) throw new ArgumentException("Mídias duplicadas.");
        foreach (var a in Biblioteca)
        {
            if (a.Id == Guid.Empty || string.IsNullOrWhiteSpace(a.Nome) || string.IsNullOrWhiteSpace(a.Caminho) || !Enum.IsDefined(a.Tipo)) throw new ArgumentException("Mídia inválida.");
            if (a.Duracao is { } d && d <= TempoMidia.Zero) throw new ArgumentException("Duração inválida.");
            if (a.Tipo != TipoMidia.Imagem && a.Duracao is null) throw new ArgumentException("Duração da mídia necessária.");
            if (a.TaxaQuadros is { } taxa) _ = taxa.DuracaoQuadro;
        }
        var clipes = LinhaDoTempo.Concat(Camadas.Select(l => l.Clipe)).ToArray();
        if (clipes.Select(c => c.Id).Distinct().Count() != clipes.Length) throw new ArgumentException("Clipes duplicados.");
        foreach (var c in clipes)
        {
            var midia = Biblioteca.SingleOrDefault(a => a.Id == c.MidiaId) ?? throw new ArgumentException("Mídia não encontrada.");
            if (c.Id == Guid.Empty || string.IsNullOrWhiteSpace(c.Nome) || c.InicioNaOrigem < TempoMidia.Zero || c.Duracao <= TempoMidia.Zero || c.Volume is < 0 or > 200) throw new ArgumentException("Clipe inválido.");
            if (midia.Duracao is { } d && c.InicioNaOrigem + c.Duracao > d) throw new ArgumentException("Clipe ultrapassa a origem.");
        }
        if (Camadas.Any(l => l.Inicio < TempoMidia.Zero || l.Inicio + l.Clipe.Duracao > Duracao)) throw new ArgumentException("Camada fora da composição.");
        if (Cursor < TempoMidia.Zero || Cursor > Duracao) throw new ArgumentException("Cursor fora da composição.");
        if (Selecao is { } s && (s.Inicio < TempoMidia.Zero || s.Fim > Duracao)) throw new ArgumentException("Seleção fora da composição.");
        if (ItemSelecionadoId is { } id && !clipes.Any(c => c.Id == id)) throw new ArgumentException("Seleção de item inválida.");
        return this;
    }
}
