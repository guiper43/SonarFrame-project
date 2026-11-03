using SonarFrame.Application;
using SonarFrame.Core;
using Xunit;

namespace SonarFrame.Tests;

public sealed class TestesRecuperacaoReproducao
{
    [Fact]
    public async Task RetomadaManualReativaMonitorMesmoSemCompletarReservaAnterior()
    {
        var fonte = new Fonte();
        var motor = new Motor();
        await using var reprodutor = new ReprodutorComposicao(motor, fonte);
        var primeira = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var segunda = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pausas = 0;
        reprodutor.Alterado += e =>
        {
            if (e.Estado == EstadoPrevia.Preparando)
            {
                if (Interlocked.Increment(ref pausas) == 1) primeira.TrySetResult();
                else segunda.TrySetResult();
            }
        };
        await reprodutor.CarregarPausadoAsync(CancellationToken.None);
        await reprodutor.ReproduzirAsync(CancellationToken.None);
        motor.Posicao = TempoMidia.Segundos(3);
        await primeira.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await reprodutor.PausarAsync(CancellationToken.None);
        await fonte.EsperaCancelada.Task.WaitAsync(TimeSpan.FromSeconds(3));
        fonte.Liberar(TempoMidia.Segundos(6));
        await reprodutor.ReproduzirAsync(CancellationToken.None);
        motor.Posicao = TempoMidia.Segundos(4);
        await segunda.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(motor.Pausado);
        Assert.Equal(2, motor.Inicios);
    }

    [Fact]
    public async Task LimparPreviaCancelaRetomadaManualQueAguardaReserva()
    {
        var fonte = new Fonte();
        var motor = new Motor();
        await using var previa = new PreviaBiblioteca(new Fabrica(motor));
        var preparando = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        previa.Alterado += e => { if (e.Mensagem?.Contains("continuará") == true) preparando.TrySetResult(); };
        await previa.AbrirAsync(new(Guid.NewGuid(), "Composição", "", TipoMidia.Video, fonte.Duracao), fonte: fonte);
        motor.Posicao = TempoMidia.Segundos(3);
        await preparando.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await previa.PausarAsync();
        var retomada = previa.AlternarAsync();
        Assert.False(retomada.IsCompleted);
        await previa.LimparAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await retomada;
        Assert.Equal(EstadoPrevia.Vazia, previa.Atual.Estado);
        Assert.True(motor.Liberado);
        Assert.Equal(1, motor.Inicios);
    }

    [Fact]
    public async Task ReservaInsuficientePausaERetomaSemAlterarPosicao()
    {
        var fonte = new Fonte();
        var motor = new Motor();
        await using var reprodutor = new ReprodutorComposicao(motor, fonte);
        var preparando = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retomou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reprodutor.Alterado += e => { if (e.Estado == EstadoPrevia.Preparando) preparando.TrySetResult(); if (e.Estado == EstadoPrevia.Reproduzindo) retomou.TrySetResult(); };
        await reprodutor.CarregarPausadoAsync(CancellationToken.None);
        await reprodutor.ReproduzirAsync(CancellationToken.None);
        motor.Posicao = TempoMidia.Segundos(3);
        await preparando.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(motor.Pausado);
        Assert.Equal(TempoMidia.Segundos(3), reprodutor.Posicao);
        Assert.False(retomou.Task.IsCompleted);
        fonte.Liberar(TempoMidia.Segundos(10));
        await retomou.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(motor.Pausado);
        Assert.Equal(2, motor.Inicios);
        Assert.Equal(TempoMidia.Segundos(3), reprodutor.Posicao);
    }

    [Fact]
    public async Task PausaDoUsuarioDuranteRecuperacaoImpedeRetomadaAutomatica()
    {
        var fonte = new Fonte();
        var motor = new Motor();
        await using var reprodutor = new ReprodutorComposicao(motor, fonte);
        var preparando = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reprodutor.Alterado += e => { if (e.Estado == EstadoPrevia.Preparando) preparando.TrySetResult(); };
        await reprodutor.CarregarPausadoAsync(CancellationToken.None);
        await reprodutor.ReproduzirAsync(CancellationToken.None);
        motor.Posicao = TempoMidia.Segundos(3);
        await preparando.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await reprodutor.PausarAsync(CancellationToken.None);
        fonte.Liberar(TempoMidia.Segundos(10));
        await fonte.EsperaCancelada.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await reprodutor.DisposeAsync();
        Assert.True(motor.Pausado);
        Assert.Equal(1, motor.Inicios);
        Assert.True(fonte.Cancelada);
    }

    [Fact]
    public async Task DescarteCancelaEsperaDeReservaESemRetomadaTardia()
    {
        var fonte = new Fonte();
        var motor = new Motor();
        var reprodutor = new ReprodutorComposicao(motor, fonte);
        var preparando = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reprodutor.Alterado += e => { if (e.Estado == EstadoPrevia.Preparando) preparando.TrySetResult(); };
        await reprodutor.CarregarPausadoAsync(CancellationToken.None);
        await reprodutor.ReproduzirAsync(CancellationToken.None);
        motor.Posicao = TempoMidia.Segundos(3);
        await preparando.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await reprodutor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        fonte.Liberar(TempoMidia.Segundos(10));
        Assert.True(fonte.Cancelada);
        Assert.True(motor.Liberado);
        Assert.Equal(1, motor.Inicios);
    }

    private sealed class Motor : IReprodutorMidia
    {
        public event Action<EventoReprodutor>? Alterado { add { } remove { } }
        public TempoMidia Posicao { get; set; }
        public bool Pausado { get; private set; }
        public bool Liberado { get; private set; }
        public int Inicios { get; private set; }
        public Task CarregarAsync(CancellationToken token) => Task.CompletedTask;
        public Task ReproduzirAsync(CancellationToken token) { Pausado = false; Inicios++; return Task.CompletedTask; }
        public Task PausarAsync(CancellationToken token) { Pausado = true; return Task.CompletedTask; }
        public Task PosicionarAsync(TempoMidia posicao, CancellationToken token) { Posicao = posicao; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Liberado = true; return ValueTask.CompletedTask; }
    }

    private sealed class Fabrica(Motor motor) : IFabricaReprodutorMidia
    {
        public Task<IReprodutorMidia> CriarAsync(Midia midia, CancellationToken token) => Task.FromResult<IReprodutorMidia>(motor);
        public Task<IReprodutorMidia> CriarAsync(Midia midia, IFonteComposicao fonte, CancellationToken token) => Task.FromResult<IReprodutorMidia>(new ReprodutorComposicao(motor, fonte));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Fonte : IFonteComposicao
    {
        private readonly object bloqueio = new();
        private TaskCompletionSource reserva = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource EsperaCancelada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TempoMidia Inicio => TempoMidia.Zero;
        public TempoMidia Duracao => TempoMidia.Segundos(30);
        public TempoMidia PreparadoAte { get; private set; } = TempoMidia.Segundos(5);
        public Task Conclusao => Task.CompletedTask;
        public bool Cancelada { get; private set; }
        public Stream AbrirLeitura() => Stream.Null;
        public void AtualizarPosicao(TempoMidia posicao) { }
        public void Liberar(TempoMidia ate)
        {
            lock (bloqueio)
            {
                PreparadoAte = ate;
                var anterior = reserva;
                reserva = new(TaskCreationOptions.RunContinuationsAsynchronously);
                anterior.TrySetResult();
            }
        }
        public async Task AguardarReservaAsync(TempoMidia posicao, TempoMidia quantidade, CancellationToken token)
        {
            try
            {
                while (true)
                {
                    Task espera;
                    lock (bloqueio)
                    {
                        token.ThrowIfCancellationRequested();
                        if (posicao + quantidade <= PreparadoAte) return;
                        espera = reserva.Task;
                    }
                    await espera.WaitAsync(token);
                }
            }
            catch (OperationCanceledException) { EsperaCancelada.TrySetResult(); throw; }
        }
        public void Cancelar() => Cancelada = true;
        public ValueTask DisposeAsync() { Cancelar(); return ValueTask.CompletedTask; }
    }
}
