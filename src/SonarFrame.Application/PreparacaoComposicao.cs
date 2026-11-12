using SonarFrame.Core;

namespace SonarFrame.Application;

public sealed record QuadroComposicao(TempoMidia Posicao, byte[] Png);

public interface IPreparadorComposicao : IAsyncDisposable
{
    Task<IFonteComposicao?> IniciarReproducaoAsync(Projeto projeto, TempoMidia inicio, TempoMidia fim, IProgress<string>? progresso, CancellationToken token)
        => Task.FromResult<IFonteComposicao?>(null);
    Task<Midia> PrepararAsync(Projeto projeto, IProgress<string>? progresso, CancellationToken token);
    Task<bool> EstaAtualizadaAsync(Projeto projeto, CancellationToken token) => Task.FromResult(false);
    Task<Midia?> PrepararIntervaloAsync(Projeto projeto, TempoMidia inicio, TempoMidia fim, CancellationToken token) => Task.FromResult<Midia?>(null);
    Task<QuadroComposicao?> ObterQuadroAsync(Projeto projeto, TempoMidia posicao, CancellationToken token) => Task.FromResult<QuadroComposicao?>(null);
    Task<TempoMidia> ProximaPosicaoAsync(Projeto projeto, int direcao, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var sessao = new SessaoEdicao(projeto);
        sessao.AvancarQuadro(direcao);
        return Task.FromResult(sessao.Atual.Cursor);
    }
}
