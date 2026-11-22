using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SonarFrame.Application;
using SonarFrame.Core;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class TestesTimelineVisual
{
    private static Projeto Amostra()
    {
        var sessao = new SessaoEdicao(Projeto.Criar("Timeline visual"));
        var video = new Midia(Guid.NewGuid(), "Cena", "cena.mp4", TipoMidia.Video, TempoMidia.Segundos(10));
        var imagem = new Midia(Guid.NewGuid(), "Selo", "selo.png", TipoMidia.Imagem, null);
        sessao.Executar(new RegistrarMidia(video));
        sessao.Executar(new RegistrarMidia(imagem));
        for (var i = 0; i < 3; i++) sessao.Executar(new InserirClipe(video.Id, i));
        sessao.Executar(new AdicionarCamada(imagem.Id, TempoMidia.Segundos(2), TempoMidia.Zero, TempoMidia.Segundos(3)));
        return sessao.Atual;
    }

    [Fact]
    public void ProjecaoPreservaOrdemIntervalosECamadasEmFaixasSeparadas()
    {
        var projeto = Amostra();
        var modelo = ModeloTimelineVisual.Criar(projeto);
        Assert.Equal(projeto.LinhaDoTempo.Select(c => c.Id), modelo.Itens.Where(i => i.Faixa == 0).Select(i => i.Id));
        Assert.Equal(new[] { 0, 10, 20, 2 }.Select(s => TempoMidia.Segundos(s)), modelo.Itens.Select(i => i.Inicio));
        Assert.Equal(1, modelo.Itens[^1].Faixa);
        Assert.Equal("Imagem", modelo.Itens[^1].Tipo);
        Assert.Equal(projeto, projeto.Validar());
    }

    [Fact]
    public void ConversaoPreservaExtremosFracoesELimitesDaTela()
    {
        var duracao = new TempoMidia(1001, 30);
        Assert.Equal(new TempoMidia(1001, 60), ModeloTimelineVisual.TempoNaPosicao(50, 100, duracao));
        Assert.Equal(TempoMidia.Zero, ModeloTimelineVisual.TempoNaPosicao(-10, 100, duracao));
        Assert.Equal(duracao, ModeloTimelineVisual.TempoNaPosicao(200, 100, duracao));
        Assert.Equal(TempoMidia.Zero, ModeloTimelineVisual.TempoNaPosicao(double.NaN, 100, duracao));
        Assert.Equal(TempoMidia.Zero, ModeloTimelineVisual.TempoNaPosicao(10, 0, duracao));
        Assert.Equal(50, ModeloTimelineVisual.PosicaoNoTempo(new TempoMidia(1001, 60), 100, duracao));
    }

    [Fact]
    public void DestinoConsideraRemocaoDaOrigemENaoReordenaCamadas()
    {
        var modelo = ModeloTimelineVisual.Criar(Amostra());
        Assert.Equal(2, modelo.DestinoReordenacao(modelo.Itens[0].Id, TempoMidia.Segundos(30)));
        Assert.Equal(0, modelo.DestinoReordenacao(modelo.Itens[2].Id, TempoMidia.Zero));
        Assert.Equal(1, modelo.DestinoReordenacao(modelo.Itens[1].Id, TempoMidia.Segundos(15)));
        Assert.Equal(-1, modelo.DestinoReordenacao(modelo.Itens[3].Id, TempoMidia.Zero));
    }

    [Fact]
    public Task GestosCompartilhamCursorSelecaoEHistoricoComLista() => TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
    {
        var projeto = Amostra();
        janela.ExibirProjeto(projeto, salvo: true, caminho: caminho);
        var sessao = janela.SessaoAtual!;
        await janela.AplicarGestoTimelineAsync(new(projeto.LinhaDoTempo[1].Id, TempoMidia.Segundos(12), TempoMidia.Segundos(12), false, null));
        Assert.Equal(TempoMidia.Segundos(12), sessao.Atual.Cursor);
        Assert.Equal(projeto.LinhaDoTempo[1].Id, sessao.Atual.ItemSelecionadoId);
        Assert.Equal(sessao.Atual.Cursor, janela.TimelineVisual.Modelo!.Cursor);
        Assert.False(sessao.PodeDesfazer);
        await janela.AplicarGestoTimelineAsync(new(null, TempoMidia.Segundos(18), TempoMidia.Segundos(4), false, null));
        Assert.Equal(new SelecaoTemporal(TempoMidia.Segundos(18), TempoMidia.Segundos(4)), sessao.Atual.Selecao);
        Assert.True(janela.MenuExcluirSelecao.IsEnabled);
        Assert.False(sessao.PodeDesfazer);
        await janela.AplicarGestoTimelineAsync(new(projeto.LinhaDoTempo[0].Id, TempoMidia.Zero, TempoMidia.Zero, false, 2));
        Assert.Equal(new[] { projeto.LinhaDoTempo[1], projeto.LinhaDoTempo[2], projeto.LinhaDoTempo[0] }, sessao.Atual.LinhaDoTempo);
        Assert.Equal(projeto.Camadas, sessao.Atual.Camadas);
        Assert.True(sessao.PodeDesfazer);
        sessao.Desfazer(); janela.Atualizar();
        Assert.Equal(projeto.LinhaDoTempo, sessao.Atual.LinhaDoTempo);
        Assert.False(sessao.PodeDesfazer);
        sessao.Refazer(); janela.Atualizar();
        Assert.Equal(projeto.LinhaDoTempo[0].Id, janela.TimelineVisual.Modelo!.Itens[2].Id);
    });

    [Fact]
    public Task ReordenacaoVisualEquivaleAoMenuEIgnoraDestinoInvalido() => TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
    {
        var projeto = Amostra();
        janela.ExibirProjeto(projeto, salvo: true, caminho: caminho);
        await janela.AplicarGestoTimelineAsync(new(projeto.LinhaDoTempo[0].Id, TempoMidia.Zero, TempoMidia.Zero, false, 99));
        Assert.False(janela.SessaoAtual!.PodeDesfazer);
        await janela.AplicarGestoTimelineAsync(new(projeto.LinhaDoTempo[0].Id, TempoMidia.Zero, TempoMidia.Zero, false, 2));
        var visual = janela.SessaoAtual.Atual.LinhaDoTempo;
        janela.ExibirProjeto(projeto, salvo: true, caminho: caminho);
        janela.LinhaDoTempo.SelectedIndex = 0;
        janela.MenuMoverFim.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await janela.UltimaEdicao;
        Assert.Equal(visual.ToArray(), janela.SessaoAtual!.Atual.LinhaDoTempo.ToArray());
    });

    [Fact]
    public Task EscapePerdaDeCapturaEAlteracaoDoProjetoCancelamSemEditar() => TestesIntegracaoEditor.ComJanela((janela, caminho) =>
    {
        janela.ExibirProjeto(Amostra(), salvo: true, caminho: caminho);
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        janela.UpdateLayout();
        var superficie = janela.TimelineVisual;
        var antes = janela.SessaoAtual!.Atual;
        superficie.IniciarGesto(new Point(10, 10));
        Assert.True(superficie.EmGesto);
        var evento = new KeyEventArgs(new TecladoTeste(), PresentationSource.FromVisual(janela)!, Environment.TickCount, Key.Escape)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        janela.LinhaDoTempo.RaiseEvent(evento);
        Assert.True(evento.Handled);
        Assert.False(superficie.EmGesto);
        superficie.ConcluirGesto(new Point(200, 10));
        Assert.Same(antes, janela.SessaoAtual.Atual);
        superficie.IniciarGesto(new Point(10, 10));
        superficie.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.LostMouseCaptureEvent });
        Assert.False(superficie.EmGesto);
        superficie.IniciarGesto(new Point(10, 10));
        janela.SessaoAtual.Executar(new MoverClipe(antes.LinhaDoTempo[0].Id, 2));
        janela.Atualizar();
        Assert.False(superficie.EmGesto);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SuperficieDistingueReguaClipesCamadasEZoom() => TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
    {
        janela.ExibirProjeto(Amostra(), salvo: true, caminho: caminho);
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        janela.UpdateLayout();
        var superficie = janela.TimelineVisual;
        var largura = superficie.ActualWidth;
        Assert.Null(superficie.ItemEm(new Point(10, 10)));
        Assert.Equal(0, superficie.ItemEm(new Point(10, 40))!.Faixa);
        Assert.Equal(1, superficie.ItemEm(new Point(8 + (largura - 16) / 10, 90))!.Faixa);
        var primeiro = janela.SessaoAtual!.Atual.LinhaDoTempo[0].Id;
        superficie.IniciarGesto(new Point(10, 40));
        Assert.False(janela.SessaoAtual.PodeDesfazer);
        superficie.ConcluirGesto(new Point(largura - 10, 40));
        await janela.UltimoGestoTimeline;
        Assert.Equal(primeiro, janela.SessaoAtual.Atual.LinhaDoTempo[^1].Id);
        janela.BotaoAmpliarTimeline.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        janela.UpdateLayout();
        Assert.True(superficie.ActualWidth > largura * 1.9);
        Assert.False(superficie.Focusable);
        janela.BotaoAjustarTimeline.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        janela.UpdateLayout();
        Assert.Equal(largura, superficie.ActualWidth, 1);
        var regiao = janela.RegiaoLinhaDoTempo;
        regiao.BringIntoView();
        janela.UpdateLayout();
        await janela.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        var imagem = new System.Windows.Media.Imaging.RenderTargetBitmap((int)regiao.ActualWidth, (int)regiao.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var desenho = new System.Windows.Media.DrawingVisual();
        using (var contexto = desenho.RenderOpen())
            contexto.DrawRectangle(new System.Windows.Media.VisualBrush(regiao), null, new Rect(0, 0, regiao.ActualWidth, regiao.ActualHeight));
        imagem.Render(desenho);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(imagem));
        using var arquivo = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, "timeline-visual.png"));
        png.Save(arquivo);
    });
}
