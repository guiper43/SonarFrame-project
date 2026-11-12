using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SonarFrame.Desktop;

public sealed class MenuComAberturaExplicita : Menu
{
    private static readonly RoutedEvent SelecaoAlterada = EventManager.GetRoutedEventsForOwner(typeof(MenuBase)).Single(e => e.Name == "IsSelectedChanged");

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers == ModifierKeys.None && e.OriginalSource is MenuItem item)
        {
            var raiz = item;
            while (ItemsControlFromItemContainer(raiz) is MenuItem pai) raiz = pai;
            if (ItemsControlFromItemContainer(raiz) == this)
            {
                var cabecalho = ReferenceEquals(item, raiz);
                if (cabecalho && e.Key is Key.Enter or Key.Space)
                {
                    var abrir = !raiz.IsSubmenuOpen;
                    foreach (var menu in Items.OfType<MenuItem>()) Selector.SetIsSelected(menu, false);
                    RoutedPropertyChangedEventHandler<bool> bloquear = (_, args) => args.Handled = true;
                    if (abrir) raiz.AddHandler(SelecaoAlterada, bloquear);
                    try { raiz.SetCurrentValue(MenuItem.IsSubmenuOpenProperty, abrir); }
                    finally { if (abrir) raiz.RemoveHandler(SelecaoAlterada, bloquear); }
                    e.Handled = true;
                }
                else if (e.Key is Key.Left or Key.Right)
                {
                    var menus = Items.OfType<MenuItem>().Where(m => m.IsEnabled && m.IsVisible).ToArray();
                    var indice = Array.IndexOf(menus, raiz);
                    if (indice >= 0)
                    {
                        foreach (var menu in menus) menu.SetCurrentValue(MenuItem.IsSubmenuOpenProperty, false);
                        menus[(indice + (e.Key == Key.Right ? 1 : menus.Length - 1)) % menus.Length].Focus();
                        foreach (var menu in menus) menu.SetCurrentValue(MenuItem.IsSubmenuOpenProperty, false);
                    }
                    e.Handled = true;
                }
                else if (cabecalho && !raiz.IsSubmenuOpen && e.Key is Key.Up or Key.Down)
                    e.Handled = true;
            }
        }
        base.OnPreviewKeyDown(e);
    }
}
