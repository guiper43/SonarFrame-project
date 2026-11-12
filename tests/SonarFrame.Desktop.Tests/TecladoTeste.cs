using System.Windows;
using System.Windows.Input;

namespace SonarFrame.Desktop.Tests;

internal sealed class TecladoTeste(ModifierKeys modificadores = ModifierKeys.None) : KeyboardDevice(InputManager.Current)
{
    protected override KeyStates GetKeyStatesFromSystem(Key key) => key switch
    {
        Key.LeftCtrl when modificadores.HasFlag(ModifierKeys.Control) => KeyStates.Down,
        Key.LeftShift when modificadores.HasFlag(ModifierKeys.Shift) => KeyStates.Down,
        Key.LeftAlt when modificadores.HasFlag(ModifierKeys.Alt) => KeyStates.Down,
        Key.LWin when modificadores.HasFlag(ModifierKeys.Windows) => KeyStates.Down,
        _ => KeyStates.None
    };

    internal static bool Pressionar(UIElement origem, Key tecla, ModifierKeys modificadores = ModifierKeys.None)
    {
        var evento = new KeyEventArgs(new TecladoTeste(modificadores), PresentationSource.FromVisual(origem)!, Environment.TickCount, tecla)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        origem.RaiseEvent(evento);
        return evento.Handled;
    }
}
