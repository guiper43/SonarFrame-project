using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SonarFrame.Application;
using SonarFrame.Core;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class TestesPreviaBiblioteca
{
    [Fact]
    public Task FimDaListaNaoSaiParaAcoesEDeleteRemovePreviaComDesfazer()
    {
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var midia = new Midia(Guid.NewGuid(), "Vídeo", "teste.mp4", TipoMidia.Video, TempoMidia.Segundos(10));
            janela.ExibirProjeto(Projeto.Criar("Biblioteca") with { Biblioteca = [midia] }, salvo: true, caminho: caminho);
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            janela.Biblioteca.SelectedIndex = 0;
            await janela.AbrirPreviaSelecionadaAsync();
            janela.Biblioteca.UpdateLayout();
            var item = (ListBoxItem)janela.Biblioteca.ItemContainerGenerator.ContainerFromIndex(0);
            item.Focus();
            foreach (var tecla in new[] { Key.Up, Key.Down, Key.Delete })
            {
                item.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(item)!, Environment.TickCount, tecla)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                if (tecla != Key.Delete) Assert.Same(item, FocusManager.GetFocusedElement(janela));
            }
            await janela.UltimaRemocaoBiblioteca;
            Assert.Empty(janela.SessaoAtual!.Atual.Biblioteca);
            Assert.Equal(EstadoPrevia.Vazia, janela.PreviaAtual.Atual.Estado);
            janela.SessaoAtual.Desfazer();
            Assert.Equal(midia, Assert.Single(janela.SessaoAtual.Atual.Biblioteca));
        }, new Fabrica());
    }

    [Fact]
    public Task ArquivoNoInicioDaListaAcessaAcoesComTabEAbrePreviaComEnter()
    {
        var fabrica = new Fabrica();
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var midias = Enumerable.Range(1, 20).Select(i => new Midia(Guid.NewGuid(), $"Vídeo {i}", $"teste{i}.mp4", TipoMidia.Video, TempoMidia.Segundos(10))).ToImmutableArray();
            janela.ExibirProjeto(Projeto.Criar("Biblioteca") with { Biblioteca = midias }, salvo: true, caminho: caminho);
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            janela.Biblioteca.SelectedIndex = 0;
            janela.Biblioteca.UpdateLayout();
            var item = (ListBoxItem)janela.Biblioteca.ItemContainerGenerator.ContainerFromIndex(0);
            item.Focus();
            item.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Assert.Same(janela.BotaoAbrirPrevia, FocusManager.GetFocusedElement(janela));
            janela.BotaoAbrirPrevia.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Assert.Same(janela.BotaoInserir, FocusManager.GetFocusedElement(janela));
            janela.BotaoInserir.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
            janela.BotaoAbrirPrevia.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
            Assert.Equal(0, janela.Biblioteca.SelectedIndex);
            Assert.Equal(0, fabrica.Aberturas);
            item.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(item)!, Environment.TickCount, Key.Enter)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await janela.UltimaOperacaoPrevia;
            Assert.Equal(1, fabrica.Aberturas);
            Assert.Equal(midias[0].Id, janela.PreviaAtual.Atual.Midia!.Id);
            Assert.Empty(janela.SessaoAtual!.Atual.LinhaDoTempo);
        }, fabrica);
    }

    [Fact]
    public Task TabEntraNaAcaoDeReproducaoMesmoDepoisDeVisitarOutrosControles()
    {
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var midia = new Midia(Guid.NewGuid(), "Vídeo", "teste.mp4", TipoMidia.Video, TempoMidia.Segundos(10));
            janela.ExibirProjeto(Projeto.Criar("Prévia") with { Biblioteca = [midia] }, salvo: true, caminho: caminho);
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            janela.Biblioteca.SelectedIndex = 0;
            await janela.AbrirPreviaSelecionadaAsync();
            janela.BotaoBuscarPrevia.Focus();
            janela.BotaoRemoverMidia.Focus();
            PressionarTab(janela.BotaoRemoverMidia);
            Assert.Same(janela.BotaoReproduzirPrevia, FocusManager.GetFocusedElement(janela));
            Assert.Equal("Pausar", janela.BotaoReproduzirPrevia.Content);
            janela.BotaoReproduzirPrevia.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await janela.UltimaOperacaoPrevia;
            janela.BotaoRemoverMidia.Focus();
            PressionarTab(janela.BotaoRemoverMidia);
            Assert.Same(janela.BotaoReproduzirPrevia, FocusManager.GetFocusedElement(janela));
            Assert.Equal("Reproduzir", janela.BotaoReproduzirPrevia.Content);
            PressionarTab(janela.BotaoReproduzirPrevia);
            Assert.Same(janela.BotaoPararPrevia, FocusManager.GetFocusedElement(janela));
            await janela.LimparPreviaAsync();
            janela.BotaoRemoverMidia.Focus();
            PressionarTab(janela.BotaoRemoverMidia);
            Assert.Same(janela.BotaoPrevia, FocusManager.GetFocusedElement(janela));
        }, new Fabrica());
    }

    private static void PressionarTab(UIElement origem)
    {
        var evento = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(origem)!, Environment.TickCount, Key.Tab)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        origem.RaiseEvent(evento);
        Assert.True(evento.Handled);
    }

    [Fact]
    public Task PreviaNaoEditaProjetoEControlesAcompanhamMidiaAberta()
    {
        var fabrica = new Fabrica();
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var video = new Midia(Guid.NewGuid(), "Vídeo", "teste.mp4", TipoMidia.Video, TempoMidia.Segundos(10));
            var foto = new Midia(Guid.NewGuid(), "Foto", "teste.png", TipoMidia.Imagem, null);
            var sessao = new SessaoEdicao(Projeto.Criar("Prévia"));
            sessao.Executar(new RegistrarMidia(video)); sessao.Executar(new RegistrarMidia(foto)); sessao.Executar(new InserirClipe(video.Id, 0));
            sessao.Posicionar(TempoMidia.Segundos(2));
            janela.ExibirProjeto(sessao.Atual, salvo: true, caminho: caminho);
            var original = janela.SessaoAtual!.Atual;
            janela.Biblioteca.SelectedIndex = 0;
            Assert.Equal(0, fabrica.Aberturas);
            Assert.True(janela.BotaoAbrirPrevia.IsEnabled);
            janela.BotaoAbrirPrevia.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await janela.UltimaOperacaoPrevia;
            Assert.True(janela.BotaoReproduzirPrevia.IsEnabled); Assert.True(janela.DestinoPrevia.IsEnabled);
            Assert.True(await janela.SalvarAtualAsync());
            Assert.True(janela.BotaoAbrirPrevia.IsEnabled); Assert.Equal(EstadoPrevia.Reproduzindo, janela.PreviaAtual.Atual.Estado);
            janela.Biblioteca.SelectedIndex = 1;
            Assert.Equal(1, fabrica.Aberturas); Assert.True(janela.BotaoReproduzirPrevia.IsEnabled);
            await janela.AbrirPreviaSelecionadaAsync();
            Assert.False(janela.BotaoReproduzirPrevia.IsEnabled); Assert.False(janela.BotaoPararPrevia.IsEnabled);
            Assert.False(janela.BotaoBuscarPrevia.IsEnabled); Assert.False(janela.DestinoPrevia.IsEnabled);
            Assert.Contains("Imagem estática", janela.PosicaoPrevia.Text);
            janela.Biblioteca.SelectedItems.Add(janela.Biblioteca.Items[0]);
            Assert.False(janela.BotaoAbrirPrevia.IsEnabled);
            await janela.AbrirPreviaSelecionadaAsync(); Assert.Equal(2, fabrica.Aberturas);
            Assert.Same(original, janela.SessaoAtual.Atual); Assert.False(janela.SessaoAtual.TemAlteracoes);
            Assert.False(janela.SessaoAtual.PodeDesfazer);
            janela.Biblioteca.SelectedItems.Clear(); janela.Biblioteca.SelectedIndex = 0;
            await janela.AbrirPreviaSelecionadaAsync(); Assert.True(janela.BotaoBuscarPrevia.IsEnabled);
            janela.BotaoDividir.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(EstadoPrevia.Pausada, janela.PreviaAtual.Atual.Estado);
            Assert.Equal(2, janela.SessaoAtual.Atual.LinhaDoTempo.Length);
            await janela.LimparPreviaAsync(); Assert.True(janela.BotaoReproduzirPrevia.IsEnabled);
        }, fabrica);
    }

    [Fact]
    public Task CancelamentoMantemEditorDisponivel()
    {
        var fabrica = new Fabrica { Aguardar = true };
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var midia = new Midia(Guid.NewGuid(), "Vídeo", "teste.mp4", TipoMidia.Video, TempoMidia.Segundos(10));
            janela.ExibirProjeto(Projeto.Criar("Prévia") with { Biblioteca = [midia] }, salvo: true, caminho: caminho);
            janela.Biblioteca.SelectedIndex = 0;
            var abertura = janela.AbrirPreviaSelecionadaAsync();
            Assert.True(janela.BotaoCancelarPrevia.IsEnabled); Assert.True(janela.Estrutura.IsEnabled);
            janela.BotaoCancelarPrevia.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await abertura;
            Assert.False(janela.BotaoCancelarPrevia.IsEnabled);
            Assert.Equal(EstadoPrevia.Vazia, janela.PreviaAtual.Atual.Estado);
        }, fabrica);
    }

    private sealed class Fabrica : IFabricaReprodutorMidia
    {
        public int Aberturas { get; private set; }
        public bool Aguardar { get; init; }
        public Task<IReprodutorMidia> CriarAsync(Midia midia, CancellationToken token)
        { Aberturas++; return Task.FromResult<IReprodutorMidia>(new Motor(Aguardar)); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Motor(bool aguardar) : IReprodutorMidia
    {
        public event Action<EventoReprodutor>? Alterado { add { } remove { } }
        public TempoMidia Posicao => TempoMidia.Segundos(1);
        public Task CarregarAsync(CancellationToken token) => aguardar ? Task.Delay(Timeout.Infinite, token) : Task.CompletedTask;
        public Task ReproduzirAsync(CancellationToken token) => Task.CompletedTask;
        public Task PausarAsync(CancellationToken token) => Task.CompletedTask;
        public Task PosicionarAsync(TempoMidia posicao, CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
