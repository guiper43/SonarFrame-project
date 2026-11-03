using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Threading;
using SonarFrame.Core;
using SonarFrame.Application;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class TestesMenu
{
    [Fact]
    public void AnunciosDeTempoUsamMinutosESegundosEPreservamPrecisaoNaSelecao()
    {
        Assert.Equal("Avançou 1 segundo. Atualmente em 0 minutos e 22 segundos.",
            JanelaPrincipal.AnuncioDeslocamento(TempoMidia.Segundos(21), TempoMidia.Segundos(22), false));
        Assert.Equal("Retrocedeu 10 segundos. Atualmente em 1 minuto e 1 segundo.",
            JanelaPrincipal.AnuncioDeslocamento(TempoMidia.Segundos(71), TempoMidia.Segundos(61), false));
        Assert.Equal("Avançou menos de 1 segundo. Atualmente em 0 minutos e 0 segundos.",
            JanelaPrincipal.AnuncioDeslocamento(TempoMidia.Zero, new(1, 30), false));
        Assert.Equal("Atualmente em 0 minutos e 0 segundos.",
            JanelaPrincipal.AnuncioDeslocamento(TempoMidia.Zero, TempoMidia.Zero, false));
        Assert.Equal("Seleção até 00:00:00.033.",
            JanelaPrincipal.AnuncioDeslocamento(TempoMidia.Zero, new(1, 30), true));
    }

    [Fact]
    public Task ProjetoAnunciaNomeAntesDoCaminho() => TestesIntegracaoEditor.ComJanela((janela, caminho) =>
    {
        var rotulo = $"Projeto: Meu vídeo. Caminho: {caminho}";
        var lista = new ListBox { DisplayMemberPath = "Rotulo", ItemsSource = new[] { new JanelaPrincipal.LinhaProjeto(caminho, rotulo) } };
        janela.Content = lista;
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        lista.UpdateLayout();
        Assert.Equal(rotulo, new ListBoxAutomationPeer(lista).GetChildren()[0].GetName());
        return Task.CompletedTask;
    });

    [Fact]
    public Task ListaDaTimelineNavegaItensVerticalmenteETempoHorizontalmente() => TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
    {
        var sessao = new SessaoEdicao(Projeto.Criar("Tempo"));
        var midia = new Midia(Guid.NewGuid(), "Vídeo", "original.mp4", TipoMidia.Video, TempoMidia.Segundos(10), new(30, 1));
        sessao.Executar(new RegistrarMidia(midia));
        sessao.Executar(new InserirClipe(midia.Id, 0));
        sessao.Executar(new InserirClipe(midia.Id, 1));
        janela.ExibirProjeto(sessao.Atual, salvo: true, caminho: caminho);
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        janela.LinhaDoTempo.SelectedIndex = 0;
        foreach (var lista in new[] { janela.Biblioteca, janela.LinhaDoTempo })
        {
            var peer = new ListBoxAutomationPeer(lista);
            var nome = peer.GetChildren()[0].GetName();
            Assert.StartsWith("Vídeo", nome);
            Assert.DoesNotContain(midia.Id.ToString(), nome);
            Assert.DoesNotContain("Id =", nome);
        }
        janela.LinhaDoTempo.Focus();
        var cursor = janela.SessaoAtual!.Atual.Cursor;
        Pressionar(janela.LinhaDoTempo, Key.Down);
        Assert.Equal(1, janela.LinhaDoTempo.SelectedIndex);
        Assert.Equal(cursor, janela.SessaoAtual.Atual.Cursor);
        janela.SessaoAtual.Posicionar(TempoMidia.Zero);
        Pressionar(janela.LinhaDoTempo, Key.Right);
        await janela.UltimaNavegacaoTemporal;
        Assert.NotEqual(TempoMidia.Zero, janela.SessaoAtual.Atual.Cursor);
        Assert.Equal(1, janela.LinhaDoTempo.SelectedIndex);
        Pressionar(janela.LinhaDoTempo, Key.Left);
        await janela.UltimaNavegacaoTemporal;
        Assert.Equal(TempoMidia.Zero, janela.SessaoAtual.Atual.Cursor);
        var foco = FocusManager.GetFocusedElement(janela);
        Pressionar(janela.LinhaDoTempo, Key.Enter);
        await janela.UltimaNavegacaoTemporal;
        Assert.Equal(TempoMidia.Segundos(10), janela.SessaoAtual.Atual.Cursor);
        Assert.Equal(1, janela.LinhaDoTempo.SelectedIndex);
        Assert.Same(foco, FocusManager.GetFocusedElement(janela));
        Pressionar(janela.LinhaDoTempo, Key.End);
        await janela.UltimaNavegacaoTemporal;
        Assert.Equal(TempoMidia.Segundos(20), janela.SessaoAtual.Atual.Cursor);
        Assert.Equal(1, janela.LinhaDoTempo.SelectedIndex);
        Assert.Same(foco, FocusManager.GetFocusedElement(janela));
        Pressionar(janela.LinhaDoTempo, Key.Home);
        await janela.UltimaNavegacaoTemporal;
        Assert.Equal(TempoMidia.Zero, janela.SessaoAtual.Atual.Cursor);
        Assert.Equal(1, janela.LinhaDoTempo.SelectedIndex);
        Assert.Same(foco, FocusManager.GetFocusedElement(janela));
    }, preparador: new PreparadorNavegacao());

    private sealed class PreparadorNavegacao : IPreparadorComposicao
    {
        public Task<Midia> PrepararAsync(Projeto projeto, IProgress<string>? progresso, CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public Task SetasFicamNaAreaETabAvancaParaProximaArea() => TestesIntegracaoEditor.ComJanela((janela, caminho) =>
    {
        janela.ExibirProjeto(Projeto.Criar("Navegação"), salvo: true, caminho: caminho);
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        janela.BotaoPrevia.Focus();
        foreach (var tecla in new[] { Key.Up, Key.Down, Key.Left, Key.Right })
        {
            Pressionar(janela.BotaoPrevia, tecla);
            Assert.Same(janela.BotaoPrevia, FocusManager.GetFocusedElement(janela));
        }
        janela.BotaoPrevia.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Assert.Same(janela.Propriedades, FocusManager.GetFocusedElement(janela));
        janela.Propriedades.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
        Assert.Same(janela.BotaoPrevia, FocusManager.GetFocusedElement(janela));
        janela.BotaoAdicionar.Focus();
        Pressionar(janela.BotaoAdicionar, Key.Down);
        Assert.Same(janela.Biblioteca, FocusManager.GetFocusedElement(janela));
        Pressionar(janela.Biblioteca, Key.Up);
        Assert.Same(janela.Biblioteca, FocusManager.GetFocusedElement(janela));
        return Task.CompletedTask;
    });

    [Fact]
    public Task SetasNaoAbremMenusEEnterEspacoAlternamAbertura() => TestesIntegracaoEditor.ComJanela((janela, caminho) =>
    {
        janela.ExibirProjeto(Projeto.Criar("Menus"), salvo: true, caminho: caminho);
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        var projeto = (MenuItem)janela.MenuPrincipal.Items[0];
        var ajuda = (MenuItem)janela.MenuPrincipal.Items[1];
        foreach (var tecla in new[] { Key.Up, Key.Down, Key.Left, Key.Right })
        {
            Pressionar(projeto, tecla);
            Assert.False(projeto.IsSubmenuOpen); Assert.False(ajuda.IsSubmenuOpen);
        }
        Pressionar(projeto, Key.Enter); Assert.True(projeto.IsSubmenuOpen);
        Pressionar(projeto, Key.Space); Assert.False(projeto.IsSubmenuOpen);
        Pressionar(ajuda, Key.Space); Assert.True(ajuda.IsSubmenuOpen);
        Pressionar(ajuda, Key.Right); Assert.False(ajuda.IsSubmenuOpen); Assert.False(projeto.IsSubmenuOpen);
        Pressionar(projeto, Key.Enter);
        Pressionar((MenuItem)projeto.Items[0], Key.Left);
        Assert.False(projeto.IsSubmenuOpen); Assert.False(ajuda.IsSubmenuOpen);
        return Task.CompletedTask;
    });

    [Fact]
    public Task AjudaIntegraListaInicialESetaParaCimaRetornaADemonstracao() => TestesIntegracaoEditor.ComJanela((janela, caminho) =>
    {
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        Assert.Equal(Visibility.Collapsed, janela.MenuPrincipal.Visibility);
        Assert.Same(janela.BotaoAjudaInicial, janela.TelaInicial.Children.OfType<Button>().Last());
        janela.BotaoDemonstracao.Focus();
        Pressionar(janela.BotaoDemonstracao, Key.Down);
        Assert.Same(janela.BotaoAjudaInicial, FocusManager.GetFocusedElement(janela));
        Pressionar(janela.BotaoAjudaInicial, Key.Up);
        Assert.Same(janela.BotaoDemonstracao, FocusManager.GetFocusedElement(janela));
        Assert.All(janela.MenuPrincipal.Items.OfType<MenuItem>(), item => Assert.False(item.IsSubmenuOpen));
        janela.ExibirProjeto(Projeto.Criar("Editor"), salvo: true, caminho: caminho);
        Assert.Equal(Visibility.Visible, janela.MenuPrincipal.Visibility);
        return Task.CompletedTask;
    });

    private static void Pressionar(UIElement item, Key tecla)
    {
        var origem = PresentationSource.FromVisual(item) ?? PresentationSource.FromVisual(Window.GetWindow(item));
        var evento = new KeyEventArgs(new TecladoTeste(), origem!, Environment.TickCount, tecla)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        item.RaiseEvent(evento);
        Assert.True(evento.Handled, $"Tecla {tecla}; modificadores {Keyboard.Modifiers}; origem {item}; visível {item.IsVisible}.");
    }

    [Fact]
    public Task AjudaExpandeAsDuasOpcoesSemAbrirDialogoENavegaDeVolta() => TestesIntegracaoEditor.ComJanela((janela, _) =>
    {
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        janela.BotaoAjudaInicial.Focus();
        Pressionar(janela.BotaoAjudaInicial, Key.Down);
        Assert.Equal(Visibility.Collapsed, janela.OpcoesAjudaInicial.Visibility);
        janela.BotaoAjudaInicial.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Visibility.Visible, janela.OpcoesAjudaInicial.Visibility);
        Assert.Empty(janela.OwnedWindows.Cast<Window>());
        Assert.Contains("expandidas", AutomationProperties.GetName(janela.BotaoAjudaInicial));
        Pressionar(janela.BotaoAjudaInicial, Key.Down);
        Assert.Same(janela.BotaoAtalhosInicial, FocusManager.GetFocusedElement(janela));
        Pressionar(janela.BotaoAtalhosInicial, Key.Down);
        Assert.Same(janela.BotaoSobreInicial, FocusManager.GetFocusedElement(janela));
        Pressionar(janela.BotaoSobreInicial, Key.Up);
        Pressionar(janela.BotaoAtalhosInicial, Key.Up);
        Assert.Same(janela.BotaoAjudaInicial, FocusManager.GetFocusedElement(janela));
        Pressionar(janela.BotaoAjudaInicial, Key.Up);
        Assert.Same(janela.BotaoDemonstracao, FocusManager.GetFocusedElement(janela));
        janela.BotaoAjudaInicial.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Visibility.Collapsed, janela.OpcoesAjudaInicial.Visibility);
        Assert.Contains("recolhidas", AutomationProperties.GetName(janela.BotaoAjudaInicial));
        return Task.CompletedTask;
    });

    [Fact]
    public Task OpcoesDeAjudaAbremConteudosCorrespondentes() => TestesIntegracaoEditor.ComJanela((janela, _) =>
    {
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        janela.BotaoAjudaInicial.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        foreach (var (botao, titulo) in new[] { (janela.BotaoAtalhosInicial, "Atalhos de teclado"), (janela.BotaoSobreInicial, "Sobre o sonarFrame") })
        {
            string? observado = null;
            janela.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var dialogo = janela.OwnedWindows.Cast<Window>().Single(); observado = dialogo.Title; dialogo.Close();
            }));
            botao.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(titulo, observado);
        }
        return Task.CompletedTask;
    });
}
