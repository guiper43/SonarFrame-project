using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Desktop;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class TestesIntegracaoEditor
{
    [Fact] public Task BotoesDeEdicaoUsamSessaoEAtualizamDisponibilidade() => ComJanela(async (janela, caminho) =>
    {
        janela.ExibirProjeto(Amostra(), caminho: caminho);
        var sessao = janela.SessaoAtual!;
        Assert.False(janela.MenuExcluirSelecao.IsEnabled);
        sessao.Posicionar(TempoMidia.Segundos(4)); janela.Atualizar();
        janela.MenuDividir.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        Assert.Equal(2, sessao.Atual.LinhaDoTempo.Length); Assert.Equal(2, janela.LinhaDoTempo.Items.Count);
        sessao.Posicionar(TempoMidia.Segundos(2)); sessao.Posicionar(TempoMidia.Segundos(6), true); janela.Atualizar();
        Assert.True(janela.MenuExcluirSelecao.IsEnabled);
        janela.MenuExcluirSelecao.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        Assert.Equal(TempoMidia.Segundos(6), sessao.Atual.Duracao); Assert.False(janela.MenuExcluirSelecao.IsEnabled);
        janela.MenuDesfazer.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        Assert.Equal(TempoMidia.Segundos(10), sessao.Atual.Duracao);
        Assert.True(janela.MenuRefazer.IsEnabled);
        janela.MenuRefazer.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        Assert.Equal(TempoMidia.Segundos(6), sessao.Atual.Duracao);
        Assert.True(await janela.SalvarAtualAsync()); Assert.False(sessao.TemAlteracoes);
        var salvo = await new RepositorioProjetosSqlite().CarregarAsync(caminho);
        Assert.Equal(sessao.Atual.Duracao, salvo.Duracao);
        Assert.True(File.Exists(caminho + ".recovery"));
    });

    [Fact] public Task CamadasEPropriedadesPermanecemVisiveisAposDesfazerESalvar() => ComJanela(async (janela, caminho) =>
    {
        var projeto = Amostra(); var sessao = new SessaoEdicao(projeto);
        sessao.Executar(new AdicionarCamada(projeto.Biblioteca[0].Id, TempoMidia.Segundos(1), TempoMidia.Zero, TempoMidia.Segundos(5)));
        var id = sessao.Atual.Camadas[0].Clipe.Id;
        sessao.Executar(new DefinirVolume(id, 40));
        janela.ExibirProjeto(sessao.Atual, caminho: caminho);
        Assert.Equal(2, janela.LinhaDoTempo.Items.Count); Assert.True(janela.BotaoEditarCamada.IsEnabled);
        Assert.True(janela.BotaoVolume.IsEnabled); Assert.Contains("40%", janela.Propriedades.Text);
        janela.BotaoRemoverCamada.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Single(janela.SessaoAtual!.Atual.LinhaDoTempo); Assert.Empty(janela.SessaoAtual.Atual.Camadas);
        Assert.False(janela.BotaoEditarCamada.IsEnabled);
        janela.MenuDesfazer.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        Assert.Single(janela.SessaoAtual.Atual.Camadas); Assert.True(janela.BotaoEditarCamada.IsEnabled);
        Assert.True(await janela.SalvarAtualAsync());
        var salvo = await new RepositorioProjetosSqlite().CarregarAsync(caminho);
        Assert.Equal(40, salvo.Camadas[0].Clipe.Volume);
    });

    private static Projeto Amostra()
    {
        var sessao = new SessaoEdicao(Projeto.Criar("Integração desktop"));
        var midia = new Midia(Guid.NewGuid(), "Vídeo", "original.mp4", TipoMidia.Video, TempoMidia.Segundos(10), new(30, 1));
        sessao.Executar(new RegistrarMidia(midia)); sessao.Executar(new InserirClipe(midia.Id, 0));
        return sessao.Atual;
    }
    [Fact] public Task SelecaoNaBibliotecaNaoAlteraCursorNemHabilitaVolumeDeOutroItem() => ComJanela((janela, caminho) =>
    {
        janela.ExibirProjeto(Amostra(), caminho: caminho);
        Assert.True(janela.BotaoVolume.IsEnabled);
        var cursor = janela.SessaoAtual!.Atual.Cursor;
        janela.Biblioteca.SelectedIndex = 0;
        Assert.False(janela.BotaoVolume.IsEnabled);
        janela.Atualizar();
        Assert.False(janela.BotaoVolume.IsEnabled); Assert.Equal(cursor, janela.SessaoAtual.Atual.Cursor);
        Assert.Contains("original.mp4", janela.Propriedades.Text);
        return Task.CompletedTask;
    });
    internal static Task ComJanela(Func<JanelaPrincipal, string, Task> corpo, IFabricaReprodutorMidia? fabrica = null, int prazoSegundos = 30, IPreparadorComposicao? preparador = null)
    {
        var conclusao = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            _ = dispatcher.InvokeAsync(async () =>
            {
                JanelaPrincipal? janela = null;
                Exception? falha = null;
                var pasta = Path.Combine(AppContext.BaseDirectory, "desktop-tests", Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(pasta);
                    janela = new JanelaPrincipal(fabrica, preparador);
                    await corpo(janela, Path.Combine(pasta, "test.sonarframe"));
                }
                catch (Exception e) { falha = e; }
                finally
                {
                    try
                    {
                        janela?.PararTarefasEmSegundoPlano();
                        if (janela is not null) await janela.EncerrarPreviaAsync();
                        if (janela?.IsLoaded == true) janela.Hide();
                        if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
                    }
                    catch (Exception e) { falha ??= e; }
                    if (falha is null) conclusao.TrySetResult(); else conclusao.TrySetException(falha);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        return conclusao.Task.WaitAsync(TimeSpan.FromSeconds(prazoSegundos));
    }
}
