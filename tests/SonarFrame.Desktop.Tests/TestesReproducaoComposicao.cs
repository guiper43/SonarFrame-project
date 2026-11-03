using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SonarFrame.Application;
using SonarFrame.Core;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class TestesReproducaoComposicao
{
    [Fact]
    public Task TimelineAlternaEntreIntervaloEItemSemRemoverAoAbrirMenu()
    {
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            janela.ExibirProjeto(ProjetoTeste(), salvo: true, caminho: caminho);
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            janela.LinhaDoTempo.SelectedIndex = 0;
            janela.LinhaDoTempo.Focus();
            var sessao = janela.SessaoAtual!;
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.Right, ModifierKeys.Shift | ModifierKeys.Control));
            await janela.UltimaNavegacaoTemporal;
            Assert.NotNull(sessao.Atual.Selecao);
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.Down));
            var antes = sessao.Atual;
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.Delete));
            Assert.True(janela.MenuTimeline.IsOpen);
            Assert.Equal("Remover vídeo da composição", janela.MenuRemoverComposicao.Header);
            Assert.True(janela.MenuRemoverComposicao.IsFocused);
            Assert.Equal(antes, sessao.Atual);
            Assert.False(sessao.PodeDesfazer);
            janela.MenuTimeline.RaiseEvent(new KeyEventArgs(new TecladoTeste(), PresentationSource.FromVisual(janela.MenuTimeline)!, Environment.TickCount, Key.Escape)
            { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.False(janela.MenuTimeline.IsOpen);
            Assert.True(FocusManager.GetFocusedElement(janela) is ListBoxItem or ListBox);
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.Apps));
            Assert.True(janela.MenuTimeline.IsOpen);
            janela.MenuTimeline.IsOpen = false;
            janela.LinhaDoTempo.UpdateLayout();
            var alvo = (ListBoxItem)janela.LinhaDoTempo.ItemContainerGenerator.ContainerFromIndex(0);
            alvo.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
            { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Assert.Equal(0, janela.LinhaDoTempo.SelectedIndex);
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.F10, ModifierKeys.Shift));
            Assert.True(janela.MenuTimeline.IsOpen);
            janela.MenuTimeline.IsOpen = false;
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.Left, ModifierKeys.Shift | ModifierKeys.Control));
            await janela.UltimaNavegacaoTemporal;
            Assert.Null(sessao.Atual.Selecao);
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.Right, ModifierKeys.Shift | ModifierKeys.Control));
            await janela.UltimaNavegacaoTemporal;
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.Delete));
            await janela.UltimaEdicao;
            Assert.False(janela.MenuTimeline.IsOpen);
            Assert.Equal(TempoMidia.Segundos(3), sessao.Atual.Duracao);
            Assert.Null(sessao.Atual.Selecao);
            Assert.True(FocusManager.GetFocusedElement(janela) is ListBoxItem or ListBox);
        }, new Fabrica(), preparador: new Preparador());
    }

    [Fact]
    public Task EditarPorAtalhoEBotaoPreservaFocoIntervaloEReproduzEstadoRestaurado()
    {
        var preparador = new Preparador();
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            janela.ExibirProjeto(ProjetoTeste(), salvo: true, caminho: caminho);
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            var sessao = janela.SessaoAtual!;
            janela.LinhaDoTempo.SelectedIndex = 1;
            janela.LinhaDoTempo.UpdateLayout();
            var item = (ListBoxItem)janela.LinhaDoTempo.ItemContainerGenerator.ContainerFromIndex(1);
            item.Focus();
            var selecionado = sessao.Atual.ItemSelecionadoId;
            sessao.Posicionar(TempoMidia.Segundos(1));
            Assert.False(TecladoTeste.Pressionar(janela.Propriedades, Key.B, ModifierKeys.Control));
            Assert.Equal(2, sessao.Atual.LinhaDoTempo.Length);
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.B, ModifierKeys.Control));
            await janela.UltimaEdicao;
            Assert.Equal(3, sessao.Atual.LinhaDoTempo.Length);
            Assert.Equal(selecionado, sessao.Atual.ItemSelecionadoId);
            Assert.Equal(TempoMidia.Segundos(1), sessao.Atual.Cursor);
            Assert.Contains("Parte 1 e Parte 2", janela.Situacao.Text);
            var foco = Assert.IsType<ListBoxItem>(FocusManager.GetFocusedElement(janela));
            Assert.Same(janela.LinhaDoTempo.SelectedItem, foco.Content);
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(foco)!;
            Assert.Contains("Vídeo", peer.GetName());
            Assert.Contains("item 3", peer.GetName());
            Assert.Contains("duração", peer.GetName());
            var dividido = sessao.Atual;
            janela.MenuDesfazer.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); await janela.UltimaEdicao;
            Assert.Equal(2, sessao.Atual.LinhaDoTempo.Length);
            janela.MenuDividir.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); await janela.UltimaEdicao;
            Assert.Equal(dividido.LinhaDoTempo.Select(c => (c.Nome, c.InicioNaOrigem, c.Duracao)), sessao.Atual.LinhaDoTempo.Select(c => (c.Nome, c.InicioNaOrigem, c.Duracao)));
            await janela.ReproduzirComposicaoAsync(false);
            Assert.Equal(sessao.Atual.LinhaDoTempo, preparador.Ultimo!.LinhaDoTempo);
            await janela.NavegarTemporalAsync(Key.None, ModifierKeys.None, new TempoMidia(1, 2));
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.Right, ModifierKeys.Control | ModifierKeys.Shift));
            await janela.UltimaNavegacaoTemporal;
            var antes = sessao.Atual;
            janela.MenuConsultarTempo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Contains("Intervalo selecionado de 00:00:00.500 até 00:00:01.500", janela.Situacao.Text);
            Assert.Contains("00:00:01.500", System.Windows.Automation.AutomationProperties.GetName(janela.NavegadorTemporal));
            var excluir = new KeyEventArgs(new TecladoTeste(), PresentationSource.FromVisual(janela.LinhaDoTempo)!, Environment.TickCount, Key.Delete)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            janela.LinhaDoTempo.RaiseEvent(excluir); await janela.UltimaEdicao;
            Assert.True(excluir.Handled);
            var depois = sessao.Atual;
            Assert.Equal(TempoMidia.Segundos(3), depois.Duracao);
            Assert.Equal(new TempoMidia(1, 2), depois.Cursor);
            Assert.Null(depois.Selecao);
            Assert.Equal(new[] { new TempoMidia(1, 2), new TempoMidia(1, 2), TempoMidia.Segundos(2) }, depois.LinhaDoTempo.Select(c => c.Duracao));
            Assert.Equal(new TempoMidia(3, 2), depois.LinhaDoTempo[1].InicioNaOrigem);
            Assert.Equal(antes.Biblioteca, depois.Biblioteca);
            Assert.Contains("lacuna fechada", janela.Situacao.Text);
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.Z, ModifierKeys.Control)); await janela.UltimaEdicao;
            Assert.Equal(antes, sessao.Atual);
            janela.MenuExcluirSelecao.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); await janela.UltimaEdicao;
            Assert.Equal(depois.LinhaDoTempo.ToArray(), sessao.Atual.LinhaDoTempo.ToArray());
            Assert.Equal(depois.Cursor, sessao.Atual.Cursor);
            depois = sessao.Atual;
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.Z, ModifierKeys.Control)); await janela.UltimaEdicao;
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.Y, ModifierKeys.Control)); await janela.UltimaEdicao;
            Assert.Equal(depois, sessao.Atual);
            await janela.ReproduzirComposicaoAsync(false);
            Assert.Equal(depois.LinhaDoTempo, preparador.Ultimo!.LinhaDoTempo);
            Assert.Equal(depois.Duracao, preparador.Ultimo.Duracao);
        }, new Fabrica(), preparador: preparador);
    }

    [Fact]
    public Task InsercaoIntermediariaEReordenacaoAtualizamListaHistoricoEPrevia()
    {
        var preparador = new Preparador();
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            janela.ExibirProjeto(ProjetoTeste(), salvo: true, caminho: caminho);
            janela.LinhaDoTempo.SelectedIndex = 1;
            janela.Biblioteca.SelectedIndex = 0;
            var sessao = janela.SessaoAtual!;
            var antes = sessao.Atual;
            janela.BotaoInserirAntes.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await janela.UltimaEdicao;
            Assert.Equal(3, sessao.Atual.LinhaDoTempo.Length);
            Assert.Equal(antes.LinhaDoTempo[0], sessao.Atual.LinhaDoTempo[0]);
            Assert.Equal(antes.LinhaDoTempo[1], sessao.Atual.LinhaDoTempo[2]);
            var inserido = sessao.Atual.LinhaDoTempo[1].Id;
            Assert.Equal(inserido, sessao.Atual.ItemSelecionadoId);
            janela.MenuMoverAntes.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); await janela.UltimaEdicao;
            var movido = sessao.Atual;
            Assert.Equal(inserido, movido.LinhaDoTempo[0].Id);
            Assert.Equal(3, janela.LinhaDoTempo.Items.Count);
            Assert.Contains("posição 1 de 3", janela.Situacao.Text);
            janela.MenuMoverAntes.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); await janela.UltimaEdicao;
            Assert.Same(movido, sessao.Atual);
            Assert.Contains("já está", janela.Situacao.Text);
            janela.MenuDesfazer.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); await janela.UltimaEdicao;
            Assert.Equal(inserido, sessao.Atual.LinhaDoTempo[1].Id);
            janela.MenuRefazer.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); await janela.UltimaEdicao;
            Assert.Equal(movido, sessao.Atual);
            await janela.ReproduzirComposicaoAsync(false);
            Assert.Equal(movido.LinhaDoTempo, preparador.Ultimo!.LinhaDoTempo);
            Assert.Equal(TempoMidia.Segundos(6), preparador.Ultimo.Duracao);
        }, new Fabrica(), preparador: preparador);
    }

    private static Projeto ProjetoTeste()
    {
        var m = new Midia(Guid.NewGuid(), "Vídeo", "teste.mp4", TipoMidia.Video, TempoMidia.Segundos(2));
        return Projeto.Criar("Composição") with { Biblioteca = [m], LinhaDoTempo = [
            new(Guid.NewGuid(), m.Id, m.Nome, TempoMidia.Zero, m.Duracao!.Value),
            new(Guid.NewGuid(), m.Id, m.Nome, TempoMidia.Zero, m.Duracao.Value)] };
    }

    [Fact]
    public Task SelecaoAteExtremosPodeSerConsultadaSemCriarEdicaoOuIntervaloVazio()
    {
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            janela.ExibirProjeto(ProjetoTeste(), salvo: true, caminho: caminho);
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            var sessao = janela.SessaoAtual!;
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.Home, ModifierKeys.Shift)); await janela.UltimaNavegacaoTemporal;
            Assert.Null(sessao.Atual.Selecao);
            Assert.Contains("Nenhum intervalo selecionado", janela.Situacao.Text);
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.End, ModifierKeys.Shift)); await janela.UltimaNavegacaoTemporal;
            Assert.Equal(new SelecaoTemporal(TempoMidia.Zero, TempoMidia.Segundos(4)), sessao.Atual.Selecao);
            Assert.True(janela.MenuExcluirSelecao.IsEnabled);
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.Home)); await janela.UltimaNavegacaoTemporal;
            Assert.Null(sessao.Atual.Selecao);
            Assert.False(janela.MenuExcluirSelecao.IsEnabled);
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.Z, ModifierKeys.Control)); await janela.UltimaEdicao;
            Assert.Equal("Nenhuma edição para desfazer.", janela.Situacao.Text);
            Assert.True(TecladoTeste.Pressionar(janela.LinhaDoTempo, Key.B, ModifierKeys.Control)); await janela.UltimaEdicao;
            Assert.Contains("dentro de um clipe", janela.Situacao.Text);
            Assert.False(sessao.PodeDesfazer);
            Assert.Equal(2, sessao.Atual.LinhaDoTempo.Length);
        }, new Fabrica(), preparador: new Preparador());
    }

    [Fact]
    public Task ReproduzCursorPausaSelecaoERemocaoDesfazSemApagarBiblioteca()
    {
        var fabrica = new Fabrica(); var preparador = new Preparador();
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            janela.ExibirProjeto(ProjetoTeste(), salvo: true, caminho: caminho);
            var sessao = janela.SessaoAtual!;
            janela.LinhaDoTempo.SelectedIndex = 0; janela.LinhaDoTempo.UpdateLayout();
            var item = (ListBoxItem)janela.LinhaDoTempo.ItemContainerGenerator.ContainerFromIndex(0);
            item.Focus();
            sessao.Posicionar(TempoMidia.Segundos(1));
            PressionarEspaco(item); await janela.UltimaOperacaoComposicao;
            Assert.Equal(EstadoPrevia.Reproduzindo, janela.PreviaAtual.Atual.Estado);
            Assert.Equal("Pausar", janela.BotaoReproduzirPrevia.Content);
            Assert.Equal("Pausar", System.Windows.Automation.AutomationProperties.GetName(janela.BotaoReproduzirPrevia));
            Assert.Equal(TempoMidia.Segundos(1), fabrica.Motor!.Posicao);
            Assert.False(sessao.PodeDesfazer);
            fabrica.Motor.Posicao = new(3, 2); janela.SincronizarCursorComposicao();
            Assert.Equal(new TempoMidia(3, 2), sessao.Atual.Cursor);
            PressionarEspaco(item); await janela.UltimaOperacaoComposicao;
            Assert.Equal(EstadoPrevia.Pausada, janela.PreviaAtual.Atual.Estado);
            PressionarEspaco(item); await janela.UltimaOperacaoComposicao;
            Assert.Equal(EstadoPrevia.Reproduzindo, janela.PreviaAtual.Atual.Estado);
            Assert.Equal("Pausar", janela.BotaoReproduzirPrevia.Content);
            Assert.Equal("Pausar", System.Windows.Automation.AutomationProperties.GetName(janela.BotaoReproduzirPrevia));
            PressionarEspaco(janela.LinhaDoTempo); await janela.UltimaOperacaoComposicao;
            Assert.Equal(EstadoPrevia.Pausada, janela.PreviaAtual.Atual.Estado);
            sessao.Posicionar(TempoMidia.Segundos(1)); sessao.Posicionar(TempoMidia.Segundos(2), true);
            var selecao = sessao.Atual.Selecao;
            await janela.ReproduzirComposicaoAsync(true);
            fabrica.Motor!.Posicao = TempoMidia.Segundos(3); janela.SincronizarCursorComposicao();
            await janela.UltimaOperacaoComposicao;
            Assert.Equal(EstadoPrevia.Pausada, janela.PreviaAtual.Atual.Estado);
            Assert.Equal(TempoMidia.Segundos(2), sessao.Atual.Cursor);
            Assert.Equal(selecao, sessao.Atual.Selecao);
            janela.LinhaDoTempo.SelectedIndex = 0;
            Assert.True(janela.MenuRemoverComposicao.IsEnabled);
            janela.MenuRemoverComposicao.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            await janela.UltimaOperacaoComposicao;
            Assert.Single(sessao.Atual.LinhaDoTempo); Assert.Single(sessao.Atual.Biblioteca);
            Assert.Equal(EstadoPrevia.Vazia, janela.PreviaAtual.Atual.Estado);
            await janela.ReproduzirComposicaoAsync(false);
            Assert.Equal(TempoMidia.Segundos(2), preparador.Ultimo!.Duracao);
            janela.MenuDesfazer.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Assert.Equal(2, sessao.Atual.LinhaDoTempo.Length);
            janela.Biblioteca.SelectedIndex = 0;
            Assert.True(janela.BotaoRemoverMidia.IsEnabled);
        }, fabrica, preparador: preparador);
    }

    private static void PressionarEspaco(UIElement origem)
    {
        var evento = new KeyEventArgs(new TecladoTeste(), PresentationSource.FromVisual(origem)!, Environment.TickCount, Key.Space)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        origem.RaiseEvent(evento);
        Assert.True(evento.Handled);
    }

    [Fact]
    public Task CancelarPreparacaoPermiteNovaTentativa()
    {
        var preparador = new Preparador { Aguardar = true };
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            janela.ExibirProjeto(ProjetoTeste(), salvo: true, caminho: caminho);
            janela.UpdateLayout();
            await janela.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            janela.BotaoReproduzirPrevia.Focus();
            janela.BotaoReproduzirPrevia.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var operacao = janela.UltimaOperacaoComposicao;
            Assert.True(janela.BotaoCancelarComposicao.IsEnabled);
            Assert.True(janela.BotaoReproduzirPrevia.IsEnabled);
            Assert.Equal("Reproduzir", janela.BotaoReproduzirPrevia.Content);
            janela.BotaoSalvar.Focus();
            janela.BotaoCancelarComposicao.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await operacao;
            Assert.Same(janela.BotaoSalvar, FocusManager.GetFocusedElement(janela));
            Assert.Equal(EstadoPrevia.Vazia, janela.PreviaAtual.Atual.Estado);
            Assert.True(janela.BotaoReproduzirPrevia.IsEnabled);
            preparador.Aguardar = false;
            janela.BotaoReproduzirPrevia.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await janela.UltimaOperacaoComposicao;
            Assert.Equal(EstadoPrevia.Reproduzindo, janela.PreviaAtual.Atual.Estado);
            Assert.Equal("Pausar", janela.BotaoReproduzirPrevia.Content);
            Assert.Equal("Pausar", System.Windows.Automation.AutomationProperties.GetName(janela.BotaoReproduzirPrevia));
            janela.BotaoReproduzirPrevia.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await janela.UltimaOperacaoComposicao;
            Assert.Equal(EstadoPrevia.Pausada, janela.PreviaAtual.Atual.Estado);
            Assert.Equal("Reproduzir", janela.BotaoReproduzirPrevia.Content);
        }, new Fabrica(), preparador: preparador);
    }

    [Fact]
    public Task EditarDurantePreparacaoCancelaTrabalhoObsoletoSemAnuncioIntermediario()
    {
        var preparador = new Preparador { Aguardar = true };
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            janela.ExibirProjeto(ProjetoTeste() with { Cursor = TempoMidia.Segundos(1) }, salvo: true, caminho: caminho);
            janela.UpdateLayout();
            await janela.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            janela.LinhaDoTempo.Focus();
            Assert.Same(janela.LinhaDoTempo, FocusManager.GetFocusedElement(janela));
            var operacao = janela.ReproduzirComposicaoAsync(false);
            Assert.False(operacao.IsCompleted);
            Assert.True(janela.Estrutura.IsEnabled);
            janela.MenuDividir.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await janela.UltimaEdicao.WaitAsync(TimeSpan.FromSeconds(5));
            await operacao;
            Assert.Equal(3, janela.SessaoAtual!.Atual.LinhaDoTempo.Length);
            Assert.Equal(TempoMidia.Segundos(1), janela.SessaoAtual.Atual.Cursor);
            Assert.Same(janela.LinhaDoTempo, FocusManager.GetFocusedElement(janela));
            Assert.DoesNotContain(janela.Mensagens.Items.Cast<object>(), m => m.ToString()!.Contains("cancelada", StringComparison.OrdinalIgnoreCase));
            preparador.Aguardar = false;
            await janela.ReproduzirComposicaoAsync(false);
            Assert.Equal(EstadoPrevia.Reproduzindo, janela.PreviaAtual.Atual.Estado);
        }, new Fabrica(), preparador: preparador);
    }

    [Fact]
    public Task NavegacaoPausaMostraDestinoPreservaFimERevalidaAntesDeRetomar()
    {
        var fabrica = new Fabrica(); var preparador = new Preparador();
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            janela.ExibirProjeto(ProjetoTeste(), salvo: true, caminho: caminho);
            await janela.ReproduzirComposicaoAsync(false);
            var mensagensAntes = janela.Mensagens.Items.Count;
            await janela.NavegarTemporalAsync(Key.Right, ModifierKeys.Control);
            Assert.Equal("Avançou 1 segundo. Atualmente em 0 minutos e 1 segundo.", janela.Situacao.Text);
            Assert.Equal(mensagensAntes + 1, janela.Mensagens.Items.Count);
            Assert.Equal(EstadoPrevia.Pausada, janela.PreviaAtual.Atual.Estado);
            Assert.Equal(TempoMidia.Segundos(1), janela.SessaoAtual!.Atual.Cursor);
            Assert.Equal(TempoMidia.Segundos(1), preparador.QuadroSolicitado);
            await janela.NavegarTemporalAsync(Key.End, ModifierKeys.None);
            janela.SincronizarCursorComposicao();
            Assert.Equal(TempoMidia.Segundos(4), janela.SessaoAtual.Atual.Cursor);
            Assert.False(janela.SessaoAtual.PodeDesfazer);
            await janela.ReproduzirComposicaoAsync(false);
            Assert.Equal(TempoMidia.Zero, fabrica.Motor!.Posicao);
            Assert.Equal(1, preparador.Preparacoes);
            await janela.ReproduzirComposicaoAsync(false);
            preparador.Atualizada = false;
            await janela.ReproduzirComposicaoAsync(false);
            Assert.Equal(2, preparador.Preparacoes);
            Assert.Equal(EstadoPrevia.Reproduzindo, janela.PreviaAtual.Atual.Estado);
            Assert.Equal("Pausar", janela.BotaoReproduzirPrevia.Content);
            Assert.Equal("Pausar", System.Windows.Automation.AutomationProperties.GetName(janela.BotaoReproduzirPrevia));
            await janela.NavegarTemporalAsync(Key.End, ModifierKeys.None);
            await janela.LimparPreviaAsync();
            Assert.Equal(TempoMidia.Segundos(4), janela.SessaoAtual.Atual.Cursor);
        }, fabrica, preparador: preparador);
    }

    [Fact]
    public Task BuscaMaisRecenteCancelaQuadroAnteriorSemMoverCursorDeVolta()
    {
        var preparador = new Preparador { AguardarPrimeiroQuadro = true };
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            janela.ExibirProjeto(ProjetoTeste(), salvo: true, caminho: caminho);
            await janela.ReproduzirComposicaoAsync(false);
            var primeira = janela.NavegarTemporalAsync(Key.Right, ModifierKeys.Control);
            await preparador.QuadroIniciado.Task;
            var segunda = janela.NavegarTemporalAsync(Key.Right, ModifierKeys.Control);
            await Task.WhenAll(primeira, segunda);
            Assert.True(preparador.QuadroCancelado);
            Assert.Equal(TempoMidia.Segundos(2), preparador.QuadroSolicitado);
            janela.SincronizarCursorComposicao();
            Assert.Equal(TempoMidia.Segundos(2), janela.SessaoAtual!.Atual.Cursor);
            Assert.Equal(EstadoPrevia.Pausada, janela.PreviaAtual.Atual.Estado);
        }, new Fabrica(), preparador: preparador);
    }

    [Fact]
    public Task FecharContextoCancelaLeituraDeQuadrosSemPublicarResultadoAntigo()
    {
        var preparador = new Preparador { AguardarIndice = true };
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            janela.ExibirProjeto(ProjetoTeste(), salvo: true, caminho: caminho);
            var navegacao = janela.NavegarTemporalAsync(Key.Right, ModifierKeys.None);
            await preparador.IndiceIniciado.Task;
            Assert.True(janela.BotaoCancelarComposicao.IsEnabled);
            await janela.LimparPreviaAsync();
            await navegacao;
            Assert.Equal(TempoMidia.Zero, janela.SessaoAtual!.Atual.Cursor);
            Assert.Equal(EstadoPrevia.Vazia, janela.PreviaAtual.Atual.Estado);
            preparador.AguardarIndice = false;
            await janela.NavegarTemporalAsync(Key.Right, ModifierKeys.None);
            Assert.Equal(new TempoMidia(1, 30), janela.SessaoAtual.Atual.Cursor);
        }, new Fabrica(), preparador: preparador);
    }

    private sealed class Preparador : IPreparadorComposicao
    {
        public Projeto? Ultimo { get; private set; }
        public bool Aguardar { get; set; }
        public bool Atualizada { get; set; } = true;
        public int Preparacoes { get; private set; }
        public TempoMidia? QuadroSolicitado { get; private set; }
        public bool AguardarPrimeiroQuadro { get; set; }
        public bool AguardarIndice { get; set; }
        public TaskCompletionSource IndiceIniciado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<TempoMidia> ProximaPosicaoAsync(Projeto projeto, int direcao, CancellationToken token)
        {
            IndiceIniciado.TrySetResult();
            if (AguardarIndice) await Task.Delay(Timeout.Infinite, token);
            return projeto.Cursor.QuadroAdjacente(new(30, 1), direcao);
        }
        public bool QuadroCancelado { get; private set; }
        public TaskCompletionSource QuadroIniciado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> EstaAtualizadaAsync(Projeto projeto, CancellationToken token) => Task.FromResult(Atualizada);
        public async Task<QuadroComposicao?> ObterQuadroAsync(Projeto projeto, TempoMidia posicao, CancellationToken token)
        {
            QuadroSolicitado = posicao;
            if (AguardarPrimeiroQuadro)
            {
                AguardarPrimeiroQuadro = false; QuadroIniciado.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { QuadroCancelado = true; throw; }
            }
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96,
                System.Windows.Media.PixelFormats.Rgb24, null, new byte[] { 0, 0, 0 }, 3);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = new System.IO.MemoryStream();
            encoder.Save(stream);
            return new(posicao, stream.ToArray());
        }
        public async Task<Midia> PrepararAsync(Projeto projeto, IProgress<string>? progresso, CancellationToken token)
        {
            Preparacoes++;
            Ultimo = projeto;
            if (Aguardar) await Task.Delay(Timeout.Infinite, token);
            return new(Guid.NewGuid(), "Composição", "previa.mp4", TipoMidia.Video, projeto.Duracao);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Fabrica : IFabricaReprodutorMidia
    {
        public Motor? Motor { get; private set; }
        public Task<IReprodutorMidia> CriarAsync(Midia midia, CancellationToken token) => Task.FromResult<IReprodutorMidia>(Motor = new());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Motor : IReprodutorMidia
    {
        public event Action<EventoReprodutor>? Alterado { add { } remove { } }
        public TempoMidia Posicao { get; set; }
        public Task CarregarAsync(CancellationToken token) => Task.CompletedTask;
        public Task ReproduzirAsync(CancellationToken token) => Task.CompletedTask;
        public Task PausarAsync(CancellationToken token) => Task.CompletedTask;
        public Task PosicionarAsync(TempoMidia posicao, CancellationToken token) { Posicao = posicao; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
