using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class TestesInjecaoDependencias
{
    [Fact]
    public async Task HostValidaGrafoNoBuildEResolveServicosEssenciaisNoEscopo()
    {
        using var host = RegistroDependencias.CriarHostSonarFrame();
        await host.StartAsync();
        await using var escopo = host.Services.CreateAsyncScope();
        var provedor = escopo.ServiceProvider;

        Assert.Same(TimeProvider.System, provedor.GetRequiredService<TimeProvider>());
        Assert.IsType<RepositorioProjetosSqlite>(provedor.GetRequiredService<IRepositorioProjetos>());
        Assert.IsType<ExecutorFfprobe>(provedor.GetRequiredService<IExecutorFfprobe>());
        Assert.IsType<LeitorMetadadosFfprobe>(provedor.GetRequiredService<ILeitorMetadadosMidia>());
        Assert.IsType<RepositorioConfiguracoes>(provedor.GetRequiredService<RepositorioConfiguracoes>());
        Assert.IsType<CatalogoProjetos>(provedor.GetRequiredService<CatalogoProjetos>());
        Assert.IsType<FabricaPreviaVlc>(provedor.GetRequiredService<IFabricaReprodutorMidia>());
        Assert.IsType<PreviaBiblioteca>(provedor.GetRequiredService<PreviaBiblioteca>());
        Assert.IsType<PreparadorComposicaoFfmpeg>(provedor.GetRequiredService<IPreparadorComposicao>());
        Assert.IsType<ExportadorFfmpeg>(provedor.GetRequiredService<IExportador>());

        await host.StopAsync();
    }

    [Fact]
    public void ValidateScopesImpedeResolucaoDeServicosEscopadosNoProvedorRaiz()
    {
        using var host = RegistroDependencias.CriarHostSonarFrame();

        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<IFabricaReprodutorMidia>());
        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<PreviaBiblioteca>());
        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<IPreparadorComposicao>());
        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<IExportador>());
        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<JanelaPrincipal>());
    }

    [Fact]
    public async Task SingletonsSaoCompartilhadosTransientesRecriadosEEscoposIsolados()
    {
        using var host = RegistroDependencias.CriarHostSonarFrame();
        await using var escopoA = host.Services.CreateAsyncScope();
        await using var escopoB = host.Services.CreateAsyncScope();

        Assert.Same(
            escopoA.ServiceProvider.GetRequiredService<IRepositorioProjetos>(),
            escopoB.ServiceProvider.GetRequiredService<IRepositorioProjetos>());
        Assert.Same(
            escopoA.ServiceProvider.GetRequiredService<IExecutorFfprobe>(),
            escopoB.ServiceProvider.GetRequiredService<IExecutorFfprobe>());
        Assert.Same(
            escopoA.ServiceProvider.GetRequiredService<ILeitorMetadadosMidia>(),
            escopoB.ServiceProvider.GetRequiredService<ILeitorMetadadosMidia>());
        Assert.Same(
            escopoA.ServiceProvider.GetRequiredService<RepositorioConfiguracoes>(),
            escopoB.ServiceProvider.GetRequiredService<RepositorioConfiguracoes>());

        Assert.NotSame(
            escopoA.ServiceProvider.GetRequiredService<CatalogoProjetos>(),
            escopoA.ServiceProvider.GetRequiredService<CatalogoProjetos>());

        var fabricaA1 = escopoA.ServiceProvider.GetRequiredService<IFabricaReprodutorMidia>();
        var fabricaA2 = escopoA.ServiceProvider.GetRequiredService<IFabricaReprodutorMidia>();
        var fabricaB = escopoB.ServiceProvider.GetRequiredService<IFabricaReprodutorMidia>();
        Assert.Same(fabricaA1, fabricaA2);
        Assert.NotSame(fabricaA1, fabricaB);

        var previaA1 = escopoA.ServiceProvider.GetRequiredService<PreviaBiblioteca>();
        var previaA2 = escopoA.ServiceProvider.GetRequiredService<PreviaBiblioteca>();
        var previaB = escopoB.ServiceProvider.GetRequiredService<PreviaBiblioteca>();
        Assert.Same(previaA1, previaA2);
        Assert.NotSame(previaA1, previaB);

        var preparadorA1 = escopoA.ServiceProvider.GetRequiredService<IPreparadorComposicao>();
        var preparadorA2 = escopoA.ServiceProvider.GetRequiredService<IPreparadorComposicao>();
        var preparadorB = escopoB.ServiceProvider.GetRequiredService<IPreparadorComposicao>();
        Assert.Same(preparadorA1, preparadorA2);
        Assert.NotSame(preparadorA1, preparadorB);

        var exportadorA1 = escopoA.ServiceProvider.GetRequiredService<IExportador>();
        var exportadorA2 = escopoA.ServiceProvider.GetRequiredService<IExportador>();
        var exportadorB = escopoB.ServiceProvider.GetRequiredService<IExportador>();
        Assert.Same(exportadorA1, exportadorA2);
        Assert.NotSame(exportadorA1, exportadorB);
    }

    [Fact]
    public async Task DescarteDoEscopoEncerraServicosAssincronosSemAfetarOutroEscopo()
    {
        var fabricaA = new FabricaRastreada();
        var fabricaB = new FabricaRastreada();
        var preparadorA = new PreparadorRastreado();
        var preparadorB = new PreparadorRastreado();
        var indiceFabrica = 0;
        var indicePreparador = 0;

        using var host = RegistroDependencias.CriarHostSonarFrame(configurarServicos: servicos =>
        {
            servicos.AddScoped<IFabricaReprodutorMidia>(_ => Interlocked.Increment(ref indiceFabrica) == 1 ? fabricaA : fabricaB);
            servicos.AddScoped<IPreparadorComposicao>(_ => Interlocked.Increment(ref indicePreparador) == 1 ? preparadorA : preparadorB);
        });

        var escopoA = host.Services.CreateAsyncScope();
        var escopoB = host.Services.CreateAsyncScope();

        var previaA = escopoA.ServiceProvider.GetRequiredService<PreviaBiblioteca>();
        var previaB = escopoB.ServiceProvider.GetRequiredService<PreviaBiblioteca>();
        _ = escopoA.ServiceProvider.GetRequiredService<IPreparadorComposicao>();
        _ = escopoB.ServiceProvider.GetRequiredService<IPreparadorComposicao>();
        var exportadorA = escopoA.ServiceProvider.GetRequiredService<IExportador>();
        var exportadorB = escopoB.ServiceProvider.GetRequiredService<IExportador>();

        await escopoA.DisposeAsync();

        Assert.Equal(1, fabricaA.Descartes);
        Assert.Equal(1, preparadorA.Descartes);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            previaA.AbrirAsync(new Midia(Guid.NewGuid(), "Vídeo", "teste.mp4", TipoMidia.Video, TempoMidia.Segundos(5))));

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            exportadorA.ExportarAsync(Projeto.Criar("Teste"), new OpcoesExportacao("saida.mp4", Orientacao.Horizontal, ModoEnquadramento.Letterbox), new Progress<ProgressoOperacao>()));

        Assert.Equal(0, fabricaB.Descartes);
        Assert.Equal(0, preparadorB.Descartes);

        await escopoB.DisposeAsync();
        Assert.Equal(1, fabricaB.Descartes);
        Assert.Equal(1, preparadorB.Descartes);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            previaB.AbrirAsync(new Midia(Guid.NewGuid(), "Vídeo", "teste.mp4", TipoMidia.Video, TempoMidia.Segundos(5))));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            exportadorB.ExportarAsync(Projeto.Criar("Teste"), new OpcoesExportacao("saida.mp4", Orientacao.Horizontal, ModoEnquadramento.Letterbox), new Progress<ProgressoOperacao>()));
    }

    [Fact]
    public Task JanelaPrincipalResolvidaPeloConteinerAssociaSuperficieDaPrevia() =>
        TestesIntegracaoEditor.ComJanela((janela, caminho) =>
        {
            Assert.NotNull(janela.PreviaAtual);
            Assert.Equal(EstadoPrevia.Vazia, janela.PreviaAtual.Atual.Estado);
            janela.ExibirProjeto(Projeto.Criar("DI Desktop"), salvo: true, caminho: caminho);
            Assert.NotNull(janela.SessaoAtual);
            Assert.Equal("DI Desktop", janela.SessaoAtual.Atual.Nome);
            return Task.CompletedTask;
        });

    [Fact]
    public async Task EncerrarPreviaDeixaDescarteDosServicosParaEscopo()
    {
        var fabrica = new FabricaRastreada();
        var preparador = new PreparadorRastreado();
        await TestesIntegracaoEditor.ComJanela(async (janela, _) =>
        {
            await janela.EncerrarPreviaAsync();
            await janela.EncerrarPreviaAsync();
            Assert.Equal(0, fabrica.Descartes);
            Assert.Equal(0, preparador.Descartes);
        }, fabrica, preparador: preparador);
        Assert.Equal(1, fabrica.Descartes);
        Assert.Equal(1, preparador.Descartes);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public async Task EncerramentoTentaTodasAsEtapasEPreservaFalhas(bool falhaEscopo, bool falhaParada, bool falhaDescarte)
    {
        List<string> etapas = [];
        var escopo = new EscopoRastreado(etapas, falhaEscopo);
        var host = new HostRastreado(etapas, falhaParada, falhaDescarte);

        var erro = await Record.ExceptionAsync(() => App.EncerrarServicosAsync(host, escopo));

        Assert.Equal(["escopo", "parada", "host"], etapas);
        var esperados = new[] { falhaEscopo, falhaParada, falhaDescarte }.Count(falha => falha);
        if (esperados == 0) Assert.Null(erro);
        else
        {
            var agregado = Assert.IsType<AggregateException>(erro);
            Assert.Equal(esperados, agregado.InnerExceptions.Count);
            Assert.Equal(etapas.Where((_, indice) => new[] { falhaEscopo, falhaParada, falhaDescarte }[indice]),
                agregado.InnerExceptions.Select(falha => falha.Message));
        }
    }

    [Fact]
    public Task FechamentoAguardaServicosComDispatcherDisponivel() =>
        TestesIntegracaoEditor.ComJanela(async (janela, _) =>
        {
            var encerrada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var iniciada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var servicosEncerrados = false;
            janela.EncerrarServicosAsync = async () =>
            {
                iniciada.SetResult();
                await liberar.Task;
                await janela.Dispatcher.InvokeAsync(() => servicosEncerrados = true);
            };
            janela.Closed += (_, _) => encerrada.SetResult();
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000;
            janela.Show();
            janela.Close();
            await iniciada.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(encerrada.Task.IsCompleted);
            liberar.SetResult();
            await encerrada.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(servicosEncerrados);
        });

    private sealed class EscopoRastreado(List<string> etapas, bool falhar) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            etapas.Add("escopo");
            return falhar ? ValueTask.FromException(new InvalidOperationException("escopo")) : ValueTask.CompletedTask;
        }
    }

    private sealed class HostRastreado(List<string> etapas, bool falhaParada, bool falhaDescarte) : IHost, IAsyncDisposable
    {
        public IServiceProvider Services => throw new NotSupportedException();
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            etapas.Add("parada");
            return falhaParada ? Task.FromException(new InvalidOperationException("parada")) : Task.CompletedTask;
        }
        public void Dispose() => throw new InvalidOperationException("Descarte síncrono inesperado.");
        public ValueTask DisposeAsync()
        {
            etapas.Add("host");
            return falhaDescarte ? ValueTask.FromException(new InvalidOperationException("host")) : ValueTask.CompletedTask;
        }
    }

    private sealed class FabricaRastreada : IFabricaReprodutorMidia
    {
        public int Descartes { get; private set; }
        public Task<IReprodutorMidia> CriarAsync(Midia midia, CancellationToken token) =>
            throw new NotSupportedException();
        public ValueTask DisposeAsync()
        {
            Descartes++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PreparadorRastreado : IPreparadorComposicao
    {
        public int Descartes { get; private set; }
        public Task<Midia> PrepararAsync(Projeto projeto, IProgress<string>? progresso, CancellationToken token) =>
            throw new NotSupportedException();
        public ValueTask DisposeAsync()
        {
            Descartes++;
            return ValueTask.CompletedTask;
        }
    }
}
