using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Microsoft.Win32;
using SonarFrame.Application;

namespace SonarFrame.Desktop;

/// <summary>Modal import workflow keeps the owner stable while allowing progress queries and cancellation.</summary>
public sealed class ImportDialog : Window
{
    private readonly TabControl tabs = new();
    private readonly TextBlock status = new() { Text = "Escolha uma guia e selecione os arquivos.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 8) };
    private readonly ProgressBar progressBar = new() { Minimum = 0, Maximum = 100, Height = 16, Margin = new Thickness(0, 0, 0, 12) };
    private readonly ListBox results = new() { MinHeight = 80, MaxHeight = 180 };
    private readonly Button close = new() { Content = "Fechar", IsCancel = true };
    private CancellationTokenSource? cancellation;
    private bool running;
    public bool ImportedAny { get; private set; }

    public ImportDialog(Window owner, EditorSession session, IMediaProbe probe)
    {
        Owner = owner; Title = "Adicionar conteúdo"; Width = 620; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; FontSize = 16;
        var panel = new StackPanel { Margin = new Thickness(24) };
        foreach (var (name, description, filter) in new[]
        {
            ("Fotos e vídeos", "Selecione fotos JPG/PNG ou vídeos MP4/MOV. Você pode selecionar vários arquivos.", "Fotos e vídeos|*.jpg;*.jpeg;*.png;*.mp4;*.mov"),
            ("Áudios e músicas", "Selecione arquivos MP3, WAV, M4A ou OGG, incluindo áudio Opus.", "Áudios e músicas|*.mp3;*.wav;*.m4a;*.ogg"),
            ("Legendas e efeitos", "A importação de legendas e a aplicação de efeitos estarão disponíveis em outra etapa.", "")
        })
        {
            var content = new StackPanel { Margin = new Thickness(12) };
            content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
            if (filter.Length > 0)
            {
                var choose = new Button { Content = "Selecionar _arquivos…", Margin = new Thickness(0, 12, 0, 0) };
                choose.Click += async (_, _) =>
                {
                    var dialog = new OpenFileDialog { Multiselect = true, Filter = filter, Title = name, CheckFileExists = true };
                    if (dialog.ShowDialog(this) != true) { choose.Focus(); return; }
                    running = true; tabs.IsEnabled = false; close.Content = "Cancelar importação";
                    cancellation = new CancellationTokenSource();
                    close.Focus();
                    results.Items.Clear();
                    Report("Importação iniciada. Os arquivos serão adicionados somente à biblioteca.");
                    var progress = new Progress<ImportProgress>(p =>
                    {
                        if (!running) return;
                        progressBar.Value = p.Total == 0 ? 0 : p.Completed * 100.0 / p.Total;
                        // Consultable progress, without live announcements on every file.
                        AutomationProperties.SetName(progressBar, $"Importação: {p.Completed} de {p.Total} arquivos");
                        status.Text = $"{p.Completed} de {p.Total} arquivos. {p.CurrentFile}";
                    });
                    try
                    {
                        var result = await MediaImport.ImportAsync(session, probe, dialog.FileNames, cancellation.Token, progress);
                        ImportedAny |= result.ImportedIds.Count > 0;
                        foreach (var failure in result.Failures) results.Items.Add($"{System.IO.Path.GetFileName(failure.Path)}: {failure.Message}");
                        foreach (var id in result.ImportedIds) results.Items.Add($"Importado: {session.Current.Library.Single(a => a.Id == id).Name}");
                        if (!result.IsCanceled) progressBar.Value = 100;
                        Report($"{(result.IsCanceled ? "Importação cancelada" : "Importação concluída")}. {result.ImportedIds.Count} arquivo(s) importado(s), {result.Failures.Count} falha(s). Os itens importados permanecem na biblioteca.");
                    }
                    catch (Exception ex) { Report("Não foi possível concluir a importação: " + ex.Message); }
                    finally
                    {
                        running = false; cancellation.Dispose(); cancellation = null;
                        tabs.IsEnabled = true; close.Content = "Fechar";
                    }
                };
                content.Children.Add(choose);
            }
            tabs.Items.Add(new TabItem { Header = name, Content = content });
        }
        panel.Children.Add(tabs); panel.Children.Add(status); panel.Children.Add(progressBar);
        panel.Children.Add(new TextBlock { Text = "Resultados da importação", Margin = new Thickness(0, 4, 0, 6) });
        AutomationProperties.SetName(results, "Resultados da importação");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(progressBar, "Progresso da importação");
        panel.Children.Add(results); panel.Children.Add(close); Content = panel;
        close.Click += (_, _) => { if (!running) Close(); };
        Closing += (_, e) =>
        {
            if (!running) return;
            e.Cancel = true; cancellation?.Cancel(); Report("Cancelando. Os arquivos já importados serão preservados.");
        };
        Loaded += (_, _) => ((TabItem)tabs.Items[0]).Focus();
    }
    private void Report(string message)
    {
        status.Text = message;
        var peer = UIElementAutomationPeer.FromElement(status) ?? UIElementAutomationPeer.CreatePeerForElement(status);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
