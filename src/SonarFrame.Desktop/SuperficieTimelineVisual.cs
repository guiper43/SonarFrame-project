using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SonarFrame.Core;

namespace SonarFrame.Desktop;

internal sealed record GestoTimelineVisual(Guid? Item, TempoMidia Inicio, TempoMidia Fim, bool Estender, int? Destino);

internal sealed class SuperficieTimelineVisual : Control
{
    private ModeloTimelineVisual? modelo;
    private Point? inicioGesto;
    private Point atualGesto;
    private ItemTimelineVisual? itemGesto;
    private bool estender;
    private bool gestoNaRegua;
    public event Action<GestoTimelineVisual>? Confirmado;
    public bool EmGesto => inicioGesto is not null;
    private const double Regua = 32;
    private const double AlturaFaixa = 44;
    private double LarguraUtil => Math.Max(1, ActualWidth - 16);
    public SuperficieTimelineVisual()
    {
        Focusable = false; IsTabStop = false; ClipToBounds = true; Cursor = Cursors.Cross;
        SetResourceReference(BackgroundProperty, SystemColors.WindowBrushKey);
        SetResourceReference(ForegroundProperty, SystemColors.WindowTextBrushKey);
        SetResourceReference(BorderBrushProperty, SystemColors.HighlightBrushKey);
        SizeChanged += (_, _) => Cancelar();
        IsEnabledChanged += (_, _) => { if (!IsEnabled) Cancelar(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) Cancelar(); };
    }

    public void Atualizar(ModeloTimelineVisual valor)
    {
        if (modelo is not null && (modelo.Duracao != valor.Duracao
            || !ReferenceEquals(modelo.Itens, valor.Itens) && !modelo.Itens.SequenceEqual(valor.Itens))) Cancelar();
        modelo = valor;
        Height = Regua + AlturaFaixa * (valor.Itens.Select(i => i.Faixa).DefaultIfEmpty(0).Max() + 1) + 28;
        InvalidateVisual();
    }

    public void Cancelar()
    {
        inicioGesto = null; itemGesto = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }

    internal ModeloTimelineVisual? Modelo => modelo;
    private double X(TempoMidia tempo) => 8 + ModeloTimelineVisual.PosicaoNoTempo(tempo, LarguraUtil, modelo!.Duracao);
    private TempoMidia Tempo(double x) => ModeloTimelineVisual.TempoNaPosicao(x - 8, LarguraUtil, modelo!.Duracao);
    internal ItemTimelineVisual? ItemEm(Point ponto) => modelo?.Itens.FirstOrDefault(i =>
        ponto.X >= 8 && ponto.X <= ActualWidth - 8
        && ponto.Y >= Regua + i.Faixa * AlturaFaixa && ponto.Y < Regua + (i.Faixa + 1) * AlturaFaixa
        && Tempo(ponto.X) >= i.Inicio && Tempo(ponto.X) < i.Inicio + i.Duracao);

    protected override void OnRender(DrawingContext desenho)
    {
        base.OnRender(desenho);
        desenho.DrawRectangle(Background, new Pen(Foreground, 1), new Rect(RenderSize));
        if (modelo is null || modelo.Duracao <= TempoMidia.Zero) { Texto(desenho, "Adicione conteúdos à composição.", 8, 6, ActualWidth - 16); return; }
        var marcas = Math.Max(1, (int)(LarguraUtil / 140));
        for (var i = 0; i <= marcas; i++)
        {
            var x = 8 + LarguraUtil * i / marcas;
            desenho.DrawLine(new Pen(Foreground, 1), new(x, 25), new(x, 31));
            if (i < marcas) Texto(desenho, Tempo(x).ToString(), x, 4, Math.Max(0, LarguraUtil / marcas - 4));
        }
        foreach (var item in modelo.Itens)
        {
            var retangulo = new Rect(X(item.Inicio), Regua + item.Faixa * AlturaFaixa + 2, Math.Max(1, X(item.Inicio + item.Duracao) - X(item.Inicio)), AlturaFaixa - 4);
            var selecionado = item.Id == modelo.Selecionado;
            desenho.DrawRectangle(Background, new Pen(selecionado ? BorderBrush : Foreground, selecionado ? 3 : 1), retangulo);
            Texto(desenho, $"{(selecionado ? "✓ " : "")}{item.Tipo}: {item.Nome}", retangulo.X + 4, retangulo.Y + 10, Math.Max(0, retangulo.Width - 8));
        }
        if ((inicioGesto is not null && gestoNaRegua ? new SelecaoTemporal(Tempo(inicioGesto.Value.X), Tempo(atualGesto.X)) : modelo.Selecao) is { } selecao)
            desenho.DrawRectangle(null, new Pen(Foreground, 2) { DashStyle = DashStyles.Dash },
                new Rect(X(selecao.Inicio), Regua, Math.Max(0, X(selecao.Fim) - X(selecao.Inicio)), Math.Max(0, ActualHeight - Regua - 2)));
        var cursor = X(modelo.Cursor);
        desenho.DrawLine(new Pen(BorderBrush, 2), new(cursor, 0), new(cursor, ActualHeight));
        desenho.DrawLine(new Pen(Foreground, 2), new(cursor - 4, 0), new(cursor, 6));
        desenho.DrawLine(new Pen(Foreground, 2), new(cursor + 4, 0), new(cursor, 6));
        if (inicioGesto is not null && itemGesto?.Faixa == 0)
        {
            var x = Math.Clamp(atualGesto.X, 8, 8 + LarguraUtil);
            desenho.DrawLine(new Pen(Foreground, 3) { DashStyle = DashStyles.Dash }, new(x, Regua), new(x, Regua + AlturaFaixa));
            Texto(desenho, $"Mover para posição {modelo.DestinoReordenacao(itemGesto.Id, Tempo(x)) + 1}", 8, ActualHeight - 24, LarguraUtil);
        }
    }

    private void Texto(DrawingContext desenho, string valor, double x, double y, double largura)
    {
        if (largura < 2) return;
        var texto = new FormattedText(valor, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = largura, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        desenho.DrawText(texto, new(x, y));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (modelo is null || modelo.Duracao <= TempoMidia.Zero || !IsEnabled) return;
        if (!CaptureMouse()) return;
        IniciarGesto(e.GetPosition(this), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        e.Handled = true;
    }
    internal void IniciarGesto(Point ponto, bool estenderSelecao = false)
    {
        if (modelo is null || modelo.Duracao <= TempoMidia.Zero || !IsEnabled) return;
        inicioGesto = atualGesto = ponto;
        itemGesto = ItemEm(ponto);
        gestoNaRegua = ponto.Y < Regua;
        estender = estenderSelecao;
        InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (inicioGesto is null) { ToolTip = ItemEm(e.GetPosition(this)) is { } item ? $"{item.Tipo}: {item.Nome}. {item.Inicio} até {item.Inicio + item.Duracao}" : "Clique para navegar. Arraste na régua para selecionar um intervalo."; return; }
        atualGesto = e.GetPosition(this); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!EmGesto) return;
        ConcluirGesto(e.GetPosition(this)); e.Handled = true;
    }
    internal void ConcluirGesto(Point fim)
    {
        if (inicioGesto is not { } inicio || modelo is null) return;
        var arrastou = Math.Abs(fim.X - inicio.X) >= SystemParameters.MinimumHorizontalDragDistance;
        var destino = itemGesto?.Faixa == 0 && arrastou ? modelo.DestinoReordenacao(itemGesto.Id, Tempo(fim.X)) : (int?)null;
        var gesto = new GestoTimelineVisual(itemGesto?.Id, Tempo(inicio.X), gestoNaRegua && arrastou ? Tempo(fim.X) : Tempo(inicio.X), estender, destino);
        var valido = fim.Y >= 0 && fim.Y <= ActualHeight && fim.X >= 0 && fim.X <= ActualWidth;
        Cancelar();
        if (valido) Confirmado?.Invoke(gesto);
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); Cancelar(); }
}
