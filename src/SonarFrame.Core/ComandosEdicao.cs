using System.Collections.Immutable;

namespace SonarFrame.Core;

public interface IComandoEdicao { Projeto Aplicar(Projeto projeto); }

public sealed record RegistrarMidia(Midia Midia) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p) => p with { Biblioteca = p.Biblioteca.Add(Midia) };
}
public sealed record RemoverMidias(ImmutableArray<Guid> MidiasIds) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        var ids = MidiasIds.ToHashSet();
        if (ids.Any(id => !p.Biblioteca.Any(m => m.Id == id))) throw new ArgumentException("Mídia não encontrada.");
        if (p.LinhaDoTempo.Concat(p.Camadas.Select(c => c.Clipe)).Any(c => ids.Contains(c.MidiaId)))
            throw new InvalidOperationException("Há mídias selecionadas em uso na composição. Remova os clipes ou camadas correspondentes antes de removê-las da biblioteca.");
        return ids.Count == 0 ? p : p with { Biblioteca = p.Biblioteca.Where(m => !ids.Contains(m.Id)).ToImmutableArray() };
    }
}
public sealed record ReligarMidia(Guid MidiaId, string Caminho) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        var indice = p.Biblioteca.FindIndex(a => a.Id == MidiaId);
        if (indice < 0) throw new ArgumentException("Mídia não encontrada.");
        return p with { Biblioteca = p.Biblioteca.SetItem(indice, p.Biblioteca[indice] with { Caminho = Caminho }) };
    }
}
public sealed record InserirClipe(Guid MidiaId, int Indice, TempoMidia? DuracaoImagem = null, LayoutVisual? Layout = null) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        var midia = p.Biblioteca.Single(a => a.Id == MidiaId);
        var duracao = midia.Duracao ?? DuracaoImagem ?? throw new ArgumentException("Informe a duração da imagem.");
        var clipe = new Clipe(Guid.NewGuid(), midia.Id, midia.Nome, TempoMidia.Zero, duracao, Layout: Layout);
        return p with { LinhaDoTempo = p.LinhaDoTempo.Insert(Indice, clipe), ItemSelecionadoId = clipe.Id };
    }
}
public sealed record AdicionarCamada(Guid MidiaId, TempoMidia Inicio, TempoMidia InicioNaOrigem, TempoMidia Duracao, LayoutVisual? Layout = null) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        var midia = p.Biblioteca.Single(a => a.Id == MidiaId);
        var clipe = new Clipe(Guid.NewGuid(), midia.Id, midia.Nome, InicioNaOrigem, Duracao, Layout: Layout);
        return p with { Camadas = p.Camadas.Add(new(clipe, Inicio)), ItemSelecionadoId = clipe.Id };
    }
}
public sealed record DividirClipe(Guid ClipeId, TempoMidia Deslocamento) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        var indice = p.LinhaDoTempo.FindIndex(c => c.Id == ClipeId);
        if (indice < 0) throw new ArgumentException("Clipe não encontrado.");
        var clipe = p.LinhaDoTempo[indice];
        if (Deslocamento <= TempoMidia.Zero || Deslocamento >= clipe.Duracao) throw new ArgumentException("Divisão deve ocorrer dentro do clipe.");
        var restante = clipe.Duracao - Deslocamento;
        var primeiro = clipe with
        {
            Nome = clipe.Nome + " — Parte 1", Duracao = Deslocamento,
            FadeInAudio = TempoMidia.Minimo(clipe.FadeInAudio, Deslocamento), FadeOutAudio = TempoMidia.Zero,
            FadeInVideo = TempoMidia.Minimo(clipe.FadeInVideo, Deslocamento), FadeOutVideo = TempoMidia.Zero
        };
        var segundo = clipe with
        {
            Id = Guid.NewGuid(), Nome = clipe.Nome + " — Parte 2", InicioNaOrigem = clipe.InicioNaOrigem + Deslocamento, Duracao = restante,
            FadeInAudio = TempoMidia.Zero, FadeOutAudio = TempoMidia.Minimo(clipe.FadeOutAudio, restante),
            FadeInVideo = TempoMidia.Zero, FadeOutVideo = TempoMidia.Minimo(clipe.FadeOutVideo, restante)
        };
        return p with { LinhaDoTempo = p.LinhaDoTempo.SetItem(indice, primeiro).Insert(indice + 1, segundo) };
    }
}
public sealed record MoverClipe(Guid ClipeId, int Destino) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        var indice = p.LinhaDoTempo.FindIndex(c => c.Id == ClipeId);
        if (indice < 0 || Destino < 0 || Destino >= p.LinhaDoTempo.Length) throw new ArgumentException("Destino ou clipe inválido.");
        if (indice == Destino) return p;
        return p with { LinhaDoTempo = p.LinhaDoTempo.RemoveAt(indice).Insert(Destino, p.LinhaDoTempo[indice]) };
    }
}
public sealed record DefinirVolume(Guid ClipeId, int Volume) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        if (!p.LinhaDoTempo.Any(c => c.Id == ClipeId) && !p.Camadas.Any(l => l.Clipe.Id == ClipeId)) throw new ArgumentException("Clipe não encontrado.");
        return p with
        {
            LinhaDoTempo = p.LinhaDoTempo.Select(c => c.Id == ClipeId ? c with { Volume = Volume } : c).ToImmutableArray(),
            Camadas = p.Camadas.Select(l => l.Clipe.Id == ClipeId ? l with { Clipe = l.Clipe with { Volume = Volume } } : l).ToImmutableArray()
        };
    }
}
public sealed record DefinirFades(Guid ClipeId, TempoMidia FadeInAudio, TempoMidia FadeOutAudio, TempoMidia FadeInVideo, TempoMidia FadeOutVideo) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        if (!p.LinhaDoTempo.Any(c => c.Id == ClipeId) && !p.Camadas.Any(l => l.Clipe.Id == ClipeId)) throw new ArgumentException("Clipe não encontrado.");
        Clipe Atualizar(Clipe c) => c with { FadeInAudio = FadeInAudio, FadeOutAudio = FadeOutAudio, FadeInVideo = FadeInVideo, FadeOutVideo = FadeOutVideo };
        return p with
        {
            LinhaDoTempo = p.LinhaDoTempo.Select(c => c.Id == ClipeId ? Atualizar(c) : c).ToImmutableArray(),
            Camadas = p.Camadas.Select(l => l.Clipe.Id == ClipeId ? l with { Clipe = Atualizar(l.Clipe) } : l).ToImmutableArray()
        };
    }
}
public sealed record DefinirLegendas(ImmutableArray<Legenda> Legendas) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        if (Legendas.IsDefault) throw new ArgumentException("Coleções ausentes.");
        return p with { Legendas = Legendas.OrderBy(l => l.Inicio).ThenBy(l => l.Fim).ToImmutableArray() };
    }
}
public sealed record EditarLegenda(Guid LegendaId, string Texto, EstiloLegenda Estilo, EscopoEstiloLegenda Escopo = EscopoEstiloLegenda.ApenasSelecionada) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        var indice = p.Legendas.FindIndex(l => l.Id == LegendaId);
        if (indice < 0) throw new ArgumentException("Legenda não encontrada.");
        if (!Enum.IsDefined(Escopo)) throw new ArgumentException("Escopo de legenda inválido.");
        return p with
        {
            Legendas = p.Legendas.Select(l => l.Id == LegendaId
                ? l with { Texto = Texto, Estilo = Estilo }
                : Escopo == EscopoEstiloLegenda.TodasDoProjeto ? l with { Estilo = Estilo } : l).ToImmutableArray()
        };
    }
}
public sealed record ExcluirIntervalo(TempoMidia Inicio, TempoMidia Fim) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        if (Inicio < TempoMidia.Zero || Fim > p.Duracao || Inicio >= Fim) throw new ArgumentException("Intervalo de remoção inválido.");
        var linhaDoTempo = ImmutableArray.CreateBuilder<Clipe>();
        var posicao = TempoMidia.Zero;
        foreach (var clipe in p.LinhaDoTempo)
        {
            linhaDoTempo.AddRange(Restante(clipe, posicao).Select(x => x.Clipe));
            posicao += clipe.Duracao;
        }
        var camadas = p.Camadas.SelectMany(l => Restante(l.Clipe, l.Inicio)).ToImmutableArray();
        var legendas = p.Legendas.SelectMany(RestanteLegenda).ToImmutableArray();
        var ids = linhaDoTempo.Select(c => c.Id).Concat(camadas.Select(l => l.Clipe.Id)).ToHashSet();
        return p with { LinhaDoTempo = linhaDoTempo.ToImmutable(), Camadas = camadas, Legendas = legendas, Cursor = Inicio,
            Selecao = null, ItemSelecionadoId = p.ItemSelecionadoId is { } id && ids.Contains(id) ? id : null };
    }
    private IEnumerable<Legenda> RestanteLegenda(Legenda legenda)
    {
        if (legenda.Fim <= Inicio) { yield return legenda; yield break; }
        if (legenda.Inicio >= Fim)
        {
            var deslocamento = Fim - Inicio;
            yield return legenda with { Inicio = legenda.Inicio - deslocamento, Fim = legenda.Fim - deslocamento };
            yield break;
        }
        var esquerda = TempoMidia.Maximo(TempoMidia.Zero, Inicio - legenda.Inicio);
        var direita = TempoMidia.Maximo(TempoMidia.Zero, legenda.Fim - Fim);
        if (esquerda > TempoMidia.Zero)
            yield return legenda with { Fim = legenda.Inicio + esquerda };
        if (direita > TempoMidia.Zero)
            yield return legenda with { Id = esquerda > TempoMidia.Zero ? Guid.NewGuid() : legenda.Id, Inicio = Inicio, Fim = Inicio + direita };
    }
    private IEnumerable<CamadaTemporal> Restante(Clipe clipe, TempoMidia posicao)
    {
        var fim = posicao + clipe.Duracao;
        if (fim <= Inicio) { yield return new(clipe, posicao); yield break; }
        if (posicao >= Fim) { yield return new(clipe, posicao - (Fim - Inicio)); yield break; }
        var esquerda = TempoMidia.Maximo(TempoMidia.Zero, Inicio - posicao);
        var direita = TempoMidia.Maximo(TempoMidia.Zero, fim - Fim);
        if (esquerda > TempoMidia.Zero) yield return new(clipe with
        {
            Duracao = esquerda,
            FadeInAudio = TempoMidia.Minimo(clipe.FadeInAudio, esquerda), FadeOutAudio = TempoMidia.Zero,
            FadeInVideo = TempoMidia.Minimo(clipe.FadeInVideo, esquerda), FadeOutVideo = TempoMidia.Zero
        }, posicao);
        if (direita > TempoMidia.Zero) yield return new(clipe with
        {
            Id = esquerda > TempoMidia.Zero ? Guid.NewGuid() : clipe.Id,
            InicioNaOrigem = clipe.InicioNaOrigem + (clipe.Duracao - direita), Duracao = direita,
            FadeInAudio = TempoMidia.Zero, FadeOutAudio = TempoMidia.Minimo(clipe.FadeOutAudio, direita),
            FadeInVideo = TempoMidia.Zero, FadeOutVideo = TempoMidia.Minimo(clipe.FadeOutVideo, direita)
        }, Inicio);
    }
}

internal static class ImmutableArrayExtensions
{
    public static int FindIndex<T>(this ImmutableArray<T> itens, Func<T, bool> correspondencia)
    {
        for (var i = 0; i < itens.Length; i++) if (correspondencia(itens[i])) return i;
        return -1;
    }
}
