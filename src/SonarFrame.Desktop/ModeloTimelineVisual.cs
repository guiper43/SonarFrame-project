using System.Numerics;
using SonarFrame.Core;

namespace SonarFrame.Desktop;

internal sealed record ItemTimelineVisual(Guid Id, string Nome, string Tipo, TempoMidia Inicio, TempoMidia Duracao, int Faixa, int Indice);

internal sealed record ModeloTimelineVisual(TempoMidia Duracao, TempoMidia Cursor, SelecaoTemporal? Selecao, Guid? Selecionado, IReadOnlyList<ItemTimelineVisual> Itens)
{
    public static ModeloTimelineVisual Criar(Projeto projeto)
    {
        var itens = new List<ItemTimelineVisual>();
        var inicio = TempoMidia.Zero;
        string Tipo(Clipe clipe) => projeto.Biblioteca.Single(m => m.Id == clipe.MidiaId).Tipo switch
        {
            TipoMidia.Audio => "Áudio", TipoMidia.Video => "Vídeo", _ => "Imagem"
        };
        foreach (var clipe in projeto.LinhaDoTempo)
        {
            itens.Add(new(clipe.Id, clipe.Nome, Tipo(clipe), inicio, clipe.Duracao, 0, itens.Count));
            inicio += clipe.Duracao;
        }
        for (var i = 0; i < projeto.Camadas.Length; i++)
        {
            var camada = projeto.Camadas[i];
            itens.Add(new(camada.Clipe.Id, camada.Clipe.Nome, Tipo(camada.Clipe), camada.Inicio, camada.Clipe.Duracao, i + 1, i));
        }
        return new(projeto.Duracao, projeto.Cursor, projeto.Selecao, projeto.ItemSelecionadoId, itens);
    }

    public static TempoMidia TempoNaPosicao(double posicao, double largura, TempoMidia duracao)
    {
        if (!double.IsFinite(posicao) || !double.IsFinite(largura) || largura <= 0 || duracao <= TempoMidia.Zero) return TempoMidia.Zero;
        var proporcao = Math.Clamp(posicao / largura, 0, 1);
        for (var escala = 1_000_000; ; escala /= 10)
        {
            var unidades = new BigInteger(Math.Round(proporcao * escala));
            var numerador = (BigInteger)duracao.Numerador * unidades;
            var denominador = (BigInteger)duracao.Denominador * escala;
            var divisor = BigInteger.GreatestCommonDivisor(numerador, denominador);
            numerador /= divisor; denominador /= divisor;
            if (numerador <= long.MaxValue && denominador <= long.MaxValue)
                return new((long)numerador, (long)denominador);
        }
    }

    public static double PosicaoNoTempo(TempoMidia tempo, double largura, TempoMidia duracao)
        => duracao <= TempoMidia.Zero ? 0 : Math.Clamp((double)tempo.Numerador / tempo.Denominador / ((double)duracao.Numerador / duracao.Denominador), 0, 1) * largura;

    public int DestinoReordenacao(Guid id, TempoMidia tempo)
    {
        var principal = Itens.Where(i => i.Faixa == 0).ToArray();
        var origem = Array.FindIndex(principal, i => i.Id == id);
        if (origem < 0) return -1;
        var destino = principal.Count(i =>
        {
            var deslocamento = tempo - i.Inicio;
            return (BigInteger)deslocamento.Numerador * i.Duracao.Denominador * 2
                >= (BigInteger)i.Duracao.Numerador * deslocamento.Denominador;
        });
        if (destino > origem) destino--;
        return Math.Clamp(destino, 0, principal.Length - 1);
    }
}
