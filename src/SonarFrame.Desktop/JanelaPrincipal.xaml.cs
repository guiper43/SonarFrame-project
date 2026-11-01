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

public partial class JanelaPrincipal : Window
{
    private SessaoEdicao? sessao;
    private string? caminhoProjeto;
    private bool atualizando;
    private bool salvando;
    private bool exibirPropriedadesBiblioteca;
    private readonly RepositorioProjetosSqlite repositorio = new();
    private readonly List<string> mensagensRecentes = [];
    private sealed record Linha(Guid Id, string Rotulo);
    public JanelaPrincipal()
    {
        InitializeComponent();
        ConfigurarCicloDeVida();
        Loaded += (_, _) => BotaoNovo.Focus();
    }
    private FrameworkElement[] Regioes => [RegiaoBiblioteca, RegiaoPropriedades, RegiaoLinhaDoTempo, RegiaoPrevia, RegiaoComandos];
    private Control[] Destinos => [BotaoAdicionar, Propriedades, NavegadorTemporal, BotaoPrevia, BotaoSalvar];
    private void Anunciar(string texto)
    {
        Situacao.Text = texto;
        mensagensRecentes.Insert(0, texto);
        if (mensagensRecentes.Count > 100) mensagensRecentes.RemoveAt(100);
        Mensagens.ItemsSource = mensagensRecentes.ToArray();
        var peer = UIElementAutomationPeer.FromElement(Situacao) ?? UIElementAutomationPeer.CreatePeerForElement(Situacao);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    internal void ExibirProjeto(Projeto projeto, bool salvo = false, string? caminho = null)
    {
        if (caminho is null)
        {
            try
            {
                Directory.CreateDirectory(configuracoes.DiretorioProjetos);
                caminho = CatalogoProjetos.GerarCaminho(configuracoes.DiretorioProjetos, projeto.Nome);
            }
            catch (Exception e) { Anunciar("Não foi possível criar o projeto na pasta configurada: " + e.Message); return; }
        }
        sessao = new SessaoEdicao(projeto, salvo);
        caminhoProjeto = caminho;
        persistencia = new PersistenciaProjeto(sessao, repositorio, caminhoProjeto);
        TelaInicial.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        mensagensRecentes.Clear();
        exibirPropriedadesBiblioteca = false;
        Propriedades.Text = "Selecione um conteúdo na biblioteca ou na linha do tempo.";
        Atualizar();
        BotaoAdicionar.Focus();
        Anunciar($"Projeto {projeto.Nome} aberto. Biblioteca de mídias. Use F1 para consultar os atalhos.");
    }
    internal void Atualizar()
    {
        if (sessao is null) return;
        atualizando = true;
        var p = sessao.Atual;
        var idsBiblioteca = Biblioteca.SelectedItems.Cast<Linha>().Select(r => r.Id).ToHashSet();
        var linhasBiblioteca = p.Biblioteca.Select(a => new Linha(a.Id, $"{a.Nome} · {Tipo(a.Tipo)} · {a.Duracao?.ToString() ?? "definir duração na inserção"}")).ToArray();
        if (!Biblioteca.Items.Cast<Linha>().SequenceEqual(linhasBiblioteca))
        {
            Biblioteca.ItemsSource = linhasBiblioteca;
            foreach (var linha in linhasBiblioteca.Where(r => idsBiblioteca.Contains(r.Id))) Biblioteca.SelectedItems.Add(linha);
        }
        var linhasLinhaDoTempo = new AcoesEdicao(sessao).Conteudos().Select((c, i) => new Linha(c.Id, $"{i + 1}. {c.Nome} · {(c.EhCamada ? "Camada" : "Sequência")} · início {c.Inicio} · duração {c.Duracao}")).ToArray();
        if (!LinhaDoTempo.Items.Cast<Linha>().SequenceEqual(linhasLinhaDoTempo)) LinhaDoTempo.ItemsSource = linhasLinhaDoTempo;
        LinhaDoTempo.SelectedItem = LinhaDoTempo.Items.Cast<Linha>().FirstOrDefault(r => r.Id == p.ItemSelecionadoId);
        TituloProjeto.Text = p.Nome;
        Title = $"{p.Nome}{(sessao.TemAlteracoes ? " *" : "")} — sonarFrame";
        NavegadorTemporal.Content = $"Posição: {p.Cursor} · Duração: {p.Duracao}";
        ResumoSelecao.Text = p.Selecao is { } selecao ? $"Seleção: {selecao.Inicio} até {selecao.Fim}" : "Nenhum intervalo selecionado.";
        AutomationProperties.SetName(NavegadorTemporal, $"Navegação temporal. Posição {p.Cursor}");
        BibliotecaVazia.Visibility = p.Biblioteca.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        LinhaDoTempoVazia.Visibility = p.LinhaDoTempo.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        BotaoInserir.IsEnabled = Biblioteca.SelectedItems.Count > 0;
        BotaoDesfazer.IsEnabled = sessao.PodeDesfazer; BotaoRefazer.IsEnabled = sessao.PodeRefazer;
        BotaoExcluirSelecao.IsEnabled = p.Selecao is { } selecionado && selecionado.Fim > selecionado.Inicio;
        BotaoDividir.IsEnabled = p.LinhaDoTempo.Length > 0;
        var conteudoSelecionado = new AcoesEdicao(sessao).Conteudos().SingleOrDefault(c => c.Id == p.ItemSelecionadoId);
        BotaoVolume.IsEnabled = !exibirPropriedadesBiblioteca && conteudoSelecionado is not null && p.Biblioteca.Single(a => a.Id == conteudoSelecionado.MidiaId).Tipo != TipoMidia.Imagem;
        BotaoEditarCamada.IsEnabled = BotaoRemoverCamada.IsEnabled = !exibirPropriedadesBiblioteca && conteudoSelecionado?.EhCamada == true;
        BotaoAdicionarCamada.IsEnabled = Biblioteca.SelectedItems.Count == 1 && p.Duracao > TempoMidia.Zero;
        atualizando = false;
        if (exibirPropriedadesBiblioteca && Biblioteca.SelectedItem is Linha linhaBiblioteca) DescreverMidia(linhaBiblioteca.Id);
        else if (conteudoSelecionado is not null) DescreverConteudo(conteudoSelecionado);
        else Propriedades.Text = "Selecione um conteúdo na biblioteca ou na linha do tempo.";
    }
    private static string Tipo(TipoMidia tipo) => tipo switch { TipoMidia.Video => "Vídeo", TipoMidia.Audio => "Áudio", _ => "Imagem" };
    private async void NovoProjeto(object sender, RoutedEventArgs e)
    {
        if (Ocupado) return;
        var entrada = new TextBox { Text = "Meu projeto", Margin = new Thickness(0, 6, 0, 16), MinWidth = 320 };
        AutomationProperties.SetName(entrada, "Nome do projeto");
        var painel = new StackPanel();
        painel.Children.Add(new Label { Content = "_Nome do projeto", Target = entrada }); painel.Children.Add(entrada);
        var dialogo = CriarDialogo("Novo projeto", painel);
        var criar = new Button { Content = "Criar projeto", IsDefault = true };
        criar.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(entrada.Text)) dialogo.DialogResult = true; else { entrada.Focus(); MessageBox.Show(dialogo, "Informe um nome para o projeto."); } };
        painel.Children.Add(criar); AdicionarFechar(dialogo, painel, "Cancelar");
        dialogo.Loaded += (_, _) => { entrada.Focus(); entrada.SelectAll(); };
        if (dialogo.ShowDialog() == true && await PodeSairAsync()) ExibirProjeto(Projeto.Criar(entrada.Text.Trim()));
    }
    private async void Demonstracao(object sender, RoutedEventArgs e)
    {
        if (!await PodeSairAsync()) return;
        var demonstracao = new SessaoEdicao(Projeto.Criar("Explorar o sonarFrame — demonstração"));
        foreach (var nome in new[] { "Apresentação", "Cena principal", "Encerramento" })
        {
            var midia = new Midia(Guid.NewGuid(), nome + " (exemplo)", nome + ".mp4", TipoMidia.Video, TempoMidia.Segundos(10), new TaxaQuadros(30, 1));
            demonstracao.Executar(new RegistrarMidia(midia)); demonstracao.Executar(new InserirClipe(midia.Id, demonstracao.Atual.LinhaDoTempo.Length));
        }
        ExibirProjeto(demonstracao.Atual, true);
    }
    private async void AbrirProjeto(object sender, RoutedEventArgs e)
    {
        if (Ocupado) return;
        var dialogo = new OpenFileDialog { Filter = "Projeto sonarFrame|*.sonarframe|Todos os arquivos|*.*" };
        if (dialogo.ShowDialog(this) != true) return;
        await AbrirCaminhoAsync(dialogo.FileName);
    }
    private async void SalvarProjeto(object sender, RoutedEventArgs e)
    {
        if (!Ocupado) await SalvarAtualAsync();
    }
    private async void IrParaInicio(object sender, RoutedEventArgs e)
    {
        if (!await PodeSairAsync()) return;
        sessao = null; persistencia = null; Editor.Visibility = Visibility.Collapsed; TelaInicial.Visibility = Visibility.Visible;
        Title = "sonarFrame"; BotaoNovo.Focus(); Anunciar("Tela inicial.");
    }
    private void Sair(object sender, RoutedEventArgs e) => Close();
    private bool Executar(IComandoEdicao comando)
    {
        if (sessao is null) return false;
        try { sessao.Executar(comando); exibirPropriedadesBiblioteca = false; Atualizar(); return true; }
        catch (Exception ex) { Anunciar(ex.Message); return false; }
    }
    private void Inserir(object sender, RoutedEventArgs e)
        => InserirNaPosicao(null);
    private void InserirAntes(object sender, RoutedEventArgs e)
    {
        if (sessao is null || LinhaDoTempo.SelectedItem is not Linha selecionado) { Anunciar("Selecione um clipe da sequência antes de inserir."); return; }
        var indice = sessao.Atual.LinhaDoTempo.Select((c, i) => (c, i)).Where(x => x.c.Id == selecionado.Id).Select(x => x.i).DefaultIfEmpty(-1).Single();
        if (indice < 0) { Anunciar("Selecione um clipe da sequência principal, não uma camada."); return; }
        InserirNaPosicao(indice);
    }
    private void InserirNaPosicao(int? indice)
    {
        if (sessao is null || Biblioteca.SelectedItems.Count == 0) return;
        var ids = Biblioteca.Items.Cast<Linha>().Where(r => Biblioteca.SelectedItems.Contains(r)).Select(r => r.Id).ToImmutableArray();
        TempoMidia? duracao = null;
        if (sessao.Atual.Biblioteca.Any(a => ids.Contains(a.Id) && a.Tipo == TipoMidia.Imagem))
        {
            duracao = SolicitarDuracaoImagem();
            if (duracao is null) { BotaoInserir.Focus(); return; }
        }
        if (Executar(new InserirClipes(ids, duracao, indice))) Anunciar($"{ids.Length} conteúdo(s) adicionado(s) à linha do tempo.");
    }
    private void MidiaSelecionada(object sender, SelectionChangedEventArgs e)
    {
        if (atualizando || sessao is null) return;
        exibirPropriedadesBiblioteca = true;
        BotaoInserir.IsEnabled = Biblioteca.SelectedItems.Count > 0;
        BotaoAdicionarCamada.IsEnabled = Biblioteca.SelectedItems.Count == 1 && sessao.Atual.Duracao > TempoMidia.Zero;
        BotaoVolume.IsEnabled = BotaoEditarCamada.IsEnabled = BotaoRemoverCamada.IsEnabled = false;
        if (Biblioteca.SelectedItem is Linha linha)
            DescreverMidia(linha.Id);
        else Propriedades.Text = "Selecione um conteúdo na biblioteca ou na linha do tempo.";
    }
    private void ConteudoSelecionado(object sender, SelectionChangedEventArgs e)
    {
        if (atualizando || sessao is null || LinhaDoTempo.SelectedItem is not Linha linha) return;
        exibirPropriedadesBiblioteca = false;
        sessao.SelecionarItem(linha.Id);
        Atualizar();
    }
    private void BibliotecaRecebeuFoco(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (atualizando || sessao is null || Biblioteca.SelectedItem is null) return;
        exibirPropriedadesBiblioteca = true; Atualizar();
    }
    private void LinhaDoTempoRecebeuFoco(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (atualizando || sessao is null || LinhaDoTempo.SelectedItem is null) return;
        exibirPropriedadesBiblioteca = false; Atualizar();
    }
    private void Mover(int destino)
    {
        if (sessao is null || LinhaDoTempo.SelectedItem is not Linha linha) { Anunciar("Selecione um conteúdo na lista da composição."); return; }
        if (!sessao.Atual.LinhaDoTempo.Any(c => c.Id == linha.Id)) { Anunciar("Use Editar camada para alterar a posição temporal de uma camada."); return; }
        destino = Math.Clamp(destino, 0, sessao.Atual.LinhaDoTempo.Length - 1);
        if (!Executar(new MoverClipe(linha.Id, destino))) return;
        LinhaDoTempo.Focus();
        if (LinhaDoTempo.ItemContainerGenerator.ContainerFromItem(LinhaDoTempo.SelectedItem) is ListBoxItem item) item.Focus();
        Anunciar($"Conteúdo movido para a posição {destino + 1} de {sessao.Atual.LinhaDoTempo.Length}.");
    }
    private void MoverParaInicio(object sender, RoutedEventArgs e) => Mover(0);
    private void MoverParaFim(object sender, RoutedEventArgs e) => Mover((sessao?.Atual.LinhaDoTempo.Length ?? 1) - 1);
    private void MoverAntes(object sender, RoutedEventArgs e) => Mover(LinhaDoTempo.SelectedIndex - 1);
    private void MoverDepois(object sender, RoutedEventArgs e) => Mover(LinhaDoTempo.SelectedIndex + 1);
    private void Desfazer(object sender, RoutedEventArgs e) { if (sessao?.PodeDesfazer != true) return; sessao.Desfazer(); Atualizar(); Anunciar("Edição desfeita."); }
    private void Refazer(object sender, RoutedEventArgs e) { if (sessao?.PodeRefazer != true) return; sessao.Refazer(); Atualizar(); Anunciar("Edição refeita."); }
    private void LerPosicao(object sender, RoutedEventArgs e) => Anunciar(sessao?.Atual.Cursor.ToString() ?? "Nenhum projeto aberto.");
    private void TeclaNavegacaoTemporal(object sender, KeyEventArgs e)
    {
        if (sessao is null) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var controle = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        try
        {
            switch (e.Key)
            {
                case Key.Left: case Key.Right:
                    var direcao = e.Key == Key.Left ? -1 : 1;
                    if (controle) sessao.Deslocar(TempoMidia.Segundos(direcao), shift); else sessao.AvancarQuadro(direcao, shift);
                    break;
                case Key.Home: sessao.Posicionar(TempoMidia.Zero, shift); break;
                case Key.End: sessao.Posicionar(sessao.Atual.Duracao, shift); break;
                case Key.PageUp: sessao.Deslocar(TempoMidia.Segundos(-10), shift); break;
                case Key.PageDown: sessao.Deslocar(TempoMidia.Segundos(10), shift); break;
                case Key.Space: InformacoesPrevia(sender, e); e.Handled = true; return;
                case Key.Delete: ExcluirIntervaloSelecionado(sender, e); e.Handled = true; return;
                default: return;
            }
            Atualizar(); Anunciar(sessao.Atual.Cursor.ToString()); e.Handled = true;
        }
        catch (Exception ex) { e.Handled = true; Anunciar(ex.Message); }
    }
    private void TeclaJanela(object sender, KeyEventArgs e)
    {
        var chave = e.Key == Key.System ? e.SystemKey : e.Key;
        if (chave == Key.F1) { Ajuda(sender, e); e.Handled = true; return; }
        if (Keyboard.Modifiers == ModifierKeys.Control && chave == Key.S) { SalvarProjeto(sender, e); e.Handled = true; return; }
        if (Editor.Visibility != Visibility.Visible) return;
        if (Keyboard.FocusedElement is not TextBox && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (chave == Key.Z) { Desfazer(sender, e); e.Handled = true; return; }
            if (chave == Key.Y) { Refazer(sender, e); e.Handled = true; return; }
            if (chave == Key.B) { DividirNoCursor(sender, e); e.Handled = true; return; }
        }
        var indice = -1;
        if (Keyboard.Modifiers == ModifierKeys.Alt && chave >= Key.D1 && chave <= Key.D5) indice = chave - Key.D1;
        if (chave == Key.F6)
        {
            var atual = Array.FindIndex(Regioes, r => r.IsKeyboardFocusWithin);
            var reverso = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            indice = atual < 0 ? (reverso ? 4 : 0) : (atual + (reverso ? 4 : 1)) % 5;
        }
        if (indice >= 0) { Destinos[indice].Focus(); Destinos[indice].BringIntoView(); e.Handled = true; }
    }
    private Window CriarDialogo(string titulo, UIElement conteudo) => new()
    {
        Title = titulo, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        SizeToContent = SizeToContent.Height, Width = 520, ResizeMode = ResizeMode.NoResize,
        Content = new Border { Padding = new Thickness(24), Child = conteudo }
    };
    private static void AdicionarFechar(Window dialogo, Panel painel, string rotulo = "Fechar")
    {
        var botao = new Button { Content = rotulo, IsCancel = true };
        botao.Click += (_, _) => dialogo.Close(); painel.Children.Add(botao);
    }
    private void Informacoes(string titulo, string texto)
    {
        var foco = Keyboard.FocusedElement;
        var painel = new StackPanel();
        var corpo = new TextBox { Text = texto, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 16) };
        AutomationProperties.SetName(corpo, titulo); painel.Children.Add(corpo);
        var dialogo = CriarDialogo(titulo, painel); AdicionarFechar(dialogo, painel);
        dialogo.Loaded += (_, _) => corpo.Focus(); dialogo.ShowDialog();
        if (foco is IInputElement elemento) Keyboard.Focus(elemento);
    }
    private void AdicionarMidia(object sender, RoutedEventArgs e)
    {
        if (sessao is null) return;
        var executavel = Path.Combine(AppContext.BaseDirectory, "tools", "ffprobe.exe");
        var leitorMetadados = new LeitorMetadadosFfprobe(new ExecutorFfprobe(File.Exists(executavel) ? executavel : "ffprobe"));
        var dialogo = new DialogoImportacao(this, sessao, leitorMetadados);
        dialogo.ShowDialog(); Atualizar(); BotaoAdicionar.Focus();
        foreach (var mensagem in sessao.Mensagens.Where(m => m.Codigo == "importacao.falhou"))
            if (!mensagensRecentes.Contains(mensagem.Mensagem)) mensagensRecentes.Insert(0, mensagem.Mensagem);
        if (dialogo.ImportouAlgumaMidia) Anunciar("Mídias adicionadas à biblioteca. Pressione Tab para selecionar os arquivos e depois escolha Adicionar à linha do tempo.");
        else Anunciar("Diálogo de importação fechado.");
    }
    private TempoMidia? SolicitarDuracaoImagem()
    {
        TempoMidia? resultado = null;
        var painel = new StackPanel();
        var entrada = new TextBox { Text = "5", Margin = new Thickness(0, 8, 0, 12) };
        painel.Children.Add(new Label { Content = "_Duração de cada foto, em segundos", Target = entrada });
        painel.Children.Add(entrada);
        AutomationProperties.SetName(entrada, "Duração de cada foto em segundos");
        var erro = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(erro, AutomationLiveSetting.Assertive);
        painel.Children.Add(erro);
        var dialogo = CriarDialogo("Duração das fotos", painel);
        var confirmar = new Button { Content = "Adicionar à composição", IsDefault = true };
        confirmar.Click += (_, _) =>
        {
            if (decimal.TryParse(entrada.Text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.CurrentCulture, out var valor) && valor >= 0.001m && valor <= 86400)
            { resultado = new TempoMidia((long)decimal.Round(valor * 1000), 1000); dialogo.DialogResult = true; }
            else
            {
                erro.Text = "Informe uma duração entre 0,001 e 86400 segundos.";
                (UIElementAutomationPeer.FromElement(erro) ?? UIElementAutomationPeer.CreatePeerForElement(erro))?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                entrada.Focus(); entrada.SelectAll();
            }
        };
        painel.Children.Add(confirmar); AdicionarFechar(dialogo, painel, "Cancelar");
        dialogo.Loaded += (_, _) => { entrada.Focus(); entrada.SelectAll(); };
        dialogo.ShowDialog(); return resultado;
    }
    private void InformacoesPrevia(object sender, RoutedEventArgs e) => Anunciar("A prévia de áudio e vídeo ainda não está disponível nesta versão.");
    private void Sobre(object sender, RoutedEventArgs e)
    {
        var versao = typeof(App).Assembly.GetName().Version!;
        Informacoes("Sobre o sonarFrame", $"sonarFrame {versao.Major}.{versao.Minor}.{versao.Build}\nMontagem não destrutiva de projetos.\nReprodução e exportação não integram esta versão.");
    }
    private void InformacoesExportacao(object sender, RoutedEventArgs e) => Anunciar("A exportação de vídeo ainda não está disponível. Você já pode salvar o projeto.");
    private void Ajuda(object sender, RoutedEventArgs e) => Informacoes("Atalhos de teclado", """
        Tab / Shift+Tab: próximo / anterior controle.
        Alt+1: biblioteca. Alt+2: propriedades.
        Alt+3: navegação temporal. Alt+4: prévia.
        Alt+5: comandos do projeto.
        F6 / Shift+F6: alternar entre as cinco regiões.
        Ctrl+S: salvar. Escape: fechar diálogos e menus.
        Ctrl+Z / Ctrl+Y: desfazer / refazer edição.
        Ctrl+B: dividir no cursor (fora de campos de texto).

        No controle Navegação temporal:
        Setas: um quadro. Ctrl+setas: um segundo.
        Page Up/Down: dez segundos. Home/End: extremos.
        Shift com esses atalhos: selecionar intervalo.
        Delete: excluir o intervalo selecionado.

        Na lista: setas selecionam; Shift+F10 abre o menu.
        Na biblioteca: Ctrl e Shift permitem seleção múltipla.
        Ctrl+Tab alterna as guias de Adicionar.
        Escape durante a importação cancela o restante.

        O projeto é salvo automaticamente quando alterado.
        Reprodução e exportação não estão disponíveis.
        """);
}
