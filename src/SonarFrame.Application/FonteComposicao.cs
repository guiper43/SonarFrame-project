using SonarFrame.Core;

namespace SonarFrame.Application;

public interface IFonteComposicao : IAsyncDisposable
{
    TempoMidia Inicio { get; }
    TempoMidia Duracao { get; }
    TempoMidia PreparadoAte { get; }
    Task Conclusao { get; }
    Stream AbrirLeitura();
    void AtualizarPosicao(TempoMidia posicao);
    Task AguardarReservaAsync(TempoMidia posicao, TempoMidia reserva, CancellationToken token);
    void Cancelar();
}
