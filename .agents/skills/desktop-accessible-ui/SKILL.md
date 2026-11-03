---
name: desktop-accessible-ui
description: >-
  Guidance for visual, layout, control choice, sizing, and accessibility design decisions
  in sonarFrame's WPF screens (JanelaPrincipal.xaml, JanelaPrincipal.*.cs, DialogoImportacao.cs)
  for a keyboard-only desktop video editor whose users are blind and use NVDA.
---

# Desktop Accessible UI

sonarFrame is a non-destructive video editor for Windows, operable entirely by keyboard, for blind users who rely on NVDA — and the developer working with you on it is himself blind. The goal is **precise, consistent execution**: reuse what `App.xaml` and the established dialog pattern already provide, and only deviate when a screen has a real, stated need they cannot meet. Never invent a color palette, a type scale, a new control template, or a new way of assembling a window or dialog.

## Scope

This skill governs **design decisions**: which region a new control belongs to, which WPF control to reach for, sizing/spacing, and when (if ever) to depart from the Windows theme's colors.
- Accessibility mechanics already working in the code (`AutomationProperties.Name`/`HelpText`, `LiveSetting`/`RaiseAutomationEvent(LiveRegionChanged)`, explicit `.Focus()`/`Keyboard.FocusedElement` handling, `Label.Target`, `IsDefault`/`IsCancel`, the `_` mnemonic) are a given baseline established across `JanelaPrincipal.*.cs` and `DialogoImportacao.cs`.
- Keyboard-shortcut and navigation rules are documented in `docs/desktop.md` and `README.md`.

## Use the existing design system — don't invent one

- **Color**: there is no `Colors.xaml` or custom palette. Every color in `src/SonarFrame.Desktop/App.xaml` comes from three Windows theme keys: `{DynamicResource {x:Static SystemColors.WindowBrushKey}}`, `WindowTextBrushKey`, and `HighlightBrushKey`. This ensures automatic Windows theme and High Contrast support. Never write a literal brush or hex color in XAML or C#.
- **Typography**: `FontFamily="Segoe UI"` and `FontSize="15"` are set once on the implicit `Window` style and inherited underneath. Don't set `FontFamily`/`FontSize`/`FontWeight` inline without a stated hierarchy need.
- **Controls**: `App.xaml` defines implicit styles for `Window`, `Button`, `GroupBox`, `TextBlock`, `ListBox`, and `ListBoxItem`. `Button` already has `Padding="16,9"`, `MinHeight="40"`, and a keyboard-focus trigger. `GroupBox` already has `Padding="12"`/`Margin="6"`.
- **Two ways to build UI**:
  1. Declarative XAML for the main window's regions (`JanelaPrincipal.xaml`).
  2. Imperative C# construction for dialogs, always through `CriarDialogo(titulo, conteudo)` and `AdicionarFechar(dialogo, painel, rotulo)` in `JanelaPrincipal.xaml.cs`.

## Layout and control choice

- The main window has five regions, each a `GroupBox` with `Header="N · Nome"` and `AutomationProperties.Name="<Região>, região N"`. `Alt+1..Alt+5` and `F6`/`Shift+F6` cycle `Regioes[]`/`Destinos[]` modulo 5. Adding a sixth top-level region requires updating `Regioes`, `Destinos`, `Key.D5`, `% 5`, and `docs/desktop.md` — always ask first before adding a new top-level region.
- New dialogs always go through `CriarDialogo`/`AdicionarFechar`: a `StackPanel` with `Label{Target=...}` + input pairs, an assertive-live-region `TextBlock` for validation errors, a primary `Button{IsDefault=true}`, and `AdicionarFechar`'s cancel button last.
- Lists are plain `ListBox` (`Biblioteca`, `LinhaDoTempo`, `Mensagens`). Do not introduce custom grid/card controls or animations.

## Sizing and focus visibility

- `Button`'s implicit style has a `Trigger` on `IsKeyboardFocused` that thickens its border to `BorderThickness="3"` using `SystemColors.HighlightBrushKey`. Any custom focusable control needs an equivalent visible-focus indicator.
- `CriarDialogo` fixes dialog width at `Width="520"` with `SizeToContent="Height"`.
- Copy margins/padding from the nearest comparable existing dialog or region (e.g. `Margin="0,8,0,12"` on dialog inputs, `Padding="24"` on `CriarDialogo`'s content border).

## Reference skeleton (zero comments, Portuguese domain identifiers)

```csharp
private string? SolicitarNomeDeExemplo()
{
    string? resultado = null;
    var painel = new StackPanel();
    var entrada = new TextBox { Margin = new Thickness(0, 8, 0, 12) };
    painel.Children.Add(new Label { Content = "_Nome do item", Target = entrada });
    painel.Children.Add(entrada);
    AutomationProperties.SetName(entrada, "Nome do item");
    var erro = new TextBlock { TextWrapping = TextWrapping.Wrap };
    AutomationProperties.SetLiveSetting(erro, AutomationLiveSetting.Assertive);
    painel.Children.Add(erro);
    var dialogo = CriarDialogo("Novo item", painel);
    var confirmar = new Button { Content = "Confirmar", IsDefault = true };
    confirmar.Click += (_, _) =>
    {
        if (!string.IsNullOrWhiteSpace(entrada.Text)) { resultado = entrada.Text; dialogo.DialogResult = true; }
        else
        {
            erro.Text = "Informe um nome.";
            (UIElementAutomationPeer.FromElement(erro) ?? UIElementAutomationPeer.CreatePeerForElement(erro))
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            entrada.Focus(); entrada.SelectAll();
        }
    };
    painel.Children.Add(confirmar);
    AdicionarFechar(dialogo, painel, "Cancelar");
    dialogo.Loaded += (_, _) => { entrada.Focus(); entrada.SelectAll(); };
    dialogo.ShowDialog();
    return resultado;
}
```

## Restraint checklist

Before finishing a screen or dialog:
- Used `CriarDialogo`/`AdicionarFechar` instead of a hand-built `Window`/cancel button?
- Avoided every literal color, font, and size, relying on `App.xaml` and `SystemColors.*`?
- Every control without an obvious label has `AutomationProperties.Name`/`HelpText`, and dynamic text changes go through `Polite`/`Assertive` + `RaiseAutomationEvent(LiveRegionChanged)`?
- Focus is explicitly set after every action/dialog close?
- Zero code comments added?
