namespace SonarFrame.Core;

public sealed record AtualizarCamada(Guid ClipeId, TempoMidia Inicio, TempoMidia InicioNaOrigem, TempoMidia Duracao) : IComandoEdicao
{
    public Projeto Aplicar(Projeto p)
    {
        var indice = p.Camadas.FindIndex(l => l.Clipe.Id == ClipeId);
        if (indice < 0) throw new ArgumentException("Camada não encontrada.");
        var camada = p.Camadas[indice];
        var duracaoValida = TempoMidia.Maximo(TempoMidia.Zero, Duracao);
        var fadeInAudio = TempoMidia.Minimo(camada.Clipe.FadeInAudio, duracaoValida);
        var fadeOutAudio = TempoMidia.Minimo(camada.Clipe.FadeOutAudio, duracaoValida - fadeInAudio);
        var fadeInVideo = TempoMidia.Minimo(camada.Clipe.FadeInVideo, duracaoValida);
        var fadeOutVideo = TempoMidia.Minimo(camada.Clipe.FadeOutVideo, duracaoValida - fadeInVideo);
        return p with
        {
            Camadas = p.Camadas.SetItem(indice, camada with
            {
                Inicio = Inicio,
                Clipe = camada.Clipe with
                {
                    InicioNaOrigem = InicioNaOrigem,
                    Duracao = Duracao,
                    FadeInAudio = fadeInAudio,
                    FadeOutAudio = fadeOutAudio,
                    FadeInVideo = fadeInVideo,
                    FadeOutVideo = fadeOutVideo
                }
            })
        };
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
