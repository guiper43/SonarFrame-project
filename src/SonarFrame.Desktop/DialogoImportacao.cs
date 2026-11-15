using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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
    private bool conteudoExibido;
    private bool focoInicialAplicado;
    public bool ImportouAlgumaMidia { get; private set; }
    public bool ImportouLegendas { get; private set; }
    public Func<int, bool>? ConfirmarSubstituicaoLegendasOverride { get; set; }

    public DialogoImportacao(Window proprietario, SessaoEdicao sessao, ILeitorMetadadosMidia leitorMetadados)
    {
        Owner = proprietario; Title = "Adicionar conteúdo"; Width = 620; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; FontSize = 16;
        var painel = new StackPanel { Margin = new Thickness(24) };
        painel.Children.Add(new TextBlock { Text = "Selecione o tipo da importação", Margin = new Thickness(0, 0, 0, 8) });
        AutomationProperties.SetName(guias, "Selecione o tipo da importação");
        AutomationProperties.SetHelpText(guias, "Use as setas para selecionar o tipo. Tab acessa Selecionar arquivos.");
        guias.PreviewKeyDown += (_, e) =>
        {
            if (e.OriginalSource is not TabItem || e.KeyboardDevice.Modifiers != ModifierKeys.None) return;
            if (e.Key is Key.Up or Key.Left or Key.Down or Key.Right)
            {
                var direcao = e.Key is Key.Up or Key.Left ? -1 : 1;
                guias.SelectedIndex = (guias.SelectedIndex + direcao + guias.Items.Count) % guias.Items.Count;
                FocarGuiaSelecionada();
                e.Handled = true;
            }
            else if (e.Key == Key.Tab)
            {
                guias.UpdateLayout();
                var conteudo = (StackPanel)((TabItem)guias.SelectedItem).Content;
                (conteudo.Children.OfType<Button>().FirstOrDefault() ?? fechar).Focus();
                e.Handled = true;
            }
        };
        foreach (var (nome, descricao, filtro, ehLegenda) in new[]
        {
            ("Fotos e vídeos", "Selecione fotos JPG/PNG ou vídeos MP4/MOV. Você pode selecionar vários arquivos.", "Fotos e vídeos|*.jpg;*.jpeg;*.png;*.mp4;*.mov", false),
            ("Áudios e músicas", "Selecione arquivos MP3, WAV, M4A ou OGG, incluindo áudio Opus.", "Áudios e músicas|*.mp3;*.wav;*.m4a;*.ogg", false),
            ("Legendas e efeitos", "Selecione um arquivo de legenda externa no formato SRT (.srt). Funciona sem chave de API.", "Legendas SRT|*.srt", true)
        })
        {
            var conteudo = new StackPanel { Margin = new Thickness(12) };
            conteudo.Children.Add(new TextBlock { Text = descricao, TextWrapping = TextWrapping.Wrap });
            if (filtro.Length > 0)
            {
                var escolher = new Button { Content = "Selecionar _arquivos…", Margin = new Thickness(0, 12, 0, 0) };
                escolher.Click += async (_, _) =>
                {
                    var dialogo = new OpenFileDialog { Multiselect = !ehLegenda, Filter = filtro, Title = nome, CheckFileExists = true };
                    if (dialogo.ShowDialog(this) != true) { escolher.Focus(); return; }
                    if (ehLegenda) await ImportarLegendaSrtAsync(sessao, dialogo.FileName);
                    else await ImportarArquivosAsync(sessao, leitorMetadados, dialogo.FileNames);
                };
                conteudo.Children.Add(escolher);
            }
            var guia = new TabItem { Header = nome, Content = conteudo };
            AutomationProperties.SetName(guia, $"Selecione o tipo da importação: {nome}");
            guias.Items.Add(guia);
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
        ContentRendered += (_, _) => { conteudoExibido = true; AgendarFocoInicial(); };
        Activated += (_, _) => AgendarFocoInicial();
    }
    private void AgendarFocoInicial()
    {
        if (!conteudoExibido || focoInicialAplicado) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!IsActive || focoInicialAplicado || executando) return;
            FocarGuiaSelecionada();
            focoInicialAplicado = ((TabItem)guias.SelectedItem).IsKeyboardFocused;
        }));
    }
    private void FocarGuiaSelecionada()
    {
        guias.UpdateLayout();
        var guia = (TabItem)guias.SelectedItem;
        guia.Focus();
        if (IsActive) Keyboard.Focus(guia);
    }
    internal async Task ImportarArquivosAsync(SessaoEdicao sessao, ILeitorMetadadosMidia leitorMetadados, string[] caminhos)
    {
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
            var resultado = await ImportacaoMidias.ImportarAsync(sessao, leitorMetadados, caminhos, cancelamento.Token, progresso);
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
            guias.IsEnabled = true; fechar.Content = "Fechar"; fechar.Focus();
        }
    }
    internal async Task ImportarLegendaSrtAsync(SessaoEdicao sessao, string caminho)
    {
        if (sessao.Atual.Legendas.Length > 0)
        {
            var substituir = ConfirmarSubstituicaoLegendasOverride is { } overrideConfirmacao
                ? overrideConfirmacao(sessao.Atual.Legendas.Length)
                : ConfirmarSubstituicaoLegendas(sessao.Atual.Legendas.Length);
            if (!substituir)
            {
                resultados.Items.Clear();
                resultados.Items.Add("Substituição cancelada: as legendas atuais foram mantidas.");
                Informar("Importação de legendas cancelada. As legendas atuais do projeto foram mantidas.");
                fechar.Focus();
                return;
            }
        }
        executando = true; guias.IsEnabled = false; fechar.Content = "Cancelar importação";
        cancelamento = new CancellationTokenSource();
        fechar.Focus();
        resultados.Items.Clear();
        barraProgresso.Value = 0;
        Informar("Importação de legendas SRT iniciada.");
        try
        {
            var legendas = await LeitorSrt.CarregarAsync(caminho, cancelamento.Token);
            sessao.Executar(new SonarFrame.Core.DefinirLegendas(legendas));
            ImportouLegendas = true;
            barraProgresso.Value = 100;
            var nomeArquivo = System.IO.Path.GetFileName(caminho);
            resultados.Items.Add($"Importado: {nomeArquivo} ({legendas.Length} legenda(s))");
            Informar($"Importação concluída. {legendas.Length} legenda(s) aplicada(s) na linha do tempo.");
        }
        catch (OperationCanceledException)
        {
            Informar("Importação de legendas cancelada.");
        }
        catch (Exception ex)
        {
            var nomeArquivo = System.IO.Path.GetFileName(caminho);
            resultados.Items.Add($"{nomeArquivo}: {ex.Message}");
            sessao.Notificar(new("importacao.falhou", $"{caminho}: {ex.Message}", true));
            Informar($"Não foi possível importar a legenda SRT: {ex.Message}");
        }
        finally
        {
            executando = false; cancelamento.Dispose(); cancelamento = null;
            guias.IsEnabled = true; fechar.Content = "Fechar"; fechar.Focus();
        }
    }
    internal (Window Dialogo, Button Substituir, Button Cancelar, Func<bool> Confirmou) CriarDialogoSubstituicaoLegendas(int quantidadeAtual)
    {
        var confirmado = false;
        var painel = new StackPanel();
        var mensagem = new TextBlock
        {
            Text = $"O projeto já possui {quantidadeAtual} legenda(s) aplicada(s). Deseja substituir as legendas atuais pelo novo arquivo SRT ou cancelar a operação?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        painel.Children.Add(mensagem);
        var dialogo = new Window
        {
            Title = "Substituir legendas",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height,
            Width = 520,
            ResizeMode = ResizeMode.NoResize,
            Content = new Border { Padding = new Thickness(24), Child = painel }
        };
        var substituir = new Button { Content = "_Substituir legendas", IsDefault = true };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true };
        substituir.Click += (_, _) =>
        {
            confirmado = true;
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) dialogo.DialogResult = true;
            else dialogo.Close();
        };
        cancelar.Click += (_, _) => dialogo.Close();
        painel.Children.Add(substituir);
        painel.Children.Add(cancelar);
        dialogo.Loaded += (_, _) => substituir.Focus();
        return (dialogo, substituir, cancelar, () => confirmado);
    }
    private bool ConfirmarSubstituicaoLegendas(int quantidadeAtual)
    {
        var confirmacao = CriarDialogoSubstituicaoLegendas(quantidadeAtual);
        confirmacao.Dialogo.ShowDialog();
        return confirmacao.Confirmou();
    }
    private void Informar(string mensagem)
    {
        situacao.Text = mensagem;
        var peer = UIElementAutomationPeer.FromElement(situacao) ?? UIElementAutomationPeer.CreatePeerForElement(situacao);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
