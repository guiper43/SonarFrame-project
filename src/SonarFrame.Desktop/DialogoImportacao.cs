using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Microsoft.Win32;
using SonarFrame.Application;

namespace SonarFrame.Desktop;

public sealed class DialogoImportacao : Window
{
    private readonly TabControl guias = new();
    private readonly TextBlock situacao = new() { Text = "Escolha uma guia e selecione os arquivos.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 8) };
    private readonly ProgressBar barraProgresso = new() { Minimum = 0, Maximum = 100, Height = 16, Margin = new Thickness(0, 0, 0, 12) };
    private readonly ListBox resultados = new() { MinHeight = 80, MaxHeight = 180 };
    private readonly Button fechar = new() { Content = "Fechar", IsCancel = true };
    private CancellationTokenSource? cancelamento;
    private bool executando;
    public bool ImportouAlgumaMidia { get; private set; }

    public DialogoImportacao(Window proprietario, SessaoEdicao sessao, ILeitorMetadadosMidia leitorMetadados)
    {
        Owner = proprietario; Title = "Adicionar conteúdo"; Width = 620; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; FontSize = 16;
        var painel = new StackPanel { Margin = new Thickness(24) };
        foreach (var (nome, descricao, filtro) in new[]
        {
            ("Fotos e vídeos", "Selecione fotos JPG/PNG ou vídeos MP4/MOV. Você pode selecionar vários arquivos.", "Fotos e vídeos|*.jpg;*.jpeg;*.png;*.mp4;*.mov"),
            ("Áudios e músicas", "Selecione arquivos MP3, WAV, M4A ou OGG, incluindo áudio Opus.", "Áudios e músicas|*.mp3;*.wav;*.m4a;*.ogg"),
            ("Legendas e efeitos", "A importação de legendas e a aplicação de efeitos estarão disponíveis em outra etapa.", "")
        })
        {
            var conteudo = new StackPanel { Margin = new Thickness(12) };
            conteudo.Children.Add(new TextBlock { Text = descricao, TextWrapping = TextWrapping.Wrap });
            if (filtro.Length > 0)
            {
                var escolher = new Button { Content = "Selecionar _arquivos…", Margin = new Thickness(0, 12, 0, 0) };
                escolher.Click += async (_, _) =>
                {
                    var dialogo = new OpenFileDialog { Multiselect = true, Filter = filtro, Title = nome, CheckFileExists = true };
                    if (dialogo.ShowDialog(this) != true) { escolher.Focus(); return; }
                    executando = true; guias.IsEnabled = false; fechar.Content = "Cancelar importação";
                    cancelamento = new CancellationTokenSource();
                    fechar.Focus();
                    resultados.Items.Clear();
                    Informar("Importação iniciada. Os arquivos serão adicionados somente à biblioteca.");
                    var progresso = new Progress<ProgressoImportacao>(p =>
                    {
                        if (!executando) return;
                        barraProgresso.Value = p.Total == 0 ? 0 : p.Concluidos * 100.0 / p.Total;

                        AutomationProperties.SetName(barraProgresso, $"Importação: {p.Concluidos} de {p.Total} arquivos");
                        situacao.Text = $"{p.Concluidos} de {p.Total} arquivos. {p.ArquivoAtual}";
                    });
                    try
                    {
                        var resultado = await ImportacaoMidias.ImportarAsync(sessao, leitorMetadados, dialogo.FileNames, cancelamento.Token, progresso);
                        ImportouAlgumaMidia |= resultado.MidiasImportadasIds.Count > 0;
                        foreach (var falha in resultado.Falhas) resultados.Items.Add($"{System.IO.Path.GetFileName(falha.Caminho)}: {falha.Mensagem}");
                        foreach (var id in resultado.MidiasImportadasIds) resultados.Items.Add($"Importado: {sessao.Atual.Biblioteca.Single(a => a.Id == id).Nome}");
                        if (!resultado.FoiCancelado) barraProgresso.Value = 100;
                        Informar($"{(resultado.FoiCancelado ? "Importação cancelada" : "Importação concluída")}. {resultado.MidiasImportadasIds.Count} arquivo(s) importado(s), {resultado.Falhas.Count} falha(s). Os itens importados permanecem na biblioteca.");
                    }
                    catch (Exception ex) { Informar("Não foi possível concluir a importação: " + ex.Message); }
                    finally
                    {
                        executando = false; cancelamento.Dispose(); cancelamento = null;
                        guias.IsEnabled = true; fechar.Content = "Fechar";
                    }
                };
                conteudo.Children.Add(escolher);
            }
            guias.Items.Add(new TabItem { Header = nome, Content = conteudo });
        }
        painel.Children.Add(guias); painel.Children.Add(situacao); painel.Children.Add(barraProgresso);
        painel.Children.Add(new TextBlock { Text = "Resultados da importação", Margin = new Thickness(0, 4, 0, 6) });
        AutomationProperties.SetName(resultados, "Resultados da importação");
        AutomationProperties.SetLiveSetting(situacao, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(barraProgresso, "Progresso da importação");
        painel.Children.Add(resultados); painel.Children.Add(fechar); Content = painel;
        fechar.Click += (_, _) => { if (!executando) Close(); };
        Closing += (_, e) =>
        {
            if (!executando) return;
            e.Cancel = true; cancelamento?.Cancel(); Informar("Cancelando. Os arquivos já importados serão preservados.");
        };
        Loaded += (_, _) => ((TabItem)guias.Items[0]).Focus();
    }
    private void Informar(string mensagem)
    {
        situacao.Text = mensagem;
        var peer = UIElementAutomationPeer.FromElement(situacao) ?? UIElementAutomationPeer.CreatePeerForElement(situacao);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
