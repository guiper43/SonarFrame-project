using SonarFrame.Application;
using SonarFrame.Core;
using Xunit;

namespace SonarFrame.Tests;

public sealed class TestesPreviaBiblioteca
{
    [Fact]
    public async Task PausaBuscaEReinicioMantemPosicaoPropria()
    {
        var fabrica = new Fabrica();
        await using var previa = new PreviaBiblioteca(fabrica);
        await previa.AbrirAsync(Midia());
        Assert.Equal(EstadoPrevia.Reproduzindo, previa.Atual.Estado);
        await previa.AlternarAsync();
        Assert.Equal(EstadoPrevia.Pausada, previa.Atual.Estado);
        await previa.PosicionarAsync(TempoMidia.Segundos(3));
        Assert.Equal(TempoMidia.Segundos(3), previa.Posicao);
        fabrica.Criados[0].Emitir(new(true));
        Assert.Equal(EstadoPrevia.Concluida, previa.Atual.Estado);
        await previa.AlternarAsync();
        Assert.Equal(TempoMidia.Zero, previa.Posicao);
        Assert.Equal(EstadoPrevia.Reproduzindo, previa.Atual.Estado);
        await previa.PararAsync();
        Assert.Equal(EstadoPrevia.Pausada, previa.Atual.Estado);
        Assert.Equal(TempoMidia.Zero, previa.Posicao);
    }

    [Fact]
    public async Task ImagemNaoExecutaComandosTemporais()
    {
        var fabrica = new Fabrica();
        await using var previa = new PreviaBiblioteca(fabrica);
        await previa.AbrirAsync(Midia(TipoMidia.Imagem));
        await previa.AlternarAsync(); await previa.PararAsync(); await previa.PosicionarAsync(TempoMidia.Segundos(2));
        Assert.Equal(EstadoPrevia.Imagem, previa.Atual.Estado);
        Assert.Equal(0, fabrica.Criados[0].Comandos);
    }

    [Fact]
    public async Task TrocaLiberaAnteriorEIgnoraSeusEventosTardios()
    {
        var fabrica = new Fabrica();
        await using var previa = new PreviaBiblioteca(fabrica);
        await previa.AbrirAsync(Midia()); var anterior = fabrica.Criados[0];
        var atual = Midia(TipoMidia.Audio); await previa.AbrirAsync(atual);
        Assert.True(anterior.Liberado);
        anterior.Emitir(new(true)); anterior.Emitir(new(false, "Falha antiga"));
        Assert.Equal(atual, previa.Atual.Midia);
        Assert.Equal(EstadoPrevia.Reproduzindo, previa.Atual.Estado);
    }

    [Fact]
    public async Task CancelamentoDuranteCarregamentoLiberaMotorENaoPublicaReproducao()
    {
        var iniciou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fabrica = new Fabrica { Carregar = async token => { iniciou.SetResult(); await Task.Delay(Timeout.Infinite, token); } };
        await using var previa = new PreviaBiblioteca(fabrica);
        var estados = new List<EstadoPrevia>(); previa.Alterado += s => estados.Add(s.Estado);
        var abertura = previa.AbrirAsync(Midia()); await iniciou.Task;
        previa.Cancelar(); await abertura.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(EstadoPrevia.Vazia, previa.Atual.Estado);
        Assert.DoesNotContain(EstadoPrevia.Reproduzindo, estados);
        Assert.True(fabrica.Criados.Single().Liberado);
    }

    [Fact]
    public async Task UltimaAberturaPrevaleceMesmoSeMotorIgnoraCancelamento()
    {
        var liberacao = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fabrica = new Fabrica { Carregar = _ => liberacao.Task };
        await using var previa = new PreviaBiblioteca(fabrica);
        var primeira = previa.AbrirAsync(Midia());
        fabrica.Carregar = _ => Task.CompletedTask;
        var ultimaMidia = Midia(TipoMidia.Audio);
        var segunda = previa.AbrirAsync(ultimaMidia);
        liberacao.SetResult(); await Task.WhenAll(primeira, segunda);
        Assert.True(fabrica.Criados[0].Liberado);
        Assert.Equal(ultimaMidia, previa.Atual.Midia);
        Assert.Equal(EstadoPrevia.Reproduzindo, previa.Atual.Estado);
    }

    [Fact]
    public async Task FalhaPermiteNovaAberturaEBuscaInvalidaPreservaMotor()
    {
        var fabrica = new Fabrica { Carregar = _ => Task.FromException(new IOException("Arquivo inválido")) };
        await using var previa = new PreviaBiblioteca(fabrica);
        await previa.AbrirAsync(Midia());
        Assert.Equal(EstadoPrevia.Erro, previa.Atual.Estado); Assert.True(fabrica.Criados[0].Liberado);
        fabrica.Carregar = _ => Task.CompletedTask;
        await previa.AbrirAsync(Midia());
        await previa.PosicionarAsync(TempoMidia.Segundos(100));
        Assert.Equal(EstadoPrevia.Reproduzindo, previa.Atual.Estado);
        Assert.Contains("duração", previa.Atual.Mensagem);
        Assert.False(fabrica.Criados[1].Liberado);
    }

    [Fact]
    public async Task FecharDuranteAberturaCancelaDescartaEIgnoraEventos()
    {
        var fabrica = new Fabrica { Carregar = token => Task.Delay(Timeout.Infinite, token) };
        var previa = new PreviaBiblioteca(fabrica);
        var abertura = previa.AbrirAsync(Midia());
        await previa.DisposeAsync(); await abertura;
        Assert.True(fabrica.Liberada); Assert.True(fabrica.Criados[0].Liberado);
        var anterior = previa.Atual;
        fabrica.Criados[0].Emitir(new(true)); Assert.Same(anterior, previa.Atual);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => previa.AbrirAsync(Midia()));
    }

    [Fact]
    public async Task MidiaCurtaNaoSobrescreveEventoDeConclusaoAoCarregar()
    {
        var fabrica = new Fabrica();
        fabrica.Carregar = _ => { fabrica.Criados[0].Emitir(new(true)); return Task.CompletedTask; };
        await using var previa = new PreviaBiblioteca(fabrica);
        await previa.AbrirAsync(Midia()); Assert.Equal(EstadoPrevia.Concluida, previa.Atual.Estado);
    }

    private static Midia Midia(TipoMidia tipo = TipoMidia.Video) => new(Guid.NewGuid(), "Mídia", "teste", tipo, tipo == TipoMidia.Imagem ? null : TempoMidia.Segundos(10));

    [Fact]
    public async Task ConclusaoDuranteRetomadaNaoEhSobrescritaPorReproduzindo()
    {
        var fabrica = new Fabrica();
        await using var previa = new PreviaBiblioteca(fabrica);
        await previa.AbrirAsync(Midia()); await previa.PausarAsync();
        fabrica.Criados[0].AoReproduzir = () => fabrica.Criados[0].Emitir(new(true));
        await previa.AlternarAsync(); Assert.Equal(EstadoPrevia.Concluida, previa.Atual.Estado);
    }
    private sealed class Fabrica : IFabricaReprodutorMidia
    {
        public List<Motor> Criados { get; } = [];
        public Func<CancellationToken, Task> Carregar { get; set; } = _ => Task.CompletedTask;
        public bool Liberada { get; private set; }
        public Task<IReprodutorMidia> CriarAsync(Midia midia, CancellationToken token)
        {
            Assert.All(Criados, motor => Assert.True(motor.Liberado));
            var motor = new Motor(Carregar); Criados.Add(motor); return Task.FromResult<IReprodutorMidia>(motor);
        }
        public ValueTask DisposeAsync() { Liberada = true; return ValueTask.CompletedTask; }
    }
    private sealed class Motor(Func<CancellationToken, Task> carregar) : IReprodutorMidia
    {
        public event Action<EventoReprodutor>? Alterado;
        public TempoMidia Posicao { get; private set; }
        public bool Liberado { get; private set; }
        public int Comandos { get; private set; }
        public Action? AoReproduzir { get; set; }
        public void Emitir(EventoReprodutor evento) => Alterado?.Invoke(evento);
        public Task CarregarAsync(CancellationToken token) => carregar(token);
        public Task ReproduzirAsync(CancellationToken token) { Comandos++; AoReproduzir?.Invoke(); return Task.CompletedTask; }
        public Task PausarAsync(CancellationToken token) { Comandos++; return Task.CompletedTask; }
        public Task PosicionarAsync(TempoMidia posicao, CancellationToken token) { Posicao = posicao; Comandos++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Liberado = true; return ValueTask.CompletedTask; }
    }
}
