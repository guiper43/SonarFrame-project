using System.Diagnostics;
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

public partial class JanelaPrincipal
{
    private IExportador exportador = null!;
    private CancellationTokenSource? cancelamentoExportacao;
    private bool exportacaoEmAndamento;
    private ProgressoOperacao? ultimoProgressoExportacao;
    private string? ultimoArquivoExportado;
    internal string PastaDownloadsExportacao { get; set; } = ConfiguracoesDesktop.PastaDownloadsPadrao;
    internal Task UltimaExportacao { get; private set; } = Task.CompletedTask;
    internal Func<OpcoesExportacao?>? SolicitarOpcoesExportacaoOverride { get; set; }
    internal Func<string, bool>? ConfirmarSubstituicaoExportacao { get; set; }
    internal Action<string, bool>? AbrirDestinoExportadoOverride { get; set; }

    internal sealed record DialogoConfiguracaoExportacao(
        Window Dialogo,
        ListBox Resolucao,
        ListBox Orientacao,
        ListBox Enquadramento,
        TextBox NomeArquivo,
        ListBox DestinoEnvio,
        TextBox PastaDestino,
        Button EscolherPasta,
        TextBlock Erro,
        Button Confirmar,
        Func<OpcoesExportacao?> ObterResultado);

    internal void ConfigurarExportacao(IExportador servicoExportador) => exportador = servicoExportador;

    private void ReiniciarEstadoExportacao()
    {
        if (exportacaoEmAndamento) return;
        ultimoProgressoExportacao = null;
        ultimoArquivoExportado = null;
        EstadoExportacao.Text = "";
        EstadoExportacao.Visibility = Visibility.Collapsed;
        BotaoProgressoExportacao.Visibility = Visibility.Collapsed;
        BotaoCancelarExportacao.Visibility = Visibility.Collapsed;
        BotaoAbrirVideoExportado.Visibility = Visibility.Collapsed;
        BotaoAbrirPastaExportada.Visibility = Visibility.Collapsed;
    }

    private void AtualizarControlesExportacao()
    {
        var temComposicao = sessao is not null && !sessao.Atual.LinhaDoTempo.IsEmpty && !previaEncerrada;
        BotaoExportar.IsEnabled = MenuExportar.IsEnabled = temComposicao && !exportacaoEmAndamento;
        BotaoSalvar.IsEnabled = sessao is not null;
        RegiaoBiblioteca.IsEnabled = !exportacaoEmAndamento;
        RegiaoPropriedades.IsEnabled = !exportacaoEmAndamento;
        RegiaoLinhaDoTempo.IsEnabled = !exportacaoEmAndamento;
        RegiaoPrevia.IsEnabled = !exportacaoEmAndamento;
        MenuPrincipal.IsEnabled = !exportacaoEmAndamento;
        BotaoVoltarInicio.IsEnabled = !exportacaoEmAndamento;
        BotaoProgressoExportacao.Visibility = exportacaoEmAndamento ? Visibility.Visible : Visibility.Collapsed;
        BotaoCancelarExportacao.Visibility = exportacaoEmAndamento ? Visibility.Visible : Visibility.Collapsed;
        var temExportado = !exportacaoEmAndamento && !string.IsNullOrWhiteSpace(ultimoArquivoExportado) && File.Exists(ultimoArquivoExportado);
        BotaoAbrirVideoExportado.Visibility = temExportado ? Visibility.Visible : Visibility.Collapsed;
        BotaoAbrirPastaExportada.Visibility = temExportado ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ExportarVideo(object sender, RoutedEventArgs e) => UltimaExportacao = IniciarFluxoExportacaoAsync();

    internal async Task IniciarFluxoExportacaoAsync()
    {
        if (sessao is null || previaEncerrada) return;
        if (exportacaoEmAndamento)
        {
            ConsultarProgressoExportacao(this, new RoutedEventArgs());
            return;
        }
        if (Ocupado) return;
        if (sessao.Atual.LinhaDoTempo.IsEmpty)
        {
            Anunciar("Adicione conteúdos à composição antes de exportar.");
            return;
        }
        if (!await PrepararEdicaoAsync()) return;
        var focoAnterior = FocusManager.GetFocusedElement(this) ?? Keyboard.FocusedElement;
        var opcoes = SolicitarOpcoesExportacaoOverride is { } overrideOpcoes ? overrideOpcoes() : SolicitarOpcoesExportacao();
        if (opcoes is null)
        {
            RestaurarFocoAposDialogo(focoAnterior, BotaoExportar);
            Anunciar("Exportação cancelada.");
            return;
        }
        await ExecutarExportacaoAsync(opcoes);
    }

    private OpcoesExportacao? SolicitarOpcoesExportacao()
    {
        var configuracao = CriarDialogoExportacao();
        configuracao.Dialogo.ShowDialog();
        return configuracao.ObterResultado();
    }

    internal DialogoConfiguracaoExportacao CriarDialogoExportacao()
    {
        OpcoesExportacao? resultado = null;
        var painel = new StackPanel();
        painel.Children.Add(new TextBlock { Text = "Formato de saída: MP4 (codificação automática H.264 e AAC).", Margin = new Thickness(0, 0, 0, 8) });
        var resolucao = CriarListaOpcoes(["1080p", "720p"], 0, "Resolução");
        painel.Children.Add(new Label { Content = "_Resolução", Target = resolucao });
        painel.Children.Add(resolucao);
        var orientacao = CriarListaOpcoes(["16:9 (horizontal)", "9:16 (vertical)"], -1, "Orientação (escolha obrigatória)");
        painel.Children.Add(new Label { Content = "_Orientação (escolha obrigatória)", Target = orientacao });
        painel.Children.Add(orientacao);
        var enquadramento = CriarListaOpcoes(["Permitir bordas", "Preencher a tela"], 0, "Enquadramento");
        painel.Children.Add(new Label { Content = "_Enquadramento", Target = enquadramento });
        painel.Children.Add(enquadramento);
        var nomeArquivo = new TextBox { Text = NomePadraoExportacao(), Margin = new Thickness(0, 4, 0, 8) };
        AutomationProperties.SetName(nomeArquivo, "Nome do arquivo MP4");
        painel.Children.Add(new Label { Content = "_Nome do arquivo (MP4)", Target = nomeArquivo });
        painel.Children.Add(nomeArquivo);
        var downloadsPadrao = PastaDownloadsPadraoEfetiva();
        var pastaInicial = PastaPadraoExportacao();
        var ehDownloadsInicial = ConfiguracoesDesktop.MesmaPasta(pastaInicial, downloadsPadrao);
        var ultimaPastaPersonalizada = ehDownloadsInicial ? "" : pastaInicial;
        var destinoEnvio = CriarListaOpcoes(["Pasta Downloads (padrão)", "Outra pasta selecionada"], ehDownloadsInicial ? 0 : 1, "Para onde enviar o vídeo exportado");
        painel.Children.Add(new Label { Content = "Para _onde enviar o vídeo", Target = destinoEnvio });
        painel.Children.Add(destinoEnvio);
        var pastaDestino = new TextBox { Text = pastaInicial, Margin = new Thickness(0, 4, 0, 8) };
        AutomationProperties.SetName(pastaDestino, "Pasta de destino");
        painel.Children.Add(new Label { Content = "Pasta de _destino", Target = pastaDestino });
        painel.Children.Add(pastaDestino);
        var sincronizandoDestino = false;
        destinoEnvio.SelectionChanged += (_, _) =>
        {
            if (sincronizandoDestino) return;
            sincronizandoDestino = true;
            try
            {
                var padraoAtual = PastaDownloadsPadraoEfetiva();
                if (destinoEnvio.SelectedIndex == 0)
                {
                    var atual = pastaDestino.Text.Trim();
                    if (!string.IsNullOrWhiteSpace(atual) && !ConfiguracoesDesktop.MesmaPasta(atual, padraoAtual))
                        ultimaPastaPersonalizada = atual;
                    pastaDestino.Text = padraoAtual;
                }
                else if (destinoEnvio.SelectedIndex == 1 && ConfiguracoesDesktop.MesmaPasta(pastaDestino.Text.Trim(), padraoAtual))
                {
                    pastaDestino.Text = ultimaPastaPersonalizada;
                }
            }
            finally { sincronizandoDestino = false; }
        };
        pastaDestino.TextChanged += (_, _) =>
        {
            if (sincronizandoDestino) return;
            sincronizandoDestino = true;
            try
            {
                var padraoAtual = PastaDownloadsPadraoEfetiva();
                var atual = pastaDestino.Text.Trim();
                if (!string.IsNullOrWhiteSpace(atual) && ConfiguracoesDesktop.MesmaPasta(atual, padraoAtual))
                    destinoEnvio.SelectedIndex = 0;
                else
                {
                    destinoEnvio.SelectedIndex = 1;
                    if (!string.IsNullOrWhiteSpace(atual))
                        ultimaPastaPersonalizada = atual;
                }
            }
            finally { sincronizandoDestino = false; }
        };
        var escolherPasta = new Button { Content = "Escolher _pasta…" };
        var dialogo = CriarDialogo("Exportar vídeo (MP4)", painel);
        escolherPasta.Click += (_, _) =>
        {
            var seletor = new OpenFolderDialog { Title = "Pasta de destino da exportação" };
            if (seletor.ShowDialog(dialogo) == true) pastaDestino.Text = seletor.FolderName;
            pastaDestino.Focus();
        };
        painel.Children.Add(escolherPasta);
        var erro = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        AutomationProperties.SetLiveSetting(erro, AutomationLiveSetting.Assertive);
        painel.Children.Add(erro);
        var confirmar = new Button { Content = "Exportar", IsDefault = true };
        void InformarErro(string mensagem, Control destinoFoco)
        {
            erro.Text = mensagem;
            (UIElementAutomationPeer.FromElement(erro) ?? UIElementAutomationPeer.CreatePeerForElement(erro))
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            if (destinoFoco is ListBox lista) FocarOpcaoLista(lista);
            else
            {
                destinoFoco.Focus();
                if (destinoFoco is TextBox texto) texto.SelectAll();
            }
        }
        confirmar.Click += (_, _) =>
        {
            if (orientacao.SelectedIndex < 0)
            {
                InformarErro("Escolha a orientação do vídeo: 16:9 (horizontal) ou 9:16 (vertical).", orientacao);
                return;
            }
            if (resolucao.SelectedIndex < 0)
            {
                InformarErro("Escolha a resolução: 1080p ou 720p.", resolucao);
                return;
            }
            if (enquadramento.SelectedIndex < 0)
            {
                InformarErro("Escolha o enquadramento: Permitir bordas ou Preencher a tela.", enquadramento);
                return;
            }
            var nome = nomeArquivo.Text.Trim();
            if (string.IsNullOrWhiteSpace(nome) || nome.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                InformarErro("Informe um nome de arquivo válido.", nomeArquivo);
                return;
            }
            if (string.IsNullOrEmpty(Path.GetExtension(nome))) nome += ".mp4";
            if (!string.Equals(Path.GetExtension(nome), ".mp4", StringComparison.OrdinalIgnoreCase))
            {
                InformarErro("O vídeo exportado deve utilizar o formato MP4 (.mp4).", nomeArquivo);
                return;
            }
            if (!OpcoesExportacao.NomeArquivoValido(nome))
            {
                InformarErro("Informe um nome de arquivo válido.", nomeArquivo);
                return;
            }
            if (destinoEnvio.SelectedIndex < 0)
            {
                InformarErro("Escolha para onde enviar o vídeo: Pasta Downloads ou Outra pasta selecionada.", destinoEnvio);
                return;
            }
            var padraoAtual = PastaDownloadsPadraoEfetiva();
            var pasta = pastaDestino.Text.Trim();
            if (destinoEnvio.SelectedIndex == 0 && string.IsNullOrWhiteSpace(pasta))
                pasta = padraoAtual;
            if (string.IsNullOrWhiteSpace(pasta) || !Path.IsPathFullyQualified(pasta))
            {
                InformarErro("Informe uma pasta de destino existente.", pastaDestino);
                return;
            }
            if (PastaMisturaArquivosProjeto(pasta))
            {
                InformarErro("Escolha uma pasta diferente da pasta do projeto para não misturar arquivos do projeto com vídeos exportados.", pastaDestino);
                return;
            }
            if ((ConfiguracoesDesktop.MesmaPasta(pasta, padraoAtual)
                || ConfiguracoesDesktop.MesmaPasta(pasta, ConfiguracoesDesktop.PastaExportacoesPadrao(configuracoes.DiretorioProjetos))) && !Directory.Exists(pasta))
            {
                try { Directory.CreateDirectory(pasta); }
                catch (Exception) { }
            }
            if (!Directory.Exists(pasta))
            {
                InformarErro("Informe uma pasta de destino existente.", pastaDestino);
                return;
            }
            var pastaCompleta = Path.GetFullPath(pasta);
            var destino = Path.Combine(pastaCompleta, nome);
            if (sessao?.Atual.Biblioteca.Any(m => string.Equals(Path.GetFullPath(m.Caminho), destino, StringComparison.OrdinalIgnoreCase)) == true)
            {
                InformarErro("O destino coincide com uma mídia do projeto. Escolha outro nome.", nomeArquivo);
                return;
            }
            var substituir = false;
            if (File.Exists(destino))
            {
                substituir = ConfirmarSubstituicaoExportacao?.Invoke(destino)
                    ?? MessageBox.Show(dialogo, $"O arquivo {nome} já existe na pasta escolhida. Deseja substituí-lo?",
                        "Substituir arquivo", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
                if (!substituir)
                {
                    InformarErro("Escolha outro nome de arquivo ou confirme a substituição.", nomeArquivo);
                    return;
                }
            }
            try
            {
                var novasConfiguracoes = configuracoes with { DiretorioExportacoes = pastaCompleta };
                repositorioConfiguracoes.Salvar(novasConfiguracoes);
                configuracoes = novasConfiguracoes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                InformarErro("Não foi possível salvar a preferência de pasta: " + ex.Message, pastaDestino);
                return;
            }
            var opcoes = new OpcoesExportacao(
                destino,
                orientacao.SelectedIndex == 0 ? Application.Orientacao.Horizontal : Application.Orientacao.Vertical,
                enquadramento.SelectedIndex == 0 ? ModoEnquadramento.Letterbox : ModoEnquadramento.Preencher,
                resolucao.SelectedIndex == 0 ? 1080 : 720,
                substituir);
            opcoes.Validar();
            resultado = opcoes;
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) dialogo.DialogResult = true;
            else dialogo.Close();
        };
        painel.Children.Add(confirmar);
        AdicionarFechar(dialogo, painel, "Cancelar");
        dialogo.Loaded += (_, _) => FocarOpcaoLista(resolucao);
        return new(dialogo, resolucao, orientacao, enquadramento, nomeArquivo, destinoEnvio, pastaDestino, escolherPasta, erro, confirmar, () => resultado);
    }

    private static ListBox CriarListaOpcoes(string[] itens, int selecionado, string nome)
    {
        var lista = new ListBox
        {
            ItemsSource = itens,
            SelectedIndex = selecionado,
            MinHeight = 0,
            Margin = new Thickness(0, 4, 0, 8)
        };
        KeyboardNavigation.SetTabNavigation(lista, KeyboardNavigationMode.Once);
        AutomationProperties.SetName(lista, nome);
        lista.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            if ((bool)e.NewValue) FocarOpcaoLista(lista);
        };
        lista.PreviewKeyDown += (_, e) =>
        {
            if (e.KeyboardDevice.Modifiers != ModifierKeys.None) return;
            if (e.Key is Key.Up or Key.Left or Key.Down or Key.Right)
            {
                var avancar = e.Key is Key.Down or Key.Right;
                lista.SelectedIndex = lista.SelectedIndex < 0
                    ? (avancar ? 0 : lista.Items.Count - 1)
                    : Math.Clamp(lista.SelectedIndex + (avancar ? 1 : -1), 0, lista.Items.Count - 1);
                FocarOpcaoLista(lista);
                e.Handled = true;
            }
            else if (e.Key == Key.Space && lista.SelectedIndex < 0)
            {
                lista.SelectedIndex = 0;
                FocarOpcaoLista(lista);
                e.Handled = true;
            }
        };
        return lista;
    }

    private static void FocarOpcaoLista(ListBox lista)
    {
        lista.UpdateLayout();
        var janela = Window.GetWindow(lista);
        if (lista.SelectedItem is not null && lista.ItemContainerGenerator.ContainerFromItem(lista.SelectedItem) is ListBoxItem item)
        {
            item.Focus();
            if (janela?.IsActive == true) Keyboard.Focus(item);
        }
        else
        {
            lista.Focus();
            if (janela?.IsActive == true) Keyboard.Focus(lista);
        }
    }

    private string NomePadraoExportacao()
    {
        var baseNome = sessao?.Atual.Nome ?? "video";
        var invalidos = Path.GetInvalidFileNameChars().ToHashSet();
        var limpo = new string(baseNome.Where(c => !invalidos.Contains(c)).ToArray()).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(limpo)) limpo = "video";
        var candidato = limpo.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? limpo : limpo + ".mp4";
        return OpcoesExportacao.NomeArquivoValido(candidato) ? candidato : "video.mp4";
    }

    internal string PastaDownloadsPadraoEfetiva()
        => PastaMisturaArquivosProjeto(PastaDownloadsExportacao)
            ? Path.Combine(PastaDownloadsExportacao, "sonarFrame-exportacoes")
            : PastaDownloadsExportacao;

    private string PastaPadraoExportacao()
    {
        var downloadsPadrao = PastaDownloadsPadraoEfetiva();
        var configurada = configuracoes.DiretorioExportacoes;
        if (!string.IsNullOrWhiteSpace(configurada)
            && Path.IsPathFullyQualified(configurada)
            && !ConfiguracoesDesktop.MesmaPasta(configurada, ConfiguracoesDesktop.PastaDownloadsPadrao)
            && !ConfiguracoesDesktop.MesmaPasta(configurada, PastaDownloadsExportacao)
            && !ConfiguracoesDesktop.MesmaPasta(configurada, downloadsPadrao)
            && !PastaMisturaArquivosProjeto(configurada)
            && Directory.Exists(configurada))
            return Path.GetFullPath(configurada);
        return downloadsPadrao;
    }

    internal bool PastaMisturaArquivosProjeto(string pasta)
    {
        if (string.IsNullOrWhiteSpace(pasta)) return false;
        if (ConfiguracoesDesktop.MesmaPasta(pasta, configuracoes.DiretorioProjetos)) return true;
        if (!string.IsNullOrWhiteSpace(caminhoProjeto)
            && Path.GetDirectoryName(caminhoProjeto) is { } pastaProjeto
            && ConfiguracoesDesktop.MesmaPasta(pasta, pastaProjeto))
            return true;
        try
        {
            var completa = Path.GetFullPath(pasta);
            return Directory.Exists(completa)
                && (Directory.EnumerateFiles(completa, "*.sonarframe").Any()
                    || Directory.EnumerateFiles(completa, "*.sonarframe.recovery").Any());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    internal async Task ExecutarExportacaoAsync(OpcoesExportacao opcoes)
    {
        if (sessao is null || exportacaoEmAndamento || previaEncerrada) return;
        var snapshot = sessao.Atual;
        using var cancelamento = new CancellationTokenSource();
        cancelamentoExportacao = cancelamento;
        exportacaoEmAndamento = true;
        ultimoArquivoExportado = null;
        ultimoProgressoExportacao = new("Preparando a exportação", 0, null);
        EstadoExportacao.Text = DescreverProgressoExportacao(ultimoProgressoExportacao);
        EstadoExportacao.Visibility = Visibility.Visible;
        Atualizar();
        BotaoProgressoExportacao.Focus();
        var decimoAnunciado = 0;
        Anunciar($"Exportação iniciada para {Path.GetFileName(opcoes.Destino)}. Use Consultar progresso da exportação ou Escape para cancelar.");
        var progresso = new Progress<ProgressoOperacao>(p =>
        {
            if (!ReferenceEquals(cancelamentoExportacao, cancelamento) || cancelamento.IsCancellationRequested) return;
            ultimoProgressoExportacao = p;
            EstadoExportacao.Text = DescreverProgressoExportacao(p);
            var decimo = (int)((p.Porcentagem ?? 0) / 10) * 10;
            if (decimo >= 10 && decimo < 100 && decimo > decimoAnunciado)
            {
                decimoAnunciado = decimo;
                Anunciar(DescreverProgressoExportacao(p));
            }
        });
        try
        {
            if (Path.GetDirectoryName(opcoes.Destino) is { } pastaExportacao && PastaMisturaArquivosProjeto(pastaExportacao))
                throw new IOException("Escolha uma pasta diferente da pasta do projeto para não misturar arquivos do projeto com vídeos exportados.");
            await exportador.ExportarAsync(snapshot, opcoes, progresso, cancelamento.Token);
            ultimoArquivoExportado = opcoes.Destino;
            ultimoProgressoExportacao = new("Exportação concluída", 100, TimeSpan.Zero);
            EstadoExportacao.Text = $"Exportação concluída: {opcoes.Destino}";
            EstadoExportacao.Visibility = Visibility.Visible;
            exportacaoEmAndamento = false;
            cancelamentoExportacao = null;
            Atualizar();
            BotaoAbrirVideoExportado.Focus();
            Anunciar($"Exportação concluída: {Path.GetFileName(opcoes.Destino)}. Use Abrir vídeo exportado ou Abrir pasta do vídeo.");
        }
        catch (OperationCanceledException)
        {
            EstadoExportacao.Text = "Exportação cancelada.";
            EstadoExportacao.Visibility = Visibility.Visible;
            exportacaoEmAndamento = false;
            cancelamentoExportacao = null;
            Atualizar();
            BotaoExportar.Focus();
            Anunciar("Exportação cancelada. O projeto e as mídias originais foram preservados.");
        }
        catch (Exception ex)
        {
            EstadoExportacao.Text = "Falha na exportação: " + ex.Message;
            EstadoExportacao.Visibility = Visibility.Visible;
            exportacaoEmAndamento = false;
            cancelamentoExportacao = null;
            Atualizar();
            BotaoExportar.Focus();
            Anunciar("Falha na exportação: " + ex.Message);
        }
    }

    internal static string DescreverProgressoExportacao(ProgressoOperacao progresso)
    {
        var percentual = progresso.Porcentagem is { } valor ? $": {(int)Math.Round(valor)}%" : "";
        var restante = progresso.Restante is { } tempo && tempo > TimeSpan.Zero
            ? $". Tempo restante estimado: {TempoFalado(new TempoMidia( Math.Max(1, (long)Math.Ceiling(tempo.TotalSeconds)), 1))}."
            : ".";
        return $"{progresso.Estado}{percentual}{restante}";
    }

    private void ConsultarProgressoExportacao(object sender, RoutedEventArgs e)
    {
        if (exportacaoEmAndamento && ultimoProgressoExportacao is { } progresso)
            Anunciar(DescreverProgressoExportacao(progresso));
        else if (!string.IsNullOrWhiteSpace(EstadoExportacao.Text))
            Anunciar(EstadoExportacao.Text);
        else
            Anunciar("Nenhuma exportação em andamento.");
    }

    private void CancelarExportacao(object sender, RoutedEventArgs e)
    {
        if (!exportacaoEmAndamento || cancelamentoExportacao is null) return;
        cancelamentoExportacao.Cancel();
    }

    private void AbrirVideoExportado(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ultimoArquivoExportado) || !File.Exists(ultimoArquivoExportado))
        {
            Anunciar("O arquivo exportado não foi encontrado.");
            return;
        }
        try
        {
            if (AbrirDestinoExportadoOverride is { } acao) acao(ultimoArquivoExportado, false);
            else Process.Start(new ProcessStartInfo(ultimoArquivoExportado) { UseShellExecute = true });
            Anunciar($"Abrindo vídeo: {Path.GetFileName(ultimoArquivoExportado)}.");
        }
        catch (Exception ex) { Anunciar("Não foi possível abrir o vídeo: " + ex.Message); }
    }

    private void AbrirPastaExportada(object sender, RoutedEventArgs e)
    {
        var pasta = string.IsNullOrWhiteSpace(ultimoArquivoExportado) ? null : Path.GetDirectoryName(ultimoArquivoExportado);
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta))
        {
            Anunciar("A pasta do vídeo não foi encontrada.");
            return;
        }
        try
        {
            if (AbrirDestinoExportadoOverride is { } acao) acao(pasta, true);
            else Process.Start(new ProcessStartInfo(pasta) { UseShellExecute = true });
            Anunciar($"Abrindo pasta: {pasta}.");
        }
        catch (Exception ex) { Anunciar("Não foi possível abrir a pasta: " + ex.Message); }
    }
}
