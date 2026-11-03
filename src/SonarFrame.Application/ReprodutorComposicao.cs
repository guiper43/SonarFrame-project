using SonarFrame.Core;

namespace SonarFrame.Application;

public sealed class ReprodutorComposicao(IReprodutorMidia motor, IFonteComposicao fonte) : IReprodutorMidia
{
    private readonly SemaphoreSlim operacoes = new(1, 1);
    private readonly CancellationTokenSource vida = new();
    private Task monitor = Task.CompletedTask;
    private CancellationTokenSource? recuperacao;
    private bool iniciado;
    private volatile bool reproduzindo;
    private bool liberado;
    private long geracao;
    public event Action<EventoReprodutor>? Alterado;
    public TempoMidia Posicao => motor.Posicao;

    private void AoAlterar(EventoReprodutor evento)
    {
        if (vida.IsCancellationRequested) return;
        if (evento.Concluido) reproduzindo = false;
        Alterado?.Invoke(evento);
    }

    public async Task CarregarAsync(CancellationToken token)
    {
        await CarregarPausadoAsync(token).ConfigureAwait(false);
        await ReproduzirAsync(token).ConfigureAwait(false);
    }

    public async Task CarregarPausadoAsync(CancellationToken token)
    {
        motor.Alterado += AoAlterar;
        using var registro = token.Register(fonte.Cancelar);
        await motor.CarregarPausadoAsync(token).ConfigureAwait(false);
    }

    public async Task ReproduzirAsync(CancellationToken token)
    {
        await operacoes.WaitAsync(token).ConfigureAwait(false);
        try
        {
            recuperacao?.Cancel();
            fonte.AtualizarPosicao(Posicao);
            await fonte.AguardarReservaAsync(Posicao, TempoMidia.Segundos(3), token).ConfigureAwait(false);
            await motor.ReproduzirAsync(token).ConfigureAwait(false);
            geracao++;
            reproduzindo = true;
            if (!iniciado) { iniciado = true; monitor = MonitorarAsync(); }
        }
        finally { operacoes.Release(); }
    }

    public async Task PausarAsync(CancellationToken token)
    {
        await operacoes.WaitAsync(token).ConfigureAwait(false);
        try { reproduzindo = false; geracao++; recuperacao?.Cancel(); await motor.PausarAsync(token).ConfigureAwait(false); }
        finally { operacoes.Release(); }
    }

    public Task PosicionarAsync(TempoMidia posicao, CancellationToken token) => motor.PosicionarAsync(posicao, token);

    private async Task MonitorarAsync()
    {
        var token = vida.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(50, token).ConfigureAwait(false);
                if (fonte.Conclusao.IsFaulted) await fonte.Conclusao.ConfigureAwait(false);
                var posicao = Posicao;
                fonte.AtualizarPosicao(posicao);
                long versao;
                CancellationTokenSource esperaReserva;
                await operacoes.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (!reproduzindo || fonte.PreparadoAte >= fonte.Duracao || fonte.PreparadoAte - posicao > TempoMidia.Segundos(2)) continue;
                    await motor.PausarAsync(token).ConfigureAwait(false);
                    versao = geracao;
                    esperaReserva = CancellationTokenSource.CreateLinkedTokenSource(token);
                    recuperacao = esperaReserva;
                    Alterado?.Invoke(new(false, Estado: EstadoPrevia.Preparando));
                }
                finally { operacoes.Release(); }
                try { await fonte.AguardarReservaAsync(Posicao, TempoMidia.Segundos(5), esperaReserva.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (esperaReserva.IsCancellationRequested) { continue; }
                finally
                {
                    await operacoes.WaitAsync().ConfigureAwait(false);
                    try { if (ReferenceEquals(recuperacao, esperaReserva)) recuperacao = null; esperaReserva.Dispose(); }
                    finally { operacoes.Release(); }
                }
                await operacoes.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (reproduzindo && versao == geracao)
                    {
                        await motor.ReproduzirAsync(token).ConfigureAwait(false);
                        Alterado?.Invoke(new(false, Estado: EstadoPrevia.Reproduzindo));
                    }
                }
                finally { operacoes.Release(); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception erro)
        {
            if (!token.IsCancellationRequested) Alterado?.Invoke(new(false, "Não foi possível continuar a reprodução: " + erro.Message));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (liberado) return;
        liberado = true;
        vida.Cancel(); fonte.Cancelar();
        await monitor.ConfigureAwait(false);
        motor.Alterado -= AoAlterar;
        try { await motor.DisposeAsync().ConfigureAwait(false); }
        finally { await fonte.DisposeAsync().ConfigureAwait(false); vida.Dispose(); operacoes.Dispose(); }
    }
}
