using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using System.Windows.Threading;
using SonarFrame.Application;
using SonarFrame.Core;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class TestesDialogoImportacao
{
    [Fact]
    public Task AberturaModalEntregaFocoRealAGuiaESetasMantemFocoAcessivel() => TestesIntegracaoEditor.ComJanela((janela, _) =>
    {
        janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        var dialogo = new DialogoImportacao(janela, new SessaoEdicao(Projeto.Criar("Foco")), new Leitor()) { ShowInTaskbar = false };
        Exception? falha = null;
        dialogo.ContentRendered += (_, _) => dialogo.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                var guias = ((StackPanel)dialogo.Content).Children.OfType<TabControl>().Single();
                Assert.Same(guias.Items[0], Keyboard.FocusedElement);
                foreach (var tecla in new[] { Key.Down, Key.Right, Key.Up, Key.Left })
                {
                    Pressionar((TabItem)Keyboard.FocusedElement, tecla);
                    var guia = (TabItem)guias.SelectedItem;
                    Assert.Same(guia, Keyboard.FocusedElement);
                    var peer = UIElementAutomationPeer.CreatePeerForElement(guia)!;
                    Assert.True(peer.HasKeyboardFocus());
                    Assert.Contains(guia.Header.ToString()!, peer.GetName());
                }
                Pressionar((TabItem)Keyboard.FocusedElement, Key.Tab);
                Assert.IsType<Button>(Keyboard.FocusedElement);
                dialogo.Activate();
                Assert.IsType<Button>(Keyboard.FocusedElement);
            }
            catch (Exception ex) { falha = ex; }
            finally { dialogo.Close(); }
        }));
        dialogo.ShowDialog();
        Assert.Null(falha);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SetasSelecionamTipoTabAcessaArquivosEConclusaoFocaFechar() => TestesIntegracaoEditor.ComJanela(async (janela, _) =>
    {
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        var sessao = new SessaoEdicao(Projeto.Criar("Importação"));
        var leitor = new Leitor();
        var dialogo = new DialogoImportacao(janela, sessao, leitor) { ShowActivated = false, ShowInTaskbar = false };
        try
        {
            dialogo.Show();
            var painel = (StackPanel)dialogo.Content;
            var guias = painel.Children.OfType<TabControl>().Single();
            Assert.Same(guias.Items[0], FocusManager.GetFocusedElement(dialogo));
            foreach (var (tecla, esperado) in new[] { (Key.Down, 1), (Key.Up, 0), (Key.Right, 1), (Key.Left, 0) })
            {
                Pressionar((TabItem)guias.SelectedItem, tecla);
                Assert.Equal(esperado, guias.SelectedIndex);
                Assert.Same(guias.SelectedItem, FocusManager.GetFocusedElement(dialogo));
            }
            Pressionar((TabItem)guias.SelectedItem, Key.Tab);
            Assert.Equal("Selecionar _arquivos…", Assert.IsType<Button>(FocusManager.GetFocusedElement(dialogo)).Content);
            await dialogo.ImportarArquivosAsync(sessao, leitor, ["teste.mp4"]);
            Assert.True(dialogo.ImportouAlgumaMidia);
            Assert.Single(sessao.Atual.Biblioteca);
            Assert.Equal("Fechar", Assert.IsType<Button>(FocusManager.GetFocusedElement(dialogo)).Content);
        }
        finally { dialogo.Close(); }
    });

    private static void Pressionar(UIElement origem, Key tecla)
    {
        var evento = new KeyEventArgs(new TecladoTeste(), PresentationSource.FromVisual(origem)!, Environment.TickCount, tecla)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        origem.RaiseEvent(evento);
        Assert.True(evento.Handled);
    }

    private sealed class Leitor : ILeitorMetadadosMidia
    {
        public Task<Midia> LerAsync(string caminho, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Midia(Guid.NewGuid(), "Vídeo", caminho, TipoMidia.Video, TempoMidia.Segundos(10)));
    }
}
