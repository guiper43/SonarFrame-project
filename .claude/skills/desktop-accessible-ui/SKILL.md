---
name: desktop-accessible-ui
<<<<<<< HEAD
description: >-
  Guidance for visual, layout, control choice, sizing, and accessibility design decisions
  in sonarFrame's WPF screens (JanelaPrincipal.xaml, JanelaPrincipal.*.cs, DialogoImportacao.cs)
  for a keyboard-only desktop video editor whose users are blind and use NVDA.
---

# desktop-accessible-ui no Claude Code

Antes de executar esta skill, ler e seguir [a skill compartilhada](../../../.agents/skills/desktop-accessible-ui/SKILL.md), relativa a este arquivo. O conteúdo canônico fica em .agents/skills/desktop-accessible-ui/SKILL.md na raiz do repositório.

Aplicar suas instruções com as ferramentas disponíveis no Claude Code. Fazer alterações de conteúdo no arquivo canônico; manter aqui somente o encaminhamento e os metadados de descoberta.
=======
description: Guidance for visual/design decisions in sonarFrame's WPF screens — layout, control choice, sizing, and visual state — for a keyboard-only desktop video editor whose users are blind and use NVDA, including the developer. Use when creating or restyling a region in JanelaPrincipal.xaml, or a dialog in JanelaPrincipal.*.cs / DialogoImportacao.cs. Defaults to reusing App.xaml's implicit styles, the Windows theme (SystemColors brushes), and the CriarDialogo/AdicionarFechar dialog pattern instead of introducing new visual choices. Does not cover UI Automation/NVDA mechanics or keyboard-navigation testing procedure.
---

# Desktop Accessible UI

sonarFrame is a non-destructive video editor for Windows, operable entirely by keyboard, for blind users who rely on NVDA — and the developer working with you on it is himself blind, so he cannot look at a screen and judge whether a visual choice "worked." That makes this the opposite of a job that rewards aesthetic risk-taking. The goal is **precise, consistent execution**: reuse what `App.xaml` and the established dialog pattern already provide, and only deviate when a screen has a real, stated need they can't meet. Never invent a color palette, a type scale, a new control template, or a new way of assembling a window/dialog just because it's possible.

## Scope

This skill governs **design decisions**: which region a new control belongs to, which WPF control to reach for, sizing/spacing, and when (if ever) to depart from the Windows theme's colors. It does not cover:
- Accessibility *mechanics* already working in the code (`AutomationProperties.Name`/`HelpText`, `LiveSetting`/`RaiseAutomationEvent(LiveRegionChanged)`, explicit `.Focus()`/`Keyboard.FocusedElement` handling, `Label.Target`, `IsDefault`/`IsCancel`, the `_` mnemonic) — these are conventions already established and working throughout `JanelaPrincipal.xaml.cs`, `.Edicao.cs`, `.Projetos.cs`, and `DialogoImportacao.cs`. Treat them as a given baseline, not something to re-explain.
- Keyboard-shortcut/navigation rules — the shortcut table and region model are documented in `docs/desktop.md` and the product principles in `README.md`. Don't restate them here.
- NVDA testing procedure — out of scope for this skill.

## Use the existing design system — don't invent one

- **Color**: there is no `Colors.xaml` or custom palette. Every color in `src/SonarFrame.Desktop/App.xaml` comes from exactly three Windows theme keys: `{DynamicResource {x:Static SystemColors.WindowBrushKey}}`, `WindowTextBrushKey`, and `HighlightBrushKey`. This means the app already follows the user's Windows theme — including High Contrast — automatically, for free. Never write a literal brush or hex color in XAML or C#; reuse one of these three keys, the same way the existing styles do.
- **Typography**: `FontFamily="Segoe UI"` and `FontSize="15"` are set once, on the implicit `Window` style, and inherited by everything underneath. Don't set `FontFamily`/`FontSize`/`FontWeight` inline on a new control without a stated hierarchy need.
- **Controls**: `App.xaml`'s `Application.Resources` defines exactly six implicit styles — `Window`, `Button`, `GroupBox`, `TextBlock`, `ListBox`, `ListBoxItem` — and every control of that type already gets them. `Button` already has `Padding="16,9"`, `MinHeight="40"`, and a keyboard-focus trigger (see Sizing below). `GroupBox` already has `Padding="12"`/`Margin="6"`. The code already has one pre-existing drift worth noting but not fixing on its own: `Biblioteca`, `LinhaDoTempo`, and `Mensagens` each override an explicit `Height` (135/115/100) on top of `ListBox`'s implicit `MinHeight="80"`. Don't treat that as license to add more size overrides elsewhere without a reason.
- **There are exactly two legitimate ways to build UI here, and no third**: declarative XAML for the main window's regions (`JanelaPrincipal.xaml`), and imperative C# construction for dialogs, always through the `CriarDialogo(titulo, conteudo)` / `AdicionarFechar(dialogo, painel, rotulo)` helpers in `JanelaPrincipal.xaml.cs` (see `DialogoImportacao.cs` and the `SolicitarNumeros`/`SolicitarDuracaoImagem` dialogs in `JanelaPrincipal.Edicao.cs`/`.xaml.cs` for the pattern). A new screen or dialog has to fit one of these two molds — never a bespoke `new Window{...}` with its own hand-rolled cancel button.

## Layout and control choice

- The main window is five regions, each a `GroupBox` with `Header="N · Name"` and an `AutomationProperties.Name` like `"<Region>, região N"`. Alt+1..Alt+5 is handled imperatively in `PreviewKeyDown` (checking `ModifierKeys.Alt` and `Key.D1`..`Key.D5`), and `F6`/`Shift+F6` cycle `Regioes[]`/`Destinos[]` modulo 5. **Adding a sixth top-level region is not just another `GroupBox`** — it also means widening the `Key.D5` check and the `% 5` in the navigation handler, and updating the shortcut table in `docs/desktop.md`. Treat that as a navigation-code change, not a layout change, and see "When to ask" below.
- New dialogs always go through `CriarDialogo`/`AdicionarFechar` — never construct a `Window` or a cancel button from scratch. The established interior shape is a single `StackPanel` with `Label{Target=...}` + input pairs, an assertive-live-region `TextBlock` for validation errors placed under the relevant field, a primary-action `Button{IsDefault=true}`, and `AdicionarFechar`'s cancel button added last. Mirror `SolicitarNumeros`/`SolicitarDuracaoImagem` rather than designing a new dialog shape.
- Lists are plain `ListBox` — either bound to objects via `DisplayMemberPath="Rotulo"` (`Biblioteca`, `LinhaDoTempo`) or to raw strings (`Mensagens`). There is no grid/card control anywhere in this app; don't introduce one for a new list of items.
- Nothing in this codebase uses custom drawing, `Storyboard`, or motion — treat that as out of scope unless a task explicitly asks for it.

## Sizing

- This is a mouse-and-keyboard desktop app — there's no touch-target/dp rule to apply. The thing that actually matters here is **keyboard focus visibility**: `Button`'s implicit style has a `Trigger` on `IsKeyboardFocused` that thickens its border to `BorderThickness="3"` using `SystemColors.HighlightBrushKey`. Any new or custom focusable control needs an equivalent visible-focus treatment — never a focusable control with no focus indicator.
- Comfortable click size is already baked into the `Button` style (`MinHeight="40"`, `Padding="16,9"`) — reuse it rather than setting a smaller explicit height on a new button.
- `CriarDialogo` fixes dialog width at `Width="520"` with `SizeToContent="Height"`. Don't pick an arbitrary width for a new dialog; let height follow content and keep 520 unless there's a stated reason to deviate.
- There's no formal spacing scale. Copy margins/padding from the nearest comparable existing dialog or region (e.g. `Margin="0,8,0,12"` on dialog input fields, `Padding="24"` on `CriarDialogo`'s content border, `Padding="12"`/`Margin="6"` on `GroupBox`) instead of picking a new number.

## Color and contrast

- The core trade: not touching color at all is what gets you Windows theme and High Contrast support for free. The only way to lose that is overriding a color with a literal value — so the practical rule is simply don't.
- Never communicate state (focused, error, disabled) through color alone. The existing code already follows this: focus uses border thickness *and* color, not color alone; validation errors use text plus an assertive live-region announcement, not a color change. Follow the same pattern for new screens.
- If a screen needs to signal something the three existing `SystemColors.*` keys don't cover, that's a "ask first" situation (see below) — don't invent a new color key silently.

## Accessibility semantics — decide, don't re-derive

Treat as a given baseline (not re-explained here): `AutomationProperties.Name`/`HelpText` on every control without an obvious visual label; `AutomationProperties.LiveSetting="Polite"` for status updates (routed through the `Anunciar` helper) versus `AutomationLiveSetting.Assertive` for validation errors, each paired with a manual `(UIElementAutomationPeer.FromElement(x) ?? UIElementAutomationPeer.CreatePeerForElement(x))?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged)` call whenever the text changes after the control is already in the tree; explicit `.Focus()` after every action or dialog close, with `Keyboard.FocusedElement` saved before and restored after for informational dialogs (see `Informacoes` in `JanelaPrincipal.xaml.cs`); `Label{Target=...}` instead of a loose `TextBlock`; `IsDefault`/`IsCancel` on dialog buttons; the `_` mnemonic in `Button`/`Label` text.

This skill's job when laying out a new screen or dialog is the design decision layered on top of that machinery — which region a control belongs to, whether it needs its own `GroupBox` or fits an existing one, the order of fields in a dialog, whether a status change is `Polite` or `Assertive`. Apply those decisions using the conventions already in the code; don't re-explain the APIs themselves here. For shortcut/navigation rules — what each key does, region order, the Alt+1..5/F6 cycle — the source of truth is the table in `docs/desktop.md` and the principles in `README.md`.

## When to ask vs. when to just decide

- **Decide and proceed, without asking**: any cosmetic or structural choice with an obvious answer from the existing system — which implicit style applies, whether to use `CriarDialogo`/`AdicionarFechar` versus an existing region, which existing dialog to mirror. The developer is blind and works through NVDA, not a visual reviewer, so pausing to ask about something the code already answers wastes his time.
- **Ask first**: before adding a new top-level region (it touches the `Regioes`/`Destinos` navigation code and the `Key.D5`/`% 5` limits, not just layout); when no existing dialog or region is a close enough precedent; or when considering something genuinely new (animation, a non-`ListBox` collection, a literal color). Surface the specific choice and a recommendation — don't decide silently.

## Reference skeleton

A new dialog following `CriarDialogo`/`AdicionarFechar`/`Label.Target`/`AutomationProperties`/focus/`IsDefault`+`IsCancel` exactly as the existing ones do. Identifiers and UI strings stay in Portuguese, matching the rest of the codebase, even though this skill's prose is in English:

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

// Call site, mirroring AdicionarMidia's focus-return + announcement:
var nome = SolicitarNomeDeExemplo();
BotaoQueAbriuODialogo.Focus();
if (nome is not null) Anunciar($"Item \"{nome}\" criado.");
```

Nothing here is a literal color, font, or size: `Button`/`Label`/`TextBlock`/`TextBox` all inherit from `App.xaml`; the dialog's width/padding come from `CriarDialogo`; the cancel button comes from `AdicionarFechar`; focus returns to the control that opened the dialog.

## Restraint checklist

Before finishing a screen or dialog:
- Did you use `CriarDialogo`/`AdicionarFechar` instead of a hand-built `Window`/cancel button?
- Did you avoid every literal color, font, and size, relying on `App.xaml`'s implicit styles and the existing `SystemColors.*` keys?
- Does every control without an obvious visual label have `AutomationProperties.Name`/`HelpText`, and does every dynamic text change go through the `Polite`/`Assertive` + `RaiseAutomationEvent(LiveRegionChanged)` pair?
- Is focus explicitly set after every action/dialog close (and restored for informational dialogs), following the existing `.Focus()`/`Keyboard.FocusedElement` pattern?
- If this introduces a new top-level region: were `Regioes`/`Destinos` and the `Key.D5`/`% 5` navigation limits updated, and does `docs/desktop.md`'s shortcut table need an update?
- Is any state communicated by color alone?
>>>>>>> origin/develop
