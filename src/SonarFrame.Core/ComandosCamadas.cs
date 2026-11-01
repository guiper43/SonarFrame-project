namespace SonarFrame.Core;

public sealed record AtualizarCamada(Guid ClipeId, TempoMidia Inicio, TempoMidia InicioNaOrigem, TempoMidia Duracao) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        var indice = p.Camadas.FindIndex(l => l.Clipe.Id == ClipeId);
        if (indice < 0) throw new ArgumentException("Camada não encontrada.");
        var camada = p.Camadas[indice];
        return p with { Camadas = p.Camadas.SetItem(indice, camada with
        { Inicio = Inicio, Clipe = camada.Clipe with { InicioNaOrigem = InicioNaOrigem, Duracao = Duracao } }) };
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
