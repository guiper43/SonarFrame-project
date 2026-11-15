using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SonarFrame.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? host;
    private AsyncServiceScope? escopoJanela;
    private Mutex? mutexInstalacao;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            mutexInstalacao = new Mutex(false, "sonarFrame.Desktop.Active");
            host = RegistroDependencias.CriarHostSonarFrame(e.Args);
            await host.StartAsync();
            escopoJanela = host.Services.CreateAsyncScope();
            var janela = escopoJanela.Value.ServiceProvider.GetRequiredService<JanelaPrincipal>();
            janela.EncerrarServicosAsync = EncerrarHostAsync;
            MainWindow = janela;
            EventHandler? abrirProjetoInicial = null;
            abrirProjetoInicial = async (_, _) =>
            {
                janela.ContentRendered -= abrirProjetoInicial;
                await janela.AbrirArgumentosAsync(e.Args);
            };
            janela.ContentRendered += abrirProjetoInicial;
            janela.Show();
        }
        catch (Exception erro)
        {
            try { await EncerrarHostAsync(); }
            catch (Exception erroEncerramento) { throw new AggregateException(erro, erroEncerramento); }
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Task.Run(EncerrarHostAsync).GetAwaiter().GetResult();
        }
        finally
        {
            mutexInstalacao?.Dispose();
            base.OnExit(e);
        }
    }

    internal Task EncerrarHostAsync()
    {
        var atual = host;
        var escopo = escopoJanela;
        host = null;
        escopoJanela = null;
        return EncerrarServicosAsync(atual, escopo);
    }

    internal static async Task EncerrarServicosAsync(IHost? atual, IAsyncDisposable? escopo)
    {
        List<Exception> erros = [];
        if (escopo is not null)
        {
            try { await escopo.DisposeAsync(); }
            catch (Exception erro) { erros.Add(erro); }
        }
        if (atual is not null)
        {
            try { await atual.StopAsync(); }
            catch (Exception erro) { erros.Add(erro); }
            try
            {
                if (atual is IAsyncDisposable assincrono) await assincrono.DisposeAsync();
                else atual.Dispose();
            }
            catch (Exception erro) { erros.Add(erro); }
        }
        if (erros.Count > 0) throw new AggregateException("Falha ao encerrar os serviços do aplicativo.", erros);
    }
}
