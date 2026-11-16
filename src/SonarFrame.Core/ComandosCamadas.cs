using System.Collections.Immutable;

namespace SonarFrame.Core;

public sealed record AtualizarCamada(Guid ClipeId, TempoMidia Inicio, TempoMidia InicioNaOrigem, TempoMidia Duracao, LayoutVisual? Layout = null) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        var indice = p.Camadas.FindIndex(l => l.Clipe.Id == ClipeId);
        if (indice < 0) throw new ArgumentException("Camada não encontrada.");
        var camada = p.Camadas[indice];
        return p with { Camadas = p.Camadas.SetItem(indice, camada with
        { Inicio = Inicio, Clipe = camada.Clipe with { InicioNaOrigem = InicioNaOrigem, Duracao = Duracao, Layout = Layout ?? camada.Clipe.Layout } }) };
    }
}
public sealed record DefinirLayoutVisual(Guid ClipeId, LayoutVisual? Layout, TempoMidia? Duracao = null) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        if (!p.LinhaDoTempo.Any(c => c.Id == ClipeId) && !p.Camadas.Any(l => l.Clipe.Id == ClipeId))
            throw new ArgumentException("Clipe não encontrado.");
        var atualizado = p with
        {
            LinhaDoTempo = p.LinhaDoTempo.Select(c => c.Id == ClipeId ? c with { Layout = Layout, Duracao = Duracao ?? c.Duracao } : c).ToImmutableArray(),
            Camadas = p.Camadas.Select(l => l.Clipe.Id == ClipeId ? l with { Clipe = l.Clipe with { Layout = Layout, Duracao = Duracao ?? l.Clipe.Duracao } } : l).ToImmutableArray()
        };
        var duracaoTotal = atualizado.Duracao;
        var cursor = TempoMidia.Minimo(atualizado.Cursor, duracaoTotal);
        SelecaoTemporal? selecao = null;
        if (atualizado.Selecao is { } sel)
        {
            var ancora = TempoMidia.Minimo(sel.Ancora, duracaoTotal);
            var ativa = TempoMidia.Minimo(sel.ExtremidadeAtiva, duracaoTotal);
            if (ancora != ativa) selecao = new(ancora, ativa);
        }
        return atualizado with { Cursor = cursor, Selecao = selecao };
    }
}
public sealed record RemoverCamada(Guid ClipeId) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        var indice = p.Camadas.FindIndex(l => l.Clipe.Id == ClipeId);
        if (indice < 0) throw new ArgumentException("Camada não encontrada.");
        return p with { Camadas = p.Camadas.RemoveAt(indice), ItemSelecionadoId = p.ItemSelecionadoId == ClipeId ? null : p.ItemSelecionadoId };
    }
}
