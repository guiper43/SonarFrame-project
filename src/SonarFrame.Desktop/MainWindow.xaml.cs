using System.ComponentModel;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;

namespace SonarFrame.Desktop;

public partial class MainWindow : Window
{
    private EditorSession? session;
    private string? projectPath;
    private bool refreshing;
    private bool saving;
    private readonly SqliteProjectStore store = new();
    private readonly List<string> recentMessages = [];
    private sealed record Row(Guid Id, string Label);
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => NewButton.Focus();
    }
    private FrameworkElement[] Regions => [LibraryRegion, PropertiesRegion, TimelineRegion, PreviewRegion, CommandsRegion];
    private Control[] Destinations => [AddButton, Properties, TimeNavigator, PreviewButton, SaveButton];
    private void Announce(string text)
    {
        Status.Text = text;
        recentMessages.Insert(0, text);
        if (recentMessages.Count > 100) recentMessages.RemoveAt(100);
        Messages.ItemsSource = recentMessages.ToArray();
        var peer = UIElementAutomationPeer.FromElement(Status) ?? UIElementAutomationPeer.CreatePeerForElement(Status);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void ShowProject(Project project, bool saved = false, string? path = null)
    {
        session = new EditorSession(project, saved);
        projectPath = path;
        Home.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        recentMessages.Clear();
        Properties.Text = "Selecione um conteúdo na biblioteca ou na linha do tempo.";
        Refresh();
        AddButton.Focus();
        Announce($"Projeto {project.Name} aberto. Biblioteca de mídias. Use F1 para consultar os atalhos.");
    }
    private void Refresh()
    {
        if (session is null) return;
        refreshing = true;
        var p = session.Current;
        var libraryIds = Library.SelectedItems.Cast<Row>().Select(r => r.Id).ToHashSet();
        var libraryRows = p.Library.Select(a => new Row(a.Id, $"{a.Name} · {Kind(a.Kind)} · {a.Duration?.ToString() ?? "definir duração na inserção"}")).ToArray();
        if (!Library.Items.Cast<Row>().SequenceEqual(libraryRows))
        {
            Library.ItemsSource = libraryRows;
            foreach (var row in libraryRows.Where(r => libraryIds.Contains(r.Id))) Library.SelectedItems.Add(row);
        }
        var timelineRows = p.Timeline.Select((c, i) => new Row(c.Id, $"{i + 1}. {c.Name} · {Kind(p.Library.Single(a => a.Id == c.AssetId).Kind)} · {c.Duration}")).ToArray();
        if (!Timeline.Items.Cast<Row>().SequenceEqual(timelineRows)) Timeline.ItemsSource = timelineRows;
        Timeline.SelectedItem = Timeline.Items.Cast<Row>().FirstOrDefault(r => r.Id == p.SelectedItemId);
        ProjectTitle.Text = p.Name;
        Title = $"{p.Name}{(session.IsDirty ? " *" : "")} — sonarFrame";
        TimeNavigator.Content = $"Posição: {p.Cursor} · Duração: {p.Duration}";
        AutomationProperties.SetName(TimeNavigator, $"Navegação temporal. Posição {p.Cursor}");
        LibraryEmpty.Visibility = p.Library.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        TimelineEmpty.Visibility = p.Timeline.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        InsertButton.IsEnabled = Library.SelectedItems.Count > 0;
        UndoButton.IsEnabled = session.CanUndo; RedoButton.IsEnabled = session.CanRedo;
        refreshing = false;
    }
    private static string Kind(MediaKind kind) => kind switch { MediaKind.Video => "Vídeo", MediaKind.Audio => "Áudio", _ => "Imagem" };
    private bool CanLeave()
    {
        if (saving) { Announce("Aguarde a conclusão do salvamento."); return false; }
        return session is null || !session.IsDirty || MessageBox.Show(this, "Há alterações não salvas. Sair deste projeto e descartá-las? Para salvar antes, escolha Não e pressione Control S.", "Alterações não salvas", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }
    private void NewProject(object sender, RoutedEventArgs e)
    {
        if (!CanLeave()) return;
        var input = new TextBox { Text = "Meu projeto", Margin = new Thickness(0, 6, 0, 16), MinWidth = 320 };
        AutomationProperties.SetName(input, "Nome do projeto");
        var panel = new StackPanel();
        panel.Children.Add(new Label { Content = "_Nome do projeto", Target = input }); panel.Children.Add(input);
        var dialog = CreateDialog("Novo projeto", panel);
        var create = new Button { Content = "Criar projeto", IsDefault = true };
        create.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true; else { input.Focus(); MessageBox.Show(dialog, "Informe um nome para o projeto."); } };
        panel.Children.Add(create); AddClose(dialog, panel, "Cancelar");
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        if (dialog.ShowDialog() == true) ShowProject(Project.Create(input.Text.Trim()));
    }
    private void Demo(object sender, RoutedEventArgs e)
    {
        if (!CanLeave()) return;
        var demo = new EditorSession(Project.Create("Explorar o sonarFrame — demonstração"));
        foreach (var name in new[] { "Apresentação", "Cena principal", "Encerramento" })
        {
            var asset = new MediaAsset(Guid.NewGuid(), name + " (exemplo)", name + ".mp4", MediaKind.Video, MediaTime.Seconds(10), new FrameRate(30, 1));
            demo.Execute(new RegisterMedia(asset)); demo.Execute(new InsertClip(asset.Id, demo.Current.Timeline.Length));
        }
        ShowProject(demo.Current, true);
    }
    private async void OpenProject(object sender, RoutedEventArgs e)
    {
        if (!CanLeave()) return;
        var dialog = new OpenFileDialog { Filter = "Projeto sonarFrame|*.sonarframe|Todos os arquivos|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try { ShowProject(await store.LoadAsync(dialog.FileName), true, dialog.FileName); }
        catch (Exception ex) { Announce("Não foi possível abrir: " + ex.Message); }
    }
    private void MyProjects(object sender, RoutedEventArgs e) => OpenProject(sender, e);
    private async void SaveProject(object sender, RoutedEventArgs e)
    {
        if (session is null) { Announce("Crie ou abra um projeto primeiro."); return; }
        if (saving) return;
        var target = projectPath;
        if (target is null)
        {
            var dialog = new SaveFileDialog { Filter = "Projeto sonarFrame|*.sonarframe", DefaultExt = ".sonarframe", FileName = "Meu projeto" };
            if (dialog.ShowDialog(this) != true) return;
            target = dialog.FileName;
        }
        saving = true;
        var activeSession = session;
        try
        {
            var snapshot = activeSession.Current;
            await store.SaveAsync(target, snapshot);
            activeSession.MarkSaved(snapshot); projectPath = target;
            Refresh(); Announce("Projeto salvo.");
        }
        catch (Exception ex) { Announce("Falha ao salvar: " + ex.Message); }
        finally { saving = false; }
    }
    private void GoHome(object sender, RoutedEventArgs e)
    {
        if (!CanLeave()) return;
        session = null; Editor.Visibility = Visibility.Collapsed; Home.Visibility = Visibility.Visible;
        Title = "sonarFrame"; NewButton.Focus(); Announce("Tela inicial.");
    }
    private void Exit(object sender, RoutedEventArgs e) => Close();
    private void WindowClosing(object? sender, CancelEventArgs e) => e.Cancel = !CanLeave();
    private bool Execute(IEditCommand command)
    {
        if (session is null) return false;
        try { session.Execute(command); Refresh(); return true; }
        catch (Exception ex) { Announce(ex.Message); return false; }
    }
    private void Insert(object sender, RoutedEventArgs e)
    {
        if (session is null || Library.SelectedItems.Count == 0) return;
        var ids = Library.Items.Cast<Row>().Where(r => Library.SelectedItems.Contains(r)).Select(r => r.Id).ToImmutableArray();
        MediaTime? duration = null;
        if (session.Current.Library.Any(a => ids.Contains(a.Id) && a.Kind == MediaKind.Image))
        {
            duration = AskImageDuration();
            if (duration is null) { InsertButton.Focus(); return; }
        }
        if (Execute(new InsertClips(ids, duration))) Announce($"{ids.Length} conteúdo(s) adicionado(s) à linha do tempo.");
    }
    private void LibrarySelected(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || session is null) return;
        InsertButton.IsEnabled = Library.SelectedItems.Count > 0;
        if (Library.SelectedItem is Row row)
        {
            var a = session.Current.Library.Single(a => a.Id == row.Id);
            Properties.Text = $"{a.Name}\nTipo: {Kind(a.Kind)}\nDuração: {a.Duration?.ToString() ?? "definida ao inserir"}\nArquivo: {a.Path}";
            if (a.VariableFrameRate) Properties.Text += "\nTaxa de quadros possivelmente variável; use navegação por segundos nesta etapa.";
        }
        else Properties.Text = "Selecione um conteúdo na biblioteca ou na linha do tempo.";
    }
    private void TimelineSelected(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || session is null || Timeline.SelectedItem is not Row row) return;
        session.SelectItem(row.Id);
        var clip = session.Current.Timeline.Single(c => c.Id == row.Id);
        Properties.Text = $"{clip.Name}\nDuração: {clip.Duration}\nVolume: {clip.Volume}%\nInício na origem: {clip.SourceStart}";
    }
    private void Move(int destination)
    {
        if (session is null || Timeline.SelectedItem is not Row row) { Announce("Selecione um conteúdo na lista da composição."); return; }
        destination = Math.Clamp(destination, 0, session.Current.Timeline.Length - 1);
        if (!Execute(new MoveClip(row.Id, destination))) return;
        Timeline.Focus();
        if (Timeline.ItemContainerGenerator.ContainerFromItem(Timeline.SelectedItem) is ListBoxItem item) item.Focus();
        Announce($"Conteúdo movido para a posição {destination + 1} de {session.Current.Timeline.Length}.");
    }
    private void MoveFirst(object sender, RoutedEventArgs e) => Move(0);
    private void MoveLast(object sender, RoutedEventArgs e) => Move((session?.Current.Timeline.Length ?? 1) - 1);
    private void MoveBefore(object sender, RoutedEventArgs e) => Move(Timeline.SelectedIndex - 1);
    private void MoveAfter(object sender, RoutedEventArgs e) => Move(Timeline.SelectedIndex + 1);
    private void Undo(object sender, RoutedEventArgs e) { session?.Undo(); Refresh(); Announce("Desfazer solicitado."); }
    private void Redo(object sender, RoutedEventArgs e) { session?.Redo(); Refresh(); Announce("Refazer solicitado."); }
    private void ReadPosition(object sender, RoutedEventArgs e) => Announce(session?.Current.Cursor.ToString() ?? "Nenhum projeto aberto.");
    private void TimeKeyDown(object sender, KeyEventArgs e)
    {
        if (session is null) return;
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        try
        {
            switch (e.Key)
            {
                case Key.Left: case Key.Right:
                    var direction = e.Key == Key.Left ? -1 : 1;
                    if (control) session.MoveBy(MediaTime.Seconds(direction), shift); else session.StepFrame(direction, shift);
                    break;
                case Key.Home: session.Seek(MediaTime.Zero, shift); break;
                case Key.End: session.Seek(session.Current.Duration, shift); break;
                case Key.PageUp: session.MoveBy(MediaTime.Seconds(-10), shift); break;
                case Key.PageDown: session.MoveBy(MediaTime.Seconds(10), shift); break;
                case Key.Space: PreviewInfo(sender, e); e.Handled = true; return;
                default: return;
            }
            Refresh(); Announce(session.Current.Cursor.ToString()); e.Handled = true;
        }
        catch (Exception ex) { e.Handled = true; Announce(ex.Message); }
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.F1) { Help(sender, e); e.Handled = true; return; }
        if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.S) { SaveProject(sender, e); e.Handled = true; return; }
        if (Editor.Visibility != Visibility.Visible) return;
        var index = -1;
        if (Keyboard.Modifiers == ModifierKeys.Alt && key >= Key.D1 && key <= Key.D5) index = key - Key.D1;
        if (key == Key.F6)
        {
            var current = Array.FindIndex(Regions, r => r.IsKeyboardFocusWithin);
            var reverse = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            index = current < 0 ? (reverse ? 4 : 0) : (current + (reverse ? 4 : 1)) % 5;
        }
        if (index >= 0) { Destinations[index].Focus(); Destinations[index].BringIntoView(); e.Handled = true; }
    }
    private Window CreateDialog(string title, UIElement content) => new()
    {
        Title = title, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        SizeToContent = SizeToContent.Height, Width = 520, ResizeMode = ResizeMode.NoResize,
        Content = new Border { Padding = new Thickness(24), Child = content }
    };
    private static void AddClose(Window dialog, Panel panel, string label = "Fechar")
    {
        var button = new Button { Content = label, IsCancel = true };
        button.Click += (_, _) => dialog.Close(); panel.Children.Add(button);
    }
    private void Information(string title, string text)
    {
        var focus = Keyboard.FocusedElement;
        var panel = new StackPanel();
        var body = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 16) };
        AutomationProperties.SetName(body, title); panel.Children.Add(body);
        var dialog = CreateDialog(title, panel); AddClose(dialog, panel);
        dialog.Loaded += (_, _) => body.Focus(); dialog.ShowDialog();
        if (focus is IInputElement element) Keyboard.Focus(element);
    }
    private void AddMedia(object sender, RoutedEventArgs e)
    {
        if (session is null) return;
        var executable = Path.Combine(AppContext.BaseDirectory, "tools", "ffprobe.exe");
        var probe = new FfprobeMediaProbe(new FfprobeRunner(File.Exists(executable) ? executable : "ffprobe"));
        var dialog = new ImportDialog(this, session, probe);
        dialog.ShowDialog(); Refresh(); AddButton.Focus();
        foreach (var message in session.Messages.Where(m => m.Code == "import.failed"))
            if (!recentMessages.Contains(message.Message)) recentMessages.Insert(0, message.Message);
        if (dialog.ImportedAny) Announce("Mídias adicionadas à biblioteca. Pressione Tab para selecionar os arquivos e depois escolha Adicionar à linha do tempo.");
        else Announce("Diálogo de importação fechado.");
    }
    private MediaTime? AskImageDuration()
    {
        MediaTime? result = null;
        var panel = new StackPanel();
        var input = new TextBox { Text = "5", Margin = new Thickness(0, 8, 0, 12) };
        panel.Children.Add(new Label { Content = "_Duração de cada foto, em segundos", Target = input });
        panel.Children.Add(input);
        AutomationProperties.SetName(input, "Duração de cada foto em segundos");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        panel.Children.Add(error);
        var dialog = CreateDialog("Duração das fotos", panel);
        var confirm = new Button { Content = "Adicionar à composição", IsDefault = true };
        confirm.Click += (_, _) =>
        {
            if (decimal.TryParse(input.Text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.CurrentCulture, out var value) && value >= 0.001m && value <= 86400)
            { result = new MediaTime((long)decimal.Round(value * 1000), 1000); dialog.DialogResult = true; }
            else
            {
                error.Text = "Informe uma duração entre 0,001 e 86400 segundos.";
                (UIElementAutomationPeer.FromElement(error) ?? UIElementAutomationPeer.CreatePeerForElement(error))?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                input.Focus(); input.SelectAll();
            }
        };
        panel.Children.Add(confirm); AddClose(dialog, panel, "Cancelar");
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        dialog.ShowDialog(); return result;
    }
    private void Settings(object sender, RoutedEventArgs e) => Information("Configurações", "A interface usa as cores do Windows e controles nativos.\n\nConfigurações de pasta, reprodução e inteligência artificial ainda não estão disponíveis. Nenhum serviço de IA está conectado.");
    private void PreviewInfo(object sender, RoutedEventArgs e) => Announce("A prévia de áudio e vídeo ainda não está disponível nesta versão.");
    private void ExportInfo(object sender, RoutedEventArgs e) => Announce("A exportação de vídeo ainda não está disponível. Você já pode salvar o projeto.");
    private void Help(object sender, RoutedEventArgs e) => Information("Atalhos de teclado", "Tab e Shift+Tab: próximo e anterior.\nAlt+1: biblioteca. Alt+2: propriedades.\nAlt+3: navegação temporal. Alt+4: prévia.\nAlt+5: comandos do projeto.\nF6 e Shift+F6: alternar entre as cinco regiões.\nCtrl+S: salvar. Escape: fechar diálogos e menus.\n\nNo controle Navegação temporal:\nSetas: um quadro. Ctrl+setas: um segundo.\nPage Up/Down: dez segundos. Home/End: extremos.\nShift com esses atalhos: selecionar intervalo.\n\nNa lista de conteúdos: setas selecionam itens.\nShift+F10: menu de reordenação.\nNa biblioteca: Ctrl e Shift permitem seleção múltipla.\nNas guias de Adicionar: Ctrl+Tab alterna guias.\nEscape durante a importação cancela os arquivos restantes.\n\nReprodução e exportação ainda não estão disponíveis.");
}
