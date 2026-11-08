using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Immutable;
using SonarFrame.Core;
using SonarFrame.Application;

namespace SonarFrame.Desktop;

public partial class JanelaPrincipal
{
    private bool contextoTemporalTimeline;
    private bool focarRemocaoTimeline;

    private void AbrirMenuTimeline(bool remover = false)
    {
        focarRemocaoTimeline = remover;
        MenuTimeline.PlacementTarget = LinhaDoTempo;
        MenuTimeline.IsOpen = true;
    }

    private void MenuTimelineAberto(object sender, RoutedEventArgs e)
    {
        Atualizar();
        var item = sessao is null ? null : new AcoesEdicao(sessao).Conteudos().SingleOrDefault(c => c.Id == sessao.Atual.ItemSelecionadoId);
        MenuRemoverComposicao.Header = item?.EhCamada == true ? "Remover camada da composição" : item is null ? "Remover conteúdo da composição"
            : $"Remover {Tipo(sessao!.Atual.Biblioteca.Single(m => m.Id == item.MidiaId).Tipo).ToLowerInvariant()} da composição";
        var destino = focarRemocaoTimeline && MenuRemoverComposicao.IsEnabled ? MenuRemoverComposicao : MenuConsultarTempo;
        focarRemocaoTimeline = false;
        destino.Focus();
    }

    private void MenuTimelineFechado(object sender, RoutedEventArgs e) => FocarConteudoSelecionado();

    private void TeclaMenuTimeline(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        MenuTimeline.IsOpen = false;
        e.Handled = true;
    }

    private void SelecionarAlvoMenuTimeline(object sender, MouseButtonEventArgs e)
    {
        contextoTemporalTimeline = false;
        if (e.OriginalSource is DependencyObject origem && ItemsControl.ContainerFromElement(LinhaDoTempo, origem) is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }
    internal Task UltimaRemocaoBiblioteca { get; private set; } = Task.CompletedTask;

    private async Task RemoverSelecionadasDaBibliotecaAsync()
    {
        if (sessao is null || Ocupado) return;
        var ids = Biblioteca.SelectedItems.Cast<Linha>().Select(l => l.Id).ToImmutableArray();
        if (ids.IsEmpty) { Anunciar("Selecione uma mídia para remover da biblioteca."); return; }
        var indice = Biblioteca.SelectedIndex;
        var comando = new RemoverMidias(ids);
        try
        {
            comando.Aplicar(sessao.Atual);
            if (!await PrepararEdicaoAsync()) return;
            emTransicao = true; Estrutura.IsEnabled = false;
            try
            {
                if (previa.Atual.Midia is { } midia && ids.Contains(midia.Id)) await LimparPreviaAsync();
                sessao.Executar(comando);
            }
            finally { emTransicao = false; Estrutura.IsEnabled = true; }
            Atualizar();
            if (Biblioteca.Items.Count > 0) Biblioteca.SelectedIndex = Math.Min(indice, Biblioteca.Items.Count - 1);
            Biblioteca.Focus();
            if (Biblioteca.SelectedItem is { } selecionado)
            {
                Biblioteca.ScrollIntoView(selecionado); Biblioteca.UpdateLayout();
                if (Biblioteca.ItemContainerGenerator.ContainerFromItem(selecionado) is ListBoxItem item) item.Focus();
            }
            Anunciar($"{ids.Length} mídia(s) removida(s) da biblioteca. Os arquivos originais foram preservados. Control Z desfaz.");
        }
        catch (Exception ex) { Anunciar(ex.Message); }
    }

    private static IEnumerable<Control> ControlesDaArea(DependencyObject elemento)
    {
        foreach (var filho in LogicalTreeHelper.GetChildren(elemento).OfType<DependencyObject>())
        {
            if (filho is Control controle && controle is Button or TextBox or ListBox)
            {
                if (controle.IsVisible && controle.IsEnabled && controle.Focusable) yield return controle;
            }
            else
                foreach (var descendente in ControlesDaArea(filho)) yield return descendente;
        }
    }

    private void TeclaAreaEditor(object sender, KeyEventArgs e)
    {
        var origem = e.OriginalSource as DependencyObject;
        var area = Regioes.FirstOrDefault(r => origem is not null && (r == origem || r.IsAncestorOf(origem)));
        if (area is null) return;
        if (area == RegiaoLinhaDoTempo && (e.Key == Key.Apps || e.Key == Key.F10 && e.KeyboardDevice.Modifiers == ModifierKeys.Shift))
        {
            e.Handled = true; AbrirMenuTimeline(); return;
        }
        if (area == RegiaoLinhaDoTempo && e.Key == Key.Enter && e.KeyboardDevice.Modifiers == ModifierKeys.None
            && origem is not null && (origem == LinhaDoTempo || LinhaDoTempo.IsAncestorOf(origem)))
        {
            e.Handled = true;
            if (sessao is not null && LinhaDoTempo.SelectedItem is Linha linha)
            {
                var item = new AcoesEdicao(sessao).Conteudos().Single(c => c.Id == linha.Id);
                UltimaNavegacaoTemporal = NavegarTemporalAsync(Key.None, ModifierKeys.None, item.Inicio);
            }
            return;
        }
        if (area == RegiaoLinhaDoTempo && e.Key == Key.Delete && e.KeyboardDevice.Modifiers == ModifierKeys.None
            && origem is not null && (origem == LinhaDoTempo || LinhaDoTempo.IsAncestorOf(origem)))
        {
            e.Handled = true;
            if (contextoTemporalTimeline && sessao?.Atual.Selecao is not null) ExcluirIntervaloSelecionado(sender, e);
            else AbrirMenuTimeline(true);
            return;
        }
        if (area == RegiaoLinhaDoTempo && e.Key == Key.Space && e.KeyboardDevice.Modifiers == ModifierKeys.None
            && origem is not null && (origem == LinhaDoTempo || LinhaDoTempo.IsAncestorOf(origem)))
        {
            e.Handled = true; AlternarComposicao(sender, e); return;
        }
        if (area == RegiaoBiblioteca && e.Key == Key.Delete && e.KeyboardDevice.Modifiers == ModifierKeys.None
            && origem is not null && (origem == Biblioteca || Biblioteca.IsAncestorOf(origem)))
        {
            e.Handled = true;
            UltimaRemocaoBiblioteca = RemoverSelecionadasDaBibliotecaAsync();
            return;
        }
        if (area == RegiaoBiblioteca && e.Key == Key.Enter && e.KeyboardDevice.Modifiers == ModifierKeys.None
            && origem is not null && (origem == Biblioteca || Biblioteca.IsAncestorOf(origem)))
        {
            e.Handled = true;
            AbrirPreviaSelecionada(sender, e);
            return;
        }
        if (e.Key == Key.Tab && e.KeyboardDevice.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            var reverso = e.KeyboardDevice.Modifiers == ModifierKeys.Shift;
            var acoes = ControlesDaArea(area).ToArray();
            var indiceAcao = Array.FindIndex(acoes, c => c == origem || (origem is not null && c.IsAncestorOf(origem)));
            var proximaAcao = indiceAcao + (reverso ? -1 : 1);
            if (indiceAcao >= 0 && proximaAcao >= 0 && proximaAcao < acoes.Length)
            {
                acoes[proximaAcao].Focus();
                acoes[proximaAcao].BringIntoView();
                e.Handled = true;
                return;
            }
            var ordem = new[] { 0, 3, 1, 2, 4 };
            var indiceArea = Array.IndexOf(ordem, Array.IndexOf(Regioes, area));
            for (var destinoArea = indiceArea + (reverso ? -1 : 1); destinoArea >= 0 && destinoArea < ordem.Length; destinoArea += reverso ? -1 : 1)
            {
                var controlesDestino = ControlesDaArea(Regioes[ordem[destinoArea]]).ToArray();
                if (controlesDestino.Length == 0) continue;
                var entradaArea = reverso
                    ? controlesDestino[^1]
                    : (Destinos[ordem[destinoArea]] is { IsEnabled: true, IsVisible: true } destinoPadrao ? destinoPadrao : controlesDestino[0]);
                entradaArea.Focus();
                entradaArea.BringIntoView();
                e.Handled = true;
                return;
            }
            if (exportacaoEmAndamento && reverso) e.Handled = true;
            return;
        }
        if (area == RegiaoLinhaDoTempo && origem is not TextBox && e.Key is Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            TeclaNavegacaoTemporal(sender, e);
            return;
        }
        if (e.Key is Key.Left or Key.Right)
        {
            if (area == RegiaoLinhaDoTempo) TeclaNavegacaoTemporal(sender, e);
            else if (origem is not TextBox) e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Up or Key.Down) || e.KeyboardDevice.Modifiers != ModifierKeys.None) return;
        if (area == RegiaoLinhaDoTempo) contextoTemporalTimeline = false;
        var controles = ControlesDaArea(area).ToArray();
        var indice = Array.FindIndex(controles, c => c == origem || (origem is not null && c.IsAncestorOf(origem)));
        if (indice < 0) return;
        var direcao = e.Key == Key.Down ? 1 : -1;
        if (controles[indice] is ListBox lista && lista.Items.Count > 0)
        {
            var selecionado = lista.SelectedIndex;
            if (selecionado < 0 || (direcao > 0 ? selecionado < lista.Items.Count - 1 : selecionado > 0))
            {
                lista.SelectedIndex = selecionado < 0 ? (direcao > 0 ? 0 : lista.Items.Count - 1) : selecionado + direcao;
                lista.ScrollIntoView(lista.SelectedItem);
                lista.UpdateLayout();
                if (lista.ItemContainerGenerator.ContainerFromItem(lista.SelectedItem) is ListBoxItem item) item.Focus();
                e.Handled = true;
                return;
            }
        }
        if (controles[indice] is ListBox) { e.Handled = true; return; }
        var destino = controles[Math.Clamp(indice + direcao, 0, controles.Length - 1)];
        destino.Focus();
        destino.BringIntoView();
        e.Handled = true;
    }
}
