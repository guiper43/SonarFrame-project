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
    private readonly IRepositorioProjetos repositorio;
    private readonly ILeitorMetadadosMidia leitorMetadados;
    private readonly TimeProvider relogio;
    private readonly List<string> mensagensRecentes = [];
    private sealed record Linha(Guid Id, string Rotulo)
    {
        public override string ToString() => Rotulo;
    }
    public JanelaPrincipal(
        IRepositorioProjetos repositorio,
        ILeitorMetadadosMidia leitorMetadados,
        RepositorioConfiguracoes repositorioConfiguracoes,
        CatalogoProjetos catalogoProjetos,
        IFabricaReprodutorMidia fabricaPrevia,
        PreviaBiblioteca previa,
        IPreparadorComposicao preparadorComposicao,
        TimeProvider relogio)
    {
        ArgumentNullException.ThrowIfNull(repositorio);
        ArgumentNullException.ThrowIfNull(leitorMetadados);
        ArgumentNullException.ThrowIfNull(repositorioConfiguracoes);
        ArgumentNullException.ThrowIfNull(catalogoProjetos);
        ArgumentNullException.ThrowIfNull(fabricaPrevia);
        ArgumentNullException.ThrowIfNull(previa);
        ArgumentNullException.ThrowIfNull(preparadorComposicao);
        ArgumentNullException.ThrowIfNull(relogio);
        this.repositorio = repositorio;
        this.leitorMetadados = leitorMetadados;
        this.repositorioConfiguracoes = repositorioConfiguracoes;
        this.catalogoProjetos = catalogoProjetos;
        this.relogio = relogio;
        InitializeComponent();
        ConfigurarPrevia(previa, fabricaPrevia);
        ConfigurarComposicao(preparadorComposicao);
        ConfigurarCicloDeVida();
        Loaded += (_, _) => BotaoNovo.Focus();
    }
    private FrameworkElement[] Regioes => [RegiaoBiblioteca, RegiaoPropriedades, RegiaoLinhaDoTempo, RegiaoPrevia, RegiaoComandos];
    private Control[] Destinos => [BotaoAdicionar, Propriedades, LinhaDoTempo, BotaoReproduzirPrevia.IsEnabled ? BotaoReproduzirPrevia : BotaoPrevia, BotaoSalvar];
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
        contextoTemporalTimeline = false;
        caminhoProjeto = caminho;
        persistencia = new PersistenciaProjeto(sessao, repositorio, caminhoProjeto, relogio);
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
        var focoTimeline = FocusManager.GetFocusedElement(this) as DependencyObject;
        var preservarFocoTimeline = focoTimeline is not null && (focoTimeline == LinhaDoTempo || LinhaDoTempo.IsAncestorOf(focoTimeline));
        var idsBiblioteca = Biblioteca.SelectedItems.Cast<Linha>().Select(r => r.Id).ToHashSet();
        var linhasBiblioteca = p.Biblioteca.Select(a => new Linha(a.Id, $"{a.Nome} · {Tipo(a.Tipo)} · {a.Duracao?.ToString() ?? "definir duração na inserção"}")).ToArray();
        if (!Biblioteca.Items.Cast<Linha>().SequenceEqual(linhasBiblioteca))
        {
            Biblioteca.ItemsSource = linhasBiblioteca;
            foreach (var linha in linhasBiblioteca.Where(r => idsBiblioteca.Contains(r.Id))) Biblioteca.SelectedItems.Add(linha);
        }
        var linhasLinhaDoTempo = new AcoesEdicao(sessao).Conteudos().Select((c, i) => new Linha(c.Id, $"{c.Nome} · {Tipo(p.Biblioteca.Single(m => m.Id == c.MidiaId).Tipo)} · item {i + 1} · início no vídeo {c.Inicio} · fim {c.Inicio + c.Duracao} · duração {c.Duracao} · {(c.EhCamada ? "Camada" : "Sequência")}")).ToArray();
        var recriouTimeline = !LinhaDoTempo.Items.Cast<Linha>().SequenceEqual(linhasLinhaDoTempo);
        if (recriouTimeline) LinhaDoTempo.ItemsSource = linhasLinhaDoTempo;
        LinhaDoTempo.SelectedItem = LinhaDoTempo.Items.Cast<Linha>().FirstOrDefault(r => r.Id == p.ItemSelecionadoId);
        if (recriouTimeline && preservarFocoTimeline) FocarConteudoSelecionado();
        TituloProjeto.Text = p.Nome;
        Title = $"{p.Nome}{(sessao.TemAlteracoes ? " *" : "")} — sonarFrame";
        NavegadorTemporal.Text = $"Posição: {p.Cursor} · Duração: {p.Duracao}";
        ResumoSelecao.Text = p.Selecao is { } selecao ? $"Seleção: {selecao.Inicio} até {selecao.Fim}" : "Nenhum intervalo selecionado.";
        AutomationProperties.SetName(NavegadorTemporal, p.Selecao is null ? $"Posição {TempoFalado(p.Cursor)}" : $"Posição {p.Cursor}");
        BibliotecaVazia.Visibility = p.Biblioteca.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        LinhaDoTempoVazia.Visibility = p.LinhaDoTempo.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        BotaoInserir.IsEnabled = Biblioteca.SelectedItems.Count > 0;
        MenuDesfazer.IsEnabled = sessao.PodeDesfazer; MenuRefazer.IsEnabled = sessao.PodeRefazer;
        MenuExcluirSelecao.IsEnabled = p.Selecao is { } selecionado && selecionado.Fim > selecionado.Inicio;
        MenuDividir.IsEnabled = p.LinhaDoTempo.Length > 0;
        var conteudoSelecionado = new AcoesEdicao(sessao).Conteudos().SingleOrDefault(c => c.Id == p.ItemSelecionadoId);
        BotaoVolume.IsEnabled = !exibirPropriedadesBiblioteca && conteudoSelecionado is not null && p.Biblioteca.Single(a => a.Id == conteudoSelecionado.MidiaId).Tipo != TipoMidia.Imagem;
        BotaoEditarCamada.IsEnabled = BotaoRemoverCamada.IsEnabled = !exibirPropriedadesBiblioteca && conteudoSelecionado?.EhCamada == true;
        BotaoAdicionarCamada.IsEnabled = Biblioteca.SelectedItems.Count == 1 && p.Duracao > TempoMidia.Zero;
        BotaoRemoverMidia.IsEnabled = Biblioteca.SelectedItems.Count > 0;
        MenuRemoverComposicao.IsEnabled = conteudoSelecionado is not null;
        MenuMoverAntes.IsEnabled = MenuMoverDepois.IsEnabled = MenuMoverInicio.IsEnabled = MenuMoverFim.IsEnabled = conteudoSelecionado?.EhCamada == false;
        AtualizarControlesPrevia();
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
    private async Task<bool> ExecutarAsync(IComandoEdicao comando)
    {
        if (sessao is null || !await PrepararEdicaoAsync()) return false;
        try { sessao.Executar(comando); exibirPropriedadesBiblioteca = false; Atualizar(); return true; }
        catch (Exception ex) { Anunciar(ex.Message); return false; }
    }
    private void Inserir(object sender, RoutedEventArgs e)
        => UltimaEdicao = InserirNaPosicaoAsync(null);
    private void InserirAntes(object sender, RoutedEventArgs e) => UltimaEdicao = InserirAntesAsync();
    private async Task InserirAntesAsync()
    {
        if (sessao is null || LinhaDoTempo.SelectedItem is not Linha selecionado) { Anunciar("Selecione um clipe da sequência antes de inserir."); return; }
        var indice = sessao.Atual.LinhaDoTempo.Select((c, i) => (c, i)).Where(x => x.c.Id == selecionado.Id).Select(x => x.i).DefaultIfEmpty(-1).Single();
        if (indice < 0) { Anunciar("Selecione um clipe da sequência principal, não uma camada."); return; }
        await InserirNaPosicaoAsync(indice);
    }
    private async Task InserirNaPosicaoAsync(int? indice)
    {
        if (sessao is null || Biblioteca.SelectedItems.Count == 0) return;
        var ids = Biblioteca.Items.Cast<Linha>().Where(r => Biblioteca.SelectedItems.Contains(r)).Select(r => r.Id).ToImmutableArray();
        TempoMidia? duracao = null;
        if (sessao.Atual.Biblioteca.Any(a => ids.Contains(a.Id) && a.Tipo == TipoMidia.Imagem))
        {
            duracao = SolicitarDuracaoImagem();
            if (duracao is null) { BotaoInserir.Focus(); return; }
        }
        if (await ExecutarAsync(new InserirClipes(ids, duracao, indice))) Anunciar($"{ids.Length} conteúdo(s) adicionado(s) à linha do tempo.");
    }
    private void MidiaSelecionada(object sender, SelectionChangedEventArgs e)
    {
        if (atualizando || sessao is null) return;
        AtualizarControlesPrevia();
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
        contextoTemporalTimeline = false;
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
    private async Task MoverAsync(int destino)
    {
        if (sessao is null || LinhaDoTempo.SelectedItem is not Linha linha) { Anunciar("Selecione um conteúdo na lista da composição."); return; }
        if (!sessao.Atual.LinhaDoTempo.Any(c => c.Id == linha.Id)) { Anunciar("Use Editar camada para alterar a posição temporal de uma camada."); return; }
        destino = Math.Clamp(destino, 0, sessao.Atual.LinhaDoTempo.Length - 1);
        if (destino == IndiceClipeSelecionado) { Anunciar($"O conteúdo já está na posição {destino + 1}."); return; }
        if (!await ExecutarAsync(new MoverClipe(linha.Id, destino))) return;
        FocarConteudoSelecionado();
        Anunciar($"Conteúdo movido para a posição {destino + 1} de {sessao.Atual.LinhaDoTempo.Length}.");
    }
    private void MoverParaInicio(object sender, RoutedEventArgs e) => UltimaEdicao = MoverAsync(0);
    private void MoverParaFim(object sender, RoutedEventArgs e) => UltimaEdicao = MoverAsync((sessao?.Atual.LinhaDoTempo.Length ?? 1) - 1);
    private int IndiceClipeSelecionado => sessao is not null && LinhaDoTempo.SelectedItem is Linha linha
        ? Array.FindIndex(sessao.Atual.LinhaDoTempo.ToArray(), c => c.Id == linha.Id) : -1;
    private void MoverAntes(object sender, RoutedEventArgs e) => UltimaEdicao = MoverAsync(IndiceClipeSelecionado - 1);
    private void MoverDepois(object sender, RoutedEventArgs e) => UltimaEdicao = MoverAsync(IndiceClipeSelecionado + 1);
    private void Desfazer(object sender, RoutedEventArgs e) => UltimaEdicao = RestaurarEdicaoAsync(false);
    private void Refazer(object sender, RoutedEventArgs e) => UltimaEdicao = RestaurarEdicaoAsync(true);
    private async Task RestaurarEdicaoAsync(bool refazer)
    {
        if (sessao is null) return;
        if (!(refazer ? sessao.PodeRefazer : sessao.PodeDesfazer)) { Anunciar(refazer ? "Nenhuma edição para refazer." : "Nenhuma edição para desfazer."); return; }
        if (!await PrepararEdicaoAsync()) return;
        if (refazer) sessao.Refazer(); else sessao.Desfazer();
        Atualizar();
        Anunciar($"Edição {(refazer ? "refeita" : "desfeita")}. {sessao.Atual.LinhaDoTempo.Length} clipe(s) na sequência. Posição {sessao.Atual.Cursor}. Duração {sessao.Atual.Duracao}. {ResumoSelecao.Text}");
    }
    private void FocarConteudoSelecionado()
    {
        LinhaDoTempo.Focus();
        if (LinhaDoTempo.SelectedItem is not { } selecionado) return;
        LinhaDoTempo.ScrollIntoView(selecionado); LinhaDoTempo.UpdateLayout();
        if (LinhaDoTempo.ItemContainerGenerator.ContainerFromItem(selecionado) is ListBoxItem item) item.Focus();
    }
    private void LerPosicao(object sender, RoutedEventArgs e) => Anunciar(sessao is null ? "Nenhum projeto aberto." : sessao.Atual.Selecao is { } selecao
        ? $"Posição {sessao.Atual.Cursor}. Intervalo selecionado de {selecao.Inicio} até {selecao.Fim}. Duração {selecao.Fim - selecao.Inicio}."
        : $"Atualmente em {TempoFalado(sessao.Atual.Cursor)}. Nenhum intervalo selecionado.");
    private void TeclaNavegacaoTemporal(object sender, KeyEventArgs e)
    {
        if (sessao is null || e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            contextoTemporalTimeline = true;
            e.Handled = true;
            UltimaNavegacaoTemporal = NavegarTemporalAsync(e.Key, e.KeyboardDevice.Modifiers);
        }
        else if (e.Key == Key.Space) { e.Handled = true; AlternarComposicao(sender, e); }
        else if (e.Key == Key.Delete) { e.Handled = true; ExcluirIntervaloSelecionado(sender, e); }
    }
    private void AlternarAjudaInicial(object sender, RoutedEventArgs e)
    {
        var expandir = OpcoesAjudaInicial.Visibility != Visibility.Visible;
        OpcoesAjudaInicial.Visibility = expandir ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(BotaoAjudaInicial, expandir ? "Ajuda, opções expandidas" : "Ajuda, opções recolhidas");
        Anunciar(expandir ? "Ajuda expandida. Atalhos de teclado e Sobre o sonarFrame. Use Baixo para navegar." : "Ajuda recolhida.");
    }
    private void TeclaTelaInicial(object sender, KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers != ModifierKeys.None || e.Key is not (Key.Up or Key.Down) || e.OriginalSource is not Button origem) return;
        var botoes = TelaInicial.Children.OfType<Button>().Concat(OpcoesAjudaInicial.Children.OfType<Button>())
            .Where(b => b.IsEnabled && b.IsVisible).ToArray();
        var indice = Array.IndexOf(botoes, origem);
        if (indice < 0) return;
        var destino = botoes[Math.Clamp(indice + (e.Key == Key.Down ? 1 : -1), 0, botoes.Length - 1)];
        destino.Focus(); destino.BringIntoView(); e.Handled = true;
    }
    private void TeclaJanela(object sender, KeyEventArgs e)
    {
        var chave = e.Key == Key.System ? e.SystemKey : e.Key;
        if (chave == Key.F1) { Ajuda(sender, e); e.Handled = true; return; }
        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && chave == Key.S) { SalvarProjeto(sender, e); e.Handled = true; return; }
        if (Editor.Visibility != Visibility.Visible) return;
        if (e.OriginalSource is not TextBox && Keyboard.FocusedElement is not TextBox && e.KeyboardDevice.Modifiers == ModifierKeys.Control)
        {
            if (chave == Key.Z) { Desfazer(sender, e); e.Handled = true; return; }
            if (chave == Key.Y) { Refazer(sender, e); e.Handled = true; return; }
            if (chave == Key.B) { DividirNoCursor(sender, e); e.Handled = true; return; }
        }
        var indice = -1;
        if (e.KeyboardDevice.Modifiers == ModifierKeys.Alt && chave >= Key.D1 && chave <= Key.D5) indice = chave - Key.D1;
        if (chave == Key.F6)
        {
            var atual = Array.FindIndex(Regioes, r => r.IsKeyboardFocusWithin);
            var reverso = e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift);
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
        var dialogo = new DialogoImportacao(this, sessao, leitorMetadados);
        dialogo.ShowDialog(); Atualizar();
        if (Biblioteca.Items.Count > 0)
        {
            if (Biblioteca.SelectedIndex < 0) Biblioteca.SelectedIndex = Biblioteca.Items.Count - 1;
            Biblioteca.ScrollIntoView(Biblioteca.SelectedItem); Biblioteca.UpdateLayout();
            if (Biblioteca.ItemContainerGenerator.ContainerFromItem(Biblioteca.SelectedItem) is ListBoxItem item) item.Focus();
            else Biblioteca.Focus();
        }
        else BotaoAdicionar.Focus();
        foreach (var mensagem in sessao.Mensagens.Where(m => m.Codigo == "importacao.falhou"))
            if (!mensagensRecentes.Contains(mensagem.Mensagem)) mensagensRecentes.Insert(0, mensagem.Mensagem);
        if (dialogo.ImportouAlgumaMidia) Anunciar("Mídias adicionadas à biblioteca. Cima/Baixo seleciona arquivos, Enter abre a prévia, Tab acessa as ações e Delete remove da biblioteca.");
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
    private void InformacoesPrevia(object sender, RoutedEventArgs e) => Anunciar("Use Reproduzir ou Pausar na área do vídeo. Espaço na timeline reproduz a montagem. A prévia inclui camadas de áudio, vídeo e imagens. Consultar prévia informa a posição e as camadas ativas.");
    private void Sobre(object sender, RoutedEventArgs e)
    {
        var versao = typeof(App).Assembly.GetName().Version!;
        Informacoes("Sobre o sonarFrame", $"sonarFrame {versao.Major}.{versao.Minor}.{versao.Build}\nMontagem não destrutiva e prévia da biblioteca.\nReprodução da composição com camadas de áudio, vídeo e imagens disponível. A exportação fica para outra etapa.");
    }
    private void InformacoesExportacao(object sender, RoutedEventArgs e) => Anunciar("A exportação de vídeo ainda não está disponível. Você já pode salvar o projeto.");
    private void Ajuda(object sender, RoutedEventArgs e) => Informacoes("Atalhos de teclado", """
        Tab / Shift+Tab: percorrer controles e ações de cada área antes de passar à próxima.
        Cima / Baixo na biblioteca: selecionar arquivos sem sair da lista. Enter: abrir prévia. Delete: remover da biblioteca.
        Esquerda / Direita na linha do tempo: mover o cursor temporal.
        Alt+1: biblioteca. Alt+2: propriedades.
        Alt+3: timeline. Alt+4: prévia.
        Alt+5: comandos do projeto.
        F6 / Shift+F6: alternar entre as cinco regiões.
        Ctrl+S: salvar. Escape: fechar diálogos e menus.
        Ctrl+Z / Ctrl+Y: desfazer / refazer edição.
        Ctrl+B: dividir no cursor (fora de campos de texto).

        Na tela inicial, Ajuda fica no fim da lista.
        Enter / Espaço em Ajuda: expandir ou recolher as opções.
        As opções são Atalhos de teclado e Sobre o sonarFrame.
        Cima / Baixo ou Tab / Shift+Tab: navegar entre ações.
        Enter / Espaço: ativar a ação selecionada.

        Na barra superior do editor, cabeçalhos Projeto e Ajuda:
        Enter / Espaço: abrir ou recolher.
        Esquerda / Direita: trocar de menu sem abrir.
        Cima / Baixo: navegar nas opções quando aberto.
        Escape: fechar o menu.

        Na lista da timeline:
        Esquerda / Direita: um quadro. Ctrl+Esquerda / Direita: um segundo.
        Page Up/Down: dez segundos. Home/End: extremos.
        Shift com esses atalhos: selecionar intervalo.
        Delete após seleção temporal: excluir o intervalo. Após Cima/Baixo: abrir a ação de remover o item.

        Na timeline: Cima / Baixo seleciona elementos em ordem de início no vídeo; Enter vai ao início do elemento selecionado; Espaço reproduz ou pausa a composição; Tab acessa as ações. Esquerda / Direita move o tempo; Home vai ao início e End ao fim da composição; Shift+F10 abre o menu.
        Na biblioteca: Ctrl e Shift permitem seleção múltipla.
        Na importação, as quatro setas selecionam o tipo; Tab acessa Selecionar arquivos. Ao concluir, o foco fica em Fechar. Ctrl+Tab também alterna as guias.
        Escape durante a importação cancela o restante.

        O projeto é salvo automaticamente quando alterado.
        Na biblioteca, selecione uma mídia e acione Abrir prévia.
        Alt+4: reproduzir/pausar ou consultar prévia quando não temporal.
        Consultar prévia informa estado e posição, sem fala contínua.
        Use os botões Reproduzir/Pausar, Parar e Buscar e pausar.
        Espaço nos botões mantém sua ativação nativa.
        Imagens isoladas não oferecem controles temporais.
        Reproduzir e Pausar usam o mesmo botão na área do vídeo. Na timeline: Parar, Reproduzir intervalo e Cancelar preparação ou busca. Espaço na lista da composição alterna reprodução. Shift+F10, Aplicações ou botão direito abre consulta, divisão, exclusão, remoção, reordenação e histórico. Escape retorna à timeline. Camadas de áudio, vídeo e imagens são reproduzidas na composição. A exportação ainda não está disponível.
        """);
}
