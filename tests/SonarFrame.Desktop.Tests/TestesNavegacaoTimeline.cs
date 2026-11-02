using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SonarFrame.Application;
using SonarFrame.Core;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class TestesNavegacaoTimeline
{
    [Fact]
    public Task TimelineOrdenaPorTempoPreservaSelecaoNasAcoesEMoveClipeCorreto() => TestesIntegracaoEditor.ComJanela((janela, caminho) =>
    {
        var sessao = new SessaoEdicao(Projeto.Criar("Ordem temporal"));
        var video = new Midia(Guid.NewGuid(), "Vídeo", "video.mp4", TipoMidia.Video, TempoMidia.Segundos(10));
        var foto = new Midia(Guid.NewGuid(), "Foto", "foto.png", TipoMidia.Imagem, null);
        sessao.Executar(new RegistrarMidia(video)); sessao.Executar(new RegistrarMidia(foto));
        sessao.Executar(new InserirClipe(video.Id, 0));
        sessao.Executar(new InserirClipe(foto.Id, 1, TempoMidia.Segundos(5)));
        sessao.Executar(new InserirClipe(video.Id, 2));
        sessao.Executar(new AdicionarCamada(foto.Id, TempoMidia.Segundos(12), TempoMidia.Zero, TempoMidia.Segundos(1)));
        sessao.Executar(new AdicionarCamada(foto.Id, TempoMidia.Segundos(2), TempoMidia.Zero, TempoMidia.Segundos(1)));
        var conteudos = new AcoesEdicao(sessao).Conteudos();
        Assert.Equal(new[] { 0, 2, 10, 12, 15 }.Select(s => TempoMidia.Segundos(s)), conteudos.Select(c => c.Inicio));
        var fotoId = sessao.Atual.LinhaDoTempo[1].Id;
        janela.ExibirProjeto(sessao.Atual, salvo: true, caminho: caminho);
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        Assert.Contains("início no vídeo " + TempoMidia.Segundos(10), janela.LinhaDoTempo.Items[2].ToString());
        Assert.Contains("fim " + TempoMidia.Segundos(15), janela.LinhaDoTempo.Items[2].ToString());
        janela.LinhaDoTempo.SelectedIndex = 2;
        janela.LinhaDoTempo.Focus();
        Pressionar(janela.LinhaDoTempo, Key.Tab);
        Assert.Same(janela.BotaoRemoverComposicao, FocusManager.GetFocusedElement(janela));
        Pressionar(janela.BotaoRemoverComposicao, Key.Tab);
        Assert.Same(janela.BotaoDividir, FocusManager.GetFocusedElement(janela));
        Pressionar(janela.BotaoDividir, Key.Tab);
        Assert.Same(janela.BotaoMoverAntes, FocusManager.GetFocusedElement(janela));
        Assert.Equal(fotoId, janela.SessaoAtual!.Atual.ItemSelecionadoId);
        janela.BotaoMoverAntes.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(fotoId, janela.SessaoAtual.Atual.LinhaDoTempo[0].Id);
        Assert.Equal(fotoId, janela.SessaoAtual.Atual.ItemSelecionadoId);
        janela.LinhaDoTempo.SelectedIndex = janela.LinhaDoTempo.Items.Count - 1;
        janela.LinhaDoTempo.Focus();
        Pressionar(janela.LinhaDoTempo, Key.Down);
        Assert.Same(janela.LinhaDoTempo, FocusManager.GetFocusedElement(janela));
        return Task.CompletedTask;
    });

    private static void Pressionar(UIElement origem, Key tecla)
    {
        var evento = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(origem)!, Environment.TickCount, tecla)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        origem.RaiseEvent(evento);
        Assert.True(evento.Handled);
    }
}
