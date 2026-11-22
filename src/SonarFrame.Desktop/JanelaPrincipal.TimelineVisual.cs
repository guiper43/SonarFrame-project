using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using SonarFrame.Application;
using SonarFrame.Core;

namespace SonarFrame.Desktop;

public partial class JanelaPrincipal
{
    internal SuperficieTimelineVisual TimelineVisual { get; } = new();
    internal Button BotaoAmpliarTimeline { get; } = new() { Content = "Ampliar timeline" };
    internal Button BotaoReduzirTimeline { get; } = new() { Content = "Reduzir timeline" };
    internal Button BotaoAjustarTimeline { get; } = new() { Content = "Ajustar timeline à janela" };
    private readonly TextBlock resumoZoomTimeline = new();
    private ScrollViewer rolagemTimeline = null!;
    private Projeto? projetoTimeline;
    private SessaoEdicao? sessaoTimeline;
    private int zoomTimeline = 1;
    private bool aplicandoGestoTimeline;
    internal Task UltimoGestoTimeline { get; private set; } = Task.CompletedTask;

    private void ConfigurarTimelineVisual()
    {
        var painel = (StackPanel)RegiaoLinhaDoTempo.Content;
        var controles = new WrapPanel();
        controles.Children.Add(BotaoReduzirTimeline);
        controles.Children.Add(BotaoAmpliarTimeline);
        controles.Children.Add(BotaoAjustarTimeline);
        controles.Children.Add(resumoZoomTimeline);
        rolagemTimeline = new ScrollViewer
        {
            Content = TimelineVisual, MaxHeight = 260,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Focusable = false
        };
        painel.Children.Insert(2, new TextBlock
        {
            Text = "Timeline gráfica: clique para navegar; arraste na régua para selecionar; arraste clipes para reordenar. Escape cancela. A lista abaixo oferece as mesmas ações por teclado.",
            TextWrapping = TextWrapping.Wrap
        });
        painel.Children.Insert(3, controles);
        painel.Children.Insert(4, rolagemTimeline);
        AutomationProperties.SetName(TimelineVisual, "Timeline gráfica complementar");
        BotaoAmpliarTimeline.Click += (_, _) => AlterarZoomTimeline(Math.Min(16, zoomTimeline * 2));
        BotaoReduzirTimeline.Click += (_, _) => AlterarZoomTimeline(Math.Max(1, zoomTimeline / 2));
        BotaoAjustarTimeline.Click += (_, _) => AlterarZoomTimeline(1);
        rolagemTimeline.SizeChanged += (_, _) => AtualizarLarguraTimeline();
        TimelineVisual.Confirmado += gesto => UltimoGestoTimeline = AplicarGestoTimelineAsync(gesto);
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (!TimelineVisual.EmGesto) return;
            TimelineVisual.Cancelar();
            if (e.Key == Key.Escape) { e.Handled = true; Anunciar("Gesto da timeline cancelado."); }
        }), true);
        Deactivated += (_, _) => TimelineVisual.Cancelar();
        AtualizarLarguraTimeline();
    }

    private void AlterarZoomTimeline(int zoom)
    {
        TimelineVisual.Cancelar();
        zoomTimeline = zoom;
        AtualizarLarguraTimeline();
        if (sessao is { } origem)
        {
            var x = 8 + ModeloTimelineVisual.PosicaoNoTempo(origem.Atual.Cursor, Math.Max(1, TimelineVisual.Width - 16), origem.Atual.Duracao);
            rolagemTimeline.ScrollToHorizontalOffset(Math.Max(0, x - rolagemTimeline.ViewportWidth / 2));
        }
        Anunciar($"Zoom da timeline: {zoomTimeline * 100}%.");
    }

    private void AtualizarLarguraTimeline()
    {
        TimelineVisual.Width = Math.Max(240, rolagemTimeline.ActualWidth - SystemParameters.VerticalScrollBarWidth - 2) * zoomTimeline;
        resumoZoomTimeline.Text = $"Zoom: {zoomTimeline * 100}%";
    }

    private void AtualizarTimelineVisual()
    {
        if (sessao is null) return;
        if (!ReferenceEquals(sessaoTimeline, sessao))
        {
            TimelineVisual.Cancelar();
            sessaoTimeline = sessao;
            projetoTimeline = null;
        }
        var projeto = sessao.Atual;
        if (ReferenceEquals(projetoTimeline, projeto)) return;
        var modelo = projetoTimeline is { } anterior && TimelineVisual.Modelo is { } atual
            && anterior.LinhaDoTempo == projeto.LinhaDoTempo && anterior.Camadas == projeto.Camadas && anterior.Biblioteca == projeto.Biblioteca
            ? atual with { Cursor = projeto.Cursor, Selecao = projeto.Selecao, Selecionado = projeto.ItemSelecionadoId }
            : ModeloTimelineVisual.Criar(projeto);
        projetoTimeline = projeto;
        TimelineVisual.Atualizar(modelo);
    }

    internal async Task AplicarGestoTimelineAsync(GestoTimelineVisual gesto)
    {
        if (sessao is not { } origem || aplicandoGestoTimeline || Ocupado || previaEncerrada) return;
        var snapshot = origem.Atual;
        if (gesto.Item is { } id && !snapshot.LinhaDoTempo.Any(c => c.Id == id) && !snapshot.Camadas.Any(c => c.Clipe.Id == id)) return;
        aplicandoGestoTimeline = true;
        TimelineVisual.IsEnabled = false;
        try
        {
            if (!await PrepararEdicaoAsync() || !ReferenceEquals(sessao, origem) || previaEncerrada) return;
            if (origem.Atual.LinhaDoTempo != snapshot.LinhaDoTempo || origem.Atual.Camadas != snapshot.Camadas) return;
            if (gesto.Destino is { } destino)
            {
                var indice = Array.FindIndex(snapshot.LinhaDoTempo.ToArray(), c => c.Id == gesto.Item);
                if (indice < 0 || destino < 0 || destino >= snapshot.LinhaDoTempo.Length) return;
                origem.SelecionarItem(gesto.Item);
                if (destino != indice) origem.Executar(new MoverClipe(gesto.Item!.Value, destino));
                contextoTemporalTimeline = false;
                exibirPropriedadesBiblioteca = false;
                Atualizar();
                FocarConteudoSelecionado();
                Anunciar($"Conteúdo na posição {destino + 1} de {snapshot.LinhaDoTempo.Length}.");
                return;
            }
            if (gesto.Item is not null) origem.SelecionarItem(gesto.Item);
            exibirPropriedadesBiblioteca = false;
            contextoTemporalTimeline = true;
            var selecionar = gesto.Inicio != gesto.Fim;
            if (selecionar) origem.Posicionar(gesto.Inicio);
            UltimaNavegacaoTemporal = NavegarTemporalAsync(Key.None,
                selecionar || gesto.Estender ? ModifierKeys.Shift : ModifierKeys.None, gesto.Fim);
            await UltimaNavegacaoTemporal;
            if (!ReferenceEquals(sessao, origem) || previaEncerrada) return;
            Atualizar();
            FocarConteudoSelecionado();
        }
        catch (Exception ex) { Anunciar(ex.Message); }
        finally { aplicandoGestoTimeline = false; TimelineVisual.IsEnabled = true; }
    }
}
