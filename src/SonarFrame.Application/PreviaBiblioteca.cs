using SonarFrame.Core;

namespace SonarFrame.Application;

public enum EstadoPrevia { Vazia, Preparando, Imagem, Reproduzindo, Pausada, Concluida, Erro }
public sealed record SituacaoPrevia(EstadoPrevia Estado, Midia? Midia = null, string? Mensagem = null);
public sealed record EventoReprodutor(bool Concluido, string? Erro = null);

public interface IReprodutorMidia : IAsyncDisposable
{
    event Action<EventoReprodutor>? Alterado;
    TempoMidia Posicao { get; }
    Task CarregarAsync(CancellationToken token);
    async Task CarregarPausadoAsync(CancellationToken token) { await CarregarAsync(token); await PausarAsync(token); }
    Task ReproduzirAsync(CancellationToken token);
    Task PausarAsync(CancellationToken token);
    Task PosicionarAsync(TempoMidia posicao, CancellationToken token);
}

public interface IFabricaReprodutorMidia : IAsyncDisposable
{
    Task<IReprodutorMidia> CriarAsync(Midia midia, CancellationToken token);
}

public sealed class PreviaBiblioteca(IFabricaReprodutorMidia fabrica) : IAsyncDisposable
{
    private readonly SemaphoreSlim operacoes = new(1, 1);
    private readonly object bloqueio = new();
    private CancellationTokenSource? carregamento;
    private IReprodutorMidia? reprodutor;
    private SituacaoPrevia atual = new(EstadoPrevia.Vazia);
    private long geracao;
    private long eventosMotor;
    private bool encerrada;
    public event Action<SituacaoPrevia>? Alterado;
    public SituacaoPrevia Atual { get { lock (bloqueio) return atual; } }
    public TempoMidia Posicao { get { lock (bloqueio) return reprodutor?.Posicao ?? TempoMidia.Zero; } }

    private void Publicar(long versao, SituacaoPrevia situacao)
    {
        lock (bloqueio)
        {
            if (versao != geracao || encerrada) return;
            atual = situacao;
        }
        Alterado?.Invoke(situacao);
    }

    public async Task AbrirAsync(Midia midia, TempoMidia? posicaoInicial = null, bool reproduzir = true)
    {
        CancellationTokenSource token;
        long versao;
        lock (bloqueio)
        {
            ObjectDisposedException.ThrowIf(encerrada, this);
            carregamento?.Cancel();
            token = new CancellationTokenSource(); carregamento = token; versao = ++geracao;
        }
        Publicar(versao, new(EstadoPrevia.Preparando, midia));
        await operacoes.WaitAsync();
        IReprodutorMidia? criado = null;
        try
        {
            token.Token.ThrowIfCancellationRequested();
            await LiberarReprodutorAsync();
            var novo = await fabrica.CriarAsync(midia, token.Token);
            criado = novo;
            lock (bloqueio) reprodutor = novo;
            novo.Alterado += evento =>
            {
                lock (bloqueio) { if (versao != geracao || encerrada) return; eventosMotor++; }
                if (evento.Erro is not null) Publicar(versao, new(EstadoPrevia.Erro, midia, evento.Erro));
                else if (evento.Concluido) Publicar(versao, new(EstadoPrevia.Concluida, midia));
            };
            if (midia.Tipo == TipoMidia.Imagem) await novo.CarregarAsync(token.Token);
            else await novo.CarregarPausadoAsync(token.Token);
            if (posicaoInicial is { } posicao) await novo.PosicionarAsync(posicao, token.Token);
            if (reproduzir && midia.Tipo != TipoMidia.Imagem) await novo.ReproduzirAsync(token.Token);
            token.Token.ThrowIfCancellationRequested();
            if (Atual.Estado == EstadoPrevia.Preparando)
                Publicar(versao, new(midia.Tipo == TipoMidia.Imagem ? EstadoPrevia.Imagem : reproduzir ? EstadoPrevia.Reproduzindo : EstadoPrevia.Pausada, midia));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (criado is not null || versao == Interlocked.Read(ref geracao)) await LiberarReprodutorAsync();
            Publicar(versao, new(EstadoPrevia.Vazia, Mensagem: "Prévia cancelada."));
        }
        catch (Exception erro)
        {
            if (criado is not null) await LiberarReprodutorAsync();
            Publicar(versao, new(EstadoPrevia.Erro, midia, erro.Message));
        }
        finally
        {
            lock (bloqueio) { if (ReferenceEquals(carregamento, token)) carregamento = null; }
            token.Dispose(); operacoes.Release();
        }
    }

    public void Cancelar() { lock (bloqueio) carregamento?.Cancel(); }

    public Task AlternarAsync() => ComReprodutorAsync(async (motor, situacao, token) =>
    {
        if (situacao.Estado == EstadoPrevia.Reproduzindo) { await motor.PausarAsync(token); return EstadoPrevia.Pausada; }
        if (situacao.Estado == EstadoPrevia.Concluida) await motor.PosicionarAsync(TempoMidia.Zero, token);
        await motor.ReproduzirAsync(token); return EstadoPrevia.Reproduzindo;
    });

    public Task PausarAsync() => ComReprodutorAsync(async (motor, situacao, token) =>
    {
        if (situacao.Estado != EstadoPrevia.Reproduzindo) return situacao.Estado;
        await motor.PausarAsync(token); return EstadoPrevia.Pausada;
    });

    public Task PosicionarAsync(TempoMidia posicao) => ComReprodutorAsync(async (motor, situacao, token) =>
    {
        if (posicao < TempoMidia.Zero || situacao.Midia?.Duracao is not { } duracao || posicao >= duracao)
            throw new ArgumentException("Informe uma posição dentro da duração da mídia.");
        await motor.PausarAsync(token);
        await motor.PosicionarAsync(posicao, token); return EstadoPrevia.Pausada;
    });

    public Task PararAsync() => PosicionarAsync(TempoMidia.Zero);

    private async Task ComReprodutorAsync(Func<IReprodutorMidia, SituacaoPrevia, CancellationToken, Task<EstadoPrevia>> acao)
    {
        var versao = Interlocked.Read(ref geracao);
        await operacoes.WaitAsync();
        try
        {
            if (versao != geracao || encerrada || reprodutor is null || Atual.Estado is not (EstadoPrevia.Reproduzindo or EstadoPrevia.Pausada or EstadoPrevia.Concluida)) return;
            var situacao = Atual;
            var eventosAntes = Interlocked.Read(ref eventosMotor);
            var estado = await acao(reprodutor, situacao, CancellationToken.None);
            if (eventosAntes == Interlocked.Read(ref eventosMotor)) Publicar(versao, situacao with { Estado = estado, Mensagem = null });
        }
        catch (ArgumentException erro) { Publicar(versao, Atual with { Mensagem = erro.Message }); }
        catch (Exception erro)
        {
            await LiberarReprodutorAsync(); Publicar(versao, Atual with { Estado = EstadoPrevia.Erro, Mensagem = erro.Message });
        }
        finally { operacoes.Release(); }
    }

    public async Task LimparAsync()
    {
        long versao;
        lock (bloqueio) { carregamento?.Cancel(); versao = ++geracao; }
        await operacoes.WaitAsync();
        try
        {
            if (versao != Interlocked.Read(ref geracao)) return;
            await LiberarReprodutorAsync(); Publicar(versao, new(EstadoPrevia.Vazia));
        }
        finally { operacoes.Release(); }
    }

    private async Task LiberarReprodutorAsync()
    {
        IReprodutorMidia? anterior;
        lock (bloqueio) { anterior = reprodutor; reprodutor = null; }
        if (anterior is not null) await anterior.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        lock (bloqueio) { if (encerrada) return; encerrada = true; }
        await LimparAsync(); await fabrica.DisposeAsync();
    }
}
