using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Microsoft.Win32;

namespace SonarFrame.Desktop;

public partial class JanelaPrincipal
{
    private static readonly PosicaoVisual[] OrdemPosicoesVisuais =
    [
        PosicaoVisual.SuperiorEsquerda,
        PosicaoVisual.SuperiorCentro,
        PosicaoVisual.SuperiorDireita,
        PosicaoVisual.CentroEsquerda,
        PosicaoVisual.Centro,
        PosicaoVisual.CentroDireita,
        PosicaoVisual.InferiorEsquerda,
        PosicaoVisual.InferiorCentro,
        PosicaoVisual.InferiorDireita
    ];

    internal sealed record ResultadoPropriedadesVisuais(
        bool InserirNaSequencia,
        TempoMidia Inicio,
        TempoMidia InicioNaOrigem,
        TempoMidia Duracao,
        LayoutVisual Layout);

    internal sealed record DialogoPropriedadesVisuais(
        Window Dialogo,
        ListBox ModoExibicao,
        ListBox Posicao,
        ListBox Tamanho,
        TextBox EscalaPersonalizada,
        ListBox Ajuste,
        TextBox InicioComposicao,
        TextBox InicioOrigem,
        TextBox Duracao,
        TextBlock AvisoSobreposicao,
        TextBlock Erro,
        Button Confirmar,
        Func<ResultadoPropriedadesVisuais?> ObterResultado);

    private bool editandoPropriedadesVisuais;
    internal Task UltimaEdicao { get; private set; } = Task.CompletedTask;
    internal Func<ResultadoPropriedadesVisuais?>? SolicitarPropriedadesVisuaisOverride { get; set; }

    internal bool TinhaPreviaComposicaoVisivel =>
        composicaoAtiva && (QuadroDaComposicao.Visibility == Visibility.Visible || SuperficiePrevia.Visibility == Visibility.Visible);

    internal async Task RestaurarQuadroPausadoSeNecessarioAsync(bool tinhaComposicaoVisivel)
    {
        if (!tinhaComposicaoVisivel || sessao is null || sessao.Atual.LinhaDoTempo.IsEmpty || previaEncerrada) return;
        composicaoAtiva = true;
        destinoManualComposicao = sessao.Atual.Cursor;
        TituloPrevia.Text = "Prévia da composição pausada.";
        AtualizarControlesPrevia();
        AtualizarPosicaoPrevia();
        UltimoQuadro = ExibirQuadroAsync(sessao.Atual.Cursor);
        await UltimoQuadro;
    }

    internal static string DescreverPosicao(PosicaoVisual posicao) => posicao switch
    {
        PosicaoVisual.SuperiorEsquerda => "Superior à esquerda",
        PosicaoVisual.SuperiorCentro => "Superior ao centro",
        PosicaoVisual.SuperiorDireita => "Superior à direita",
        PosicaoVisual.CentroEsquerda => "Centro à esquerda",
        PosicaoVisual.Centro => "Centro",
        PosicaoVisual.CentroDireita => "Centro à direita",
        PosicaoVisual.InferiorEsquerda => "Inferior à esquerda",
        PosicaoVisual.InferiorCentro => "Inferior ao centro",
        PosicaoVisual.InferiorDireita => "Inferior à direita",
        _ => "Centro"
    };

    internal static string DescreverTamanho(int escala) => escala switch
    {
        25 => "Pequeno (limite de 25%)",
        50 => "Médio (50%)",
        75 => "Grande (75%)",
        _ => $"Personalizado ({escala}%)"
    };

    internal static string DescreverAjuste(AjusteVisual ajuste) => ajuste switch
    {
        AjusteVisual.PreencherInteiro => "Preencher por inteiro (corte proporcional)",
        _ => "Permitir bordas (imagem integral centralizada)"
    };

    private bool editandoFades;
    private bool editandoLegenda;
    internal sealed record DialogoConfiguracaoFades(
        Window Dialogo,
        TextBox? FadeInAudio,
        TextBox? FadeOutAudio,
        TextBox? FadeInVideo,
        TextBox? FadeOutVideo,
        TextBlock Erro,
        Button Confirmar,
        Func<(TempoMidia FadeInAudio, TempoMidia FadeOutAudio, TempoMidia FadeInVideo, TempoMidia FadeOutVideo)?> ObterResultado);
    internal sealed record DialogoEdicaoLegenda(
        Window Dialogo,
        TextBox Texto,
        ComboBox Fonte,
        TextBox Tamanho,
        ComboBox Cor,
        CheckBox Negrito,
        CheckBox Italico,
        CheckBox Fundo,
        ComboBox Posicao,
        ComboBox Escopo,
        TextBlock Erro,
        Button Confirmar,
        Func<(string Texto, EstiloLegenda Estilo, EscopoEstiloLegenda Escopo)?> ObterResultado);
    internal Func<ItemComposicao, TipoMidia, (TempoMidia FadeInAudio, TempoMidia FadeOutAudio, TempoMidia FadeInVideo, TempoMidia FadeOutVideo)?>? SolicitarFadesOverride { get; set; }
    internal Func<Legenda, (string Texto, EstiloLegenda Estilo, EscopoEstiloLegenda Escopo)?>? SolicitarEdicaoLegendaOverride { get; set; }
    internal Func<IReadOnlyList<Legenda>, Legenda?>? SolicitarLegendaAtivaOverride { get; set; }
    private void DescreverMidia(Guid id)
    {
        if (sessao is null) return;
        var midia = sessao.Atual.Biblioteca.Single(a => a.Id == id);
        Propriedades.Text = $"{midia.Nome}\nTipo: {Tipo(midia.Tipo)}\nDuração: {midia.Duracao?.ToString() ?? "definida ao inserir"}\nArquivo: {midia.Caminho}";
        if (!File.Exists(midia.Caminho)) Propriedades.Text += "\nArquivo ausente. Use Localizar arquivo na biblioteca.";
        if (midia.TaxaQuadrosVariavel) Propriedades.Text += "\nTaxa de quadros possivelmente variável; use navegação por segundos.";
    }

    private void DescreverConteudo(ItemComposicao item)
    {
        var tipo = sessao!.Atual.Biblioteca.Single(a => a.Id == item.MidiaId).Tipo;
        var texto = $"{item.Nome}\n{(item.EhCamada ? "Camada independente" : "Sequência principal")}\nInício na composição: {item.Inicio}\nDuração: {item.Duracao}";
        if (tipo != TipoMidia.Imagem)
        {
            texto += $"\nVolume: {item.Volume}%";
            texto += item.FadeInAudio > TempoMidia.Zero || item.FadeOutAudio > TempoMidia.Zero
                ? $"\nFades de áudio: fade-in {item.FadeInAudio}, fade-out {item.FadeOutAudio}"
                : "\nFades de áudio: nenhum";
        }
        if (tipo != TipoMidia.Audio)
        {
            texto += item.FadeInVideo > TempoMidia.Zero || item.FadeOutVideo > TempoMidia.Zero
                ? $"\nFades de vídeo: fade-in {item.FadeInVideo}, fade-out {item.FadeOutVideo}"
                : "\nFades de vídeo: nenhum";
        }
        if (new AcoesEdicao(sessao).LegendaAtiva() is { } legenda)
            texto += $"\nLegenda ativa ({legenda.Inicio} até {legenda.Fim}): {legenda.Texto.Replace('\n', ' ')}";
        if (tipo != TipoMidia.Audio)
        {
            var layout = item.LayoutEfetivo;
            if (item.EhCamada && layout.Modo == ModoExibicaoVisual.Sobrepor)
                texto += $"\nExibição visual: Sobrepor ao conteúdo visual\nPosição: {DescreverPosicao(layout.Posicao)}\nTamanho: {DescreverTamanho(layout.EscalaPercentual)}";
            else
                texto += $"\nExibição visual: {(item.EhCamada ? "Substituir a imagem do vídeo no intervalo" : "Substituição visual na sequência")}\nAjuste: {DescreverAjuste(layout.Ajuste)}";
            if (item.EhCamada)
            {
                var conflitos = new AcoesEdicao(sessao).ConflitosVisuais(item.Inicio, item.Duracao, layout, item.Id);
                if (!conflitos.IsEmpty)
                    texto += $"\nSobreposição simultânea na mesma região com: {string.Join(", ", conflitos)}";
            }
        }
        Propriedades.Text = texto;
    }

    private async Task AplicarAcaoAsync(Action<AcoesEdicao> acao, string mensagem)
    {
        if (sessao is null || !await PrepararEdicaoAsync()) return;
        try { acao(new AcoesEdicao(sessao)); exibirPropriedadesBiblioteca = false; Atualizar(); Anunciar(mensagem); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { Anunciar(e.Message); }
    }

    private void DividirNoCursor(object sender, RoutedEventArgs e) => UltimaEdicao = DividirNoCursorAsync();

    private async Task DividirNoCursorAsync()
    {
        if (sessao is null || !await PrepararEdicaoAsync()) return;
        try
        {
            var posicao = sessao.Atual.Cursor;
            var nome = new AcoesEdicao(sessao).Conteudos().FirstOrDefault(c => !c.EhCamada && c.Inicio < posicao && c.Inicio + c.Duracao > posicao)?.Nome;
            new AcoesEdicao(sessao).DividirNoCursor();
            exibirPropriedadesBiblioteca = false; Atualizar();
            Anunciar($"Corte realizado em {posicao}. {nome} dividido em Parte 1 e Parte 2.");
        }
        catch (Exception erro) when (erro is ArgumentException or InvalidOperationException) { Anunciar(erro.Message); }
    }

    private void ExcluirIntervaloSelecionado(object sender, RoutedEventArgs e) => UltimaEdicao = ExcluirIntervaloSelecionadoAsync();

    private async Task ExcluirIntervaloSelecionadoAsync()
    {
        if (sessao is null || !await PrepararEdicaoAsync()) return;
        try
        {
            var selecao = sessao.Atual.Selecao;
            new AcoesEdicao(sessao).ExcluirSelecao();
            exibirPropriedadesBiblioteca = false; Atualizar();
            FocarConteudoSelecionado();
            Anunciar($"Intervalo de {selecao!.Inicio} até {selecao.Fim} excluído e lacuna fechada. Posição {sessao.Atual.Cursor}. Duração da composição {sessao.Atual.Duracao}.");
        }
        catch (Exception erro) when (erro is ArgumentException or InvalidOperationException) { Anunciar(erro.Message); }
    }

    private async void AlterarVolume(object sender, RoutedEventArgs e)
    {
        if (sessao is null) return;
        try
        {
            var selecionado = new AcoesEdicao(sessao).Selecionado();
            var valores = SolicitarNumeros("Volume do conteúdo", [("Volume, em porcentagem", (decimal)selecionado.Volume, 0m, 200m, true)]);
            if (valores is not null) await AplicarAcaoAsync(a => a.DefinirVolumeSelecionado((int)valores[0]), $"Volume definido para {valores[0]}%.");
        }
        catch (InvalidOperationException ex) { Anunciar(ex.Message); }
        BotaoVolume.Focus();
    }
    private void ConfigurarFades(object sender, RoutedEventArgs e) => UltimaEdicao = ConfigurarFadesAsync(BotaoFades);
    private void ConfigurarFadesMenu(object sender, RoutedEventArgs e) => UltimaEdicao = ConfigurarFadesAsync(LinhaDoTempo);
    private void RemoverFades(object sender, RoutedEventArgs e) => UltimaEdicao = RemoverFadesAsync(BotaoFades);
    private void RemoverFadesMenu(object sender, RoutedEventArgs e) => UltimaEdicao = RemoverFadesAsync(LinhaDoTempo);
    internal async Task ConfigurarFadesAsync(UIElement destinoFoco)
    {
        if (sessao is null || Ocupado || editandoFades) return;
        editandoFades = true;
        try
        {
            if (!await PrepararEdicaoAsync()) return;
            var selecionado = new AcoesEdicao(sessao).Selecionado();
            var midia = sessao.Atual.Biblioteca.Single(a => a.Id == selecionado.MidiaId);
            var valores = SolicitarFadesOverride is { } overrideFades
                ? overrideFades(selecionado, midia.Tipo)
                : SolicitarFades(selecionado, midia.Tipo);
            if (valores is { } f)
            {
                var mensagem = ResumoFadesAnuncio(selecionado.Nome, midia.Tipo, f.FadeInAudio, f.FadeOutAudio, f.FadeInVideo, f.FadeOutVideo);
                await AplicarAcaoAsync(a => a.DefinirFadesSelecionado(f.FadeInAudio, f.FadeOutAudio, f.FadeInVideo, f.FadeOutVideo), mensagem);
            }
        }
        catch (InvalidOperationException ex) { Anunciar(ex.Message); }
        finally { editandoFades = false; }
        if (ReferenceEquals(destinoFoco, LinhaDoTempo)) FocarConteudoSelecionado();
        else RestaurarFocoAposDialogo(destinoFoco, BotaoFades);
    }
    internal async Task RemoverFadesAsync(UIElement destinoFoco)
    {
        if (sessao is null || Ocupado || editandoFades) return;
        editandoFades = true;
        try
        {
            var selecionado = new AcoesEdicao(sessao).Selecionado();
            await AplicarAcaoAsync(a => a.RemoverFadesSelecionado(), $"Fades removidos de {selecionado.Nome}.");
        }
        catch (InvalidOperationException ex) { Anunciar(ex.Message); }
        finally { editandoFades = false; }
        if (ReferenceEquals(destinoFoco, LinhaDoTempo)) FocarConteudoSelecionado();
        else RestaurarFocoAposDialogo(BotaoFades, LinhaDoTempo);
    }
    private static string ResumoFadesAnuncio(string nome, TipoMidia tipo, TempoMidia inAudio, TempoMidia outAudio, TempoMidia inVideo, TempoMidia outVideo)
    {
        if (inAudio == TempoMidia.Zero && outAudio == TempoMidia.Zero && inVideo == TempoMidia.Zero && outVideo == TempoMidia.Zero)
            return $"Fades removidos de {nome}.";
        var partes = new List<string>();
        if (tipo != TipoMidia.Imagem) partes.Add($"áudio (fade-in {inAudio}, fade-out {outAudio})");
        if (tipo != TipoMidia.Audio) partes.Add($"vídeo (fade-in {inVideo}, fade-out {outVideo})");
        return $"Fades atualizados em {nome}: {string.Join("; ", partes)}.";
    }
    private (TempoMidia FadeInAudio, TempoMidia FadeOutAudio, TempoMidia FadeInVideo, TempoMidia FadeOutVideo)? SolicitarFades(ItemComposicao item, TipoMidia tipo)
    {
        var configuracao = CriarDialogoFades(item, tipo);
        configuracao.Dialogo.ShowDialog();
        return configuracao.ObterResultado();
    }
    internal DialogoConfiguracaoFades CriarDialogoFades(ItemComposicao item, TipoMidia tipo)
    {
        (TempoMidia FadeInAudio, TempoMidia FadeOutAudio, TempoMidia FadeInVideo, TempoMidia FadeOutVideo)? resultado = null;
        var duracaoMax = decimal.Round(Segundos(item.Duracao), 6);
        var duracaoTexto = duracaoMax.ToString("0.######", CultureInfo.CurrentCulture);
        var painel = new StackPanel();
        painel.Children.Add(new TextBlock
        {
            Text = $"Informe a duração em segundos (0 remove o efeito). Duração do conteúdo: {duracaoTexto} s.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        (TextBox Caixa, string TextoInicial, TempoMidia ValorOriginal) CriarCampo(string rotulo, string nomeAcessivel, TempoMidia valor)
        {
            var inicial = decimal.Round(Segundos(valor), 6).ToString("0.######", CultureInfo.CurrentCulture);
            var caixa = new TextBox { Text = inicial, Margin = new Thickness(0, 4, 0, 12) };
            painel.Children.Add(new Label { Content = rotulo, Target = caixa });
            AutomationProperties.SetName(caixa, nomeAcessivel);
            painel.Children.Add(caixa);
            return (caixa, inicial, valor);
        }
        (TextBox Caixa, string TextoInicial, TempoMidia ValorOriginal)? fadeInAudio = null, fadeOutAudio = null, fadeInVideo = null, fadeOutVideo = null;
        if (tipo != TipoMidia.Imagem)
        {
            fadeInAudio = CriarCampo("Fade-_in de áudio, em segundos", "Fade-in de áudio, em segundos", item.FadeInAudio);
            fadeOutAudio = CriarCampo("Fade-_out de áudio, em segundos", "Fade-out de áudio, em segundos", item.FadeOutAudio);
        }
        if (tipo != TipoMidia.Audio)
        {
            fadeInVideo = CriarCampo("Fade-in de _vídeo, em segundos", "Fade-in de vídeo, em segundos", item.FadeInVideo);
            fadeOutVideo = CriarCampo("Fade-out de ví_deo, em segundos", "Fade-out de vídeo, em segundos", item.FadeOutVideo);
        }
        var erro = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetLiveSetting(erro, AutomationLiveSetting.Assertive);
        painel.Children.Add(erro);
        var dialogo = CriarDialogo("Fades do conteúdo", painel);
        var confirmar = new Button { Content = "Aplicar", IsDefault = true };
        void InformarErro(string mensagem, TextBox destino)
        {
            erro.Text = mensagem;
            (UIElementAutomationPeer.FromElement(erro) ?? UIElementAutomationPeer.CreatePeerForElement(erro))
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            destino.Focus();
            destino.SelectAll();
        }
        bool TentarLer((TextBox Caixa, string TextoInicial, TempoMidia ValorOriginal)? campo, string nome, out TempoMidia tempo)
        {
            tempo = TempoMidia.Zero;
            if (campo is not { } c) return true;
            if (string.Equals(c.Caixa.Text.Trim(), c.TextoInicial, StringComparison.Ordinal) && c.ValorOriginal <= item.Duracao)
            {
                tempo = c.ValorOriginal;
                return true;
            }
            if (!decimal.TryParse(c.Caixa.Text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.CurrentCulture, out var valor)
                || valor < 0m || valor > duracaoMax)
            {
                InformarErro($"{nome}: informe um número entre 0 e {duracaoTexto}.", c.Caixa);
                return false;
            }
            tempo = TempoMidia.Minimo(item.Duracao, Tempo(valor));
            return true;
        }
        confirmar.Click += (_, _) =>
        {
            if (!TentarLer(fadeInAudio, "Fade-in de áudio, em segundos", out var inAudio)) return;
            if (!TentarLer(fadeOutAudio, "Fade-out de áudio, em segundos", out var outAudio)) return;
            if (inAudio + outAudio > item.Duracao)
            {
                InformarErro($"A soma de fade-in e fade-out de áudio não pode ultrapassar a duração do conteúdo ({duracaoTexto} s).", fadeOutAudio!.Value.Caixa);
                return;
            }
            if (!TentarLer(fadeInVideo, "Fade-in de vídeo, em segundos", out var inVideo)) return;
            if (!TentarLer(fadeOutVideo, "Fade-out de vídeo, em segundos", out var outVideo)) return;
            if (inVideo + outVideo > item.Duracao)
            {
                InformarErro($"A soma de fade-in e fade-out de vídeo não pode ultrapassar a duração do conteúdo ({duracaoTexto} s).", fadeOutVideo!.Value.Caixa);
                return;
            }
            resultado = (inAudio, outAudio, inVideo, outVideo);
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) dialogo.DialogResult = true;
            else dialogo.Close();
        };
        painel.Children.Add(confirmar);
        AdicionarFechar(dialogo, painel, "Cancelar");
        var primeiroCampo = fadeInAudio?.Caixa ?? fadeInVideo!.Value.Caixa;
        dialogo.Loaded += (_, _) => { primeiroCampo.Focus(); primeiroCampo.SelectAll(); };
        return new(dialogo, fadeInAudio?.Caixa, fadeOutAudio?.Caixa, fadeInVideo?.Caixa, fadeOutVideo?.Caixa, erro, confirmar, () => resultado);
    }
    private void EditarLegendaPropriedades(object sender, RoutedEventArgs e) => UltimaEdicao = EditarLegendaAsync(BotaoEditarLegenda);
    private void EditarLegendaMenu(object sender, RoutedEventArgs e) => UltimaEdicao = EditarLegendaAsync(LinhaDoTempo);
    internal async Task EditarLegendaAsync(UIElement destinoFoco)
    {
        if (sessao is null || Ocupado || editandoLegenda) return;
        editandoLegenda = true;
        try
        {
            var tinhaComposicao = composicaoAtiva && !sessao.Atual.LinhaDoTempo.IsEmpty;
            if (!await PrepararEdicaoAsync()) return;
            var cursorAnterior = sessao.Atual.Cursor;
            var legendas = new AcoesEdicao(sessao).LegendasAtivas();
            if (legendas.Count == 0)
            {
                Anunciar("Posicione o cursor sobre um trecho com legenda ativa e mantenha a prévia pausada para editar a legenda.");
                return;
            }
            var legenda = legendas.Count == 1 ? legendas[0]
                : SolicitarLegendaAtivaOverride is { } escolherLegenda ? escolherLegenda(legendas) : SolicitarLegendaAtiva(legendas);
            var edicao = legenda is null ? null : SolicitarEdicaoLegendaOverride is { } overrideLegenda
                ? overrideLegenda(legenda)
                : SolicitarEdicaoLegenda(legenda);
            if (edicao is { } dados && legenda is not null)
            {
                var escopoTexto = dados.Escopo == EscopoEstiloLegenda.TodasDoProjeto ? "estilo aplicado a todas as legendas do projeto" : "estilo aplicado à legenda atual";
                await AplicarAcaoAsync(a => a.EditarLegenda(legenda.Id, dados.Texto, dados.Estilo, dados.Escopo),
                    $"Legenda atualizada ({escopoTexto}). Posição {cursorAnterior} preservada.");
                if (sessao.Atual.Cursor != cursorAnterior) sessao.Posicionar(cursorAnterior);
                Atualizar();
            }
            else Anunciar($"Edição de legenda cancelada. Posição {cursorAnterior} preservada.");
            if (tinhaComposicao && !sessao.Atual.LinhaDoTempo.IsEmpty)
            {
                composicaoAtiva = true;
                destinoManualComposicao = sessao.Atual.Cursor;
                UltimoQuadro = ExibirQuadroAsync(sessao.Atual.Cursor);
                await UltimoQuadro;
                AtualizarPosicaoPrevia();
            }
        }
        finally
        {
            editandoLegenda = false;
            if (ReferenceEquals(destinoFoco, LinhaDoTempo)) FocarConteudoSelecionado();
            else RestaurarFocoAposDialogo(destinoFoco, LinhaDoTempo);
        }
    }
    private Legenda? SolicitarLegendaAtiva(IReadOnlyList<Legenda> legendas)
    {
        var escolha = CriarDialogoEscolhaLegenda(legendas);
        escolha.Dialogo.ShowDialog();
        return escolha.ObterResultado();
    }
    internal (Window Dialogo, ListBox Lista, Button Confirmar, Func<Legenda?> ObterResultado) CriarDialogoEscolhaLegenda(IReadOnlyList<Legenda> legendas)
    {
        Legenda? resultado = null;
        var painel = new StackPanel();
        var lista = new ListBox
        {
            ItemsSource = legendas.Select((l, i) => $"Legenda {i + 1}: {l.Texto.Replace('\n', ' ')}. De {l.Inicio} até {l.Fim}.").ToArray(),
            SelectedIndex = 0,
            MaxHeight = 180,
            Margin = new Thickness(0, 4, 0, 12)
        };
        painel.Children.Add(new Label { Content = "_Legendas ativas nesta posição", Target = lista });
        AutomationProperties.SetName(lista, "Legendas ativas nesta posição");
        AutomationProperties.SetHelpText(lista, "Use Cima e Baixo para escolher a legenda e Enter para editar.");
        painel.Children.Add(lista);
        var dialogo = CriarDialogo("Escolher legenda para editar", painel);
        var confirmar = new Button { Content = "_Editar legenda", IsDefault = true };
        confirmar.Click += (_, _) =>
        {
            if (lista.SelectedIndex < 0) return;
            resultado = legendas[lista.SelectedIndex];
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) dialogo.DialogResult = true;
            else dialogo.Close();
        };
        painel.Children.Add(confirmar);
        AdicionarFechar(dialogo, painel, "Cancelar");
        dialogo.Loaded += (_, _) =>
        {
            lista.UpdateLayout();
            if (lista.ItemContainerGenerator.ContainerFromIndex(lista.SelectedIndex) is ListBoxItem item) item.Focus();
            else lista.Focus();
        };
        return (dialogo, lista, confirmar, () => resultado);
    }
    private (string Texto, EstiloLegenda Estilo, EscopoEstiloLegenda Escopo)? SolicitarEdicaoLegenda(Legenda legenda)
    {
        var dialogo = CriarDialogoLegenda(legenda);
        dialogo.Dialogo.ShowDialog();
        return dialogo.ObterResultado();
    }
    internal DialogoEdicaoLegenda CriarDialogoLegenda(Legenda legenda)
    {
        (string Texto, EstiloLegenda Estilo, EscopoEstiloLegenda Escopo)? resultado = null;
        var painel = new StackPanel();
        painel.Children.Add(new TextBlock
        {
            Text = $"Intervalo da legenda: {legenda.Inicio} até {legenda.Fim}.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var caixaTexto = new TextBox
        {
            Text = legenda.Texto,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12)
        };
        painel.Children.Add(new Label { Content = "_Texto da legenda", Target = caixaTexto });
        AutomationProperties.SetName(caixaTexto, "Texto da legenda");
        painel.Children.Add(caixaTexto);

        var fontes = new List<string> { "Arial", "Segoe UI", "Verdana", "Tahoma", "Times New Roman", "Courier New", "Consolas" };
        if (!fontes.Contains(legenda.Estilo.Fonte, StringComparer.OrdinalIgnoreCase)) fontes.Insert(0, legenda.Estilo.Fonte);
        var comboFonte = new ComboBox { ItemsSource = fontes, SelectedItem = fontes.First(f => string.Equals(f, legenda.Estilo.Fonte, StringComparison.OrdinalIgnoreCase)), Margin = new Thickness(0, 4, 0, 12) };
        painel.Children.Add(new Label { Content = "_Fonte", Target = comboFonte });
        AutomationProperties.SetName(comboFonte, "Fonte da legenda");
        painel.Children.Add(comboFonte);

        var caixaTamanho = new TextBox { Text = legenda.Estilo.Tamanho.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 4, 0, 12) };
        painel.Children.Add(new Label { Content = "Ta_manho da fonte (8 a 120)", Target = caixaTamanho });
        AutomationProperties.SetName(caixaTamanho, "Tamanho da fonte, entre 8 e 120");
        painel.Children.Add(caixaTamanho);

        var cores = new List<(string Nome, string Hex)>
        {
            ("Branco (#FFFFFF)", "#FFFFFF"),
            ("Amarelo (#FFFF00)", "#FFFF00"),
            ("Ciano (#00FFFF)", "#00FFFF"),
            ("Verde (#00FF00)", "#00FF00"),
            ("Preto (#000000)", "#000000")
        };
        if (!cores.Any(c => string.Equals(c.Hex, legenda.Estilo.Cor, StringComparison.OrdinalIgnoreCase)))
            cores.Insert(0, ($"Personalizada ({legenda.Estilo.Cor.ToUpperInvariant()})", legenda.Estilo.Cor.ToUpperInvariant()));
        var comboCor = new ComboBox
        {
            ItemsSource = cores.Select(c => c.Nome).ToArray(),
            SelectedIndex = Math.Max(0, cores.FindIndex(c => string.Equals(c.Hex, legenda.Estilo.Cor, StringComparison.OrdinalIgnoreCase))),
            Margin = new Thickness(0, 4, 0, 12)
        };
        painel.Children.Add(new Label { Content = "_Cor do texto", Target = comboCor });
        AutomationProperties.SetName(comboCor, "Cor do texto da legenda");
        painel.Children.Add(comboCor);

        var checkNegrito = new CheckBox { Content = "_Negrito", IsChecked = legenda.Estilo.Negrito, Margin = new Thickness(0, 0, 16, 8) };
        AutomationProperties.SetName(checkNegrito, "Negrito");
        var checkItalico = new CheckBox { Content = "_Itálico", IsChecked = legenda.Estilo.Italico, Margin = new Thickness(0, 0, 16, 8) };
        AutomationProperties.SetName(checkItalico, "Itálico");
        var checkFundo = new CheckBox { Content = "Exibir _fundo na legenda", IsChecked = legenda.Estilo.Fundo, Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(checkFundo, "Exibir fundo na legenda");
        painel.Children.Add(checkNegrito);
        painel.Children.Add(checkItalico);
        painel.Children.Add(checkFundo);

        var posicoes = new (PosicaoVisual Valor, string Rotulo)[]
        {
            (PosicaoVisual.SuperiorEsquerda, "Superior esquerda"),
            (PosicaoVisual.SuperiorCentro, "Superior centro"),
            (PosicaoVisual.SuperiorDireita, "Superior direita"),
            (PosicaoVisual.CentroEsquerda, "Centro esquerda"),
            (PosicaoVisual.Centro, "Centro"),
            (PosicaoVisual.CentroDireita, "Centro direita"),
            (PosicaoVisual.InferiorEsquerda, "Inferior esquerda"),
            (PosicaoVisual.InferiorCentro, "Inferior centro (padrão)"),
            (PosicaoVisual.InferiorDireita, "Inferior direita")
        };
        var comboPosicao = new ComboBox
        {
            ItemsSource = posicoes.Select(p => p.Rotulo).ToArray(),
            SelectedIndex = Math.Max(0, Array.FindIndex(posicoes, p => p.Valor == legenda.Estilo.Posicao)),
            Margin = new Thickness(0, 4, 0, 12)
        };
        painel.Children.Add(new Label { Content = "_Posicionamento visual", Target = comboPosicao });
        AutomationProperties.SetName(comboPosicao, "Posicionamento visual da legenda");
        painel.Children.Add(comboPosicao);

        var escopos = new (EscopoEstiloLegenda Valor, string Rotulo)[]
        {
            (EscopoEstiloLegenda.ApenasSelecionada, "Apenas esta legenda"),
            (EscopoEstiloLegenda.TodasDoProjeto, "Todas as legendas do projeto")
        };
        var comboEscopo = new ComboBox
        {
            ItemsSource = escopos.Select(e => e.Rotulo).ToArray(),
            SelectedIndex = 0,
            Margin = new Thickness(0, 4, 0, 12)
        };
        painel.Children.Add(new Label { Content = "Aplicar formatação _visual a", Target = comboEscopo });
        AutomationProperties.SetName(comboEscopo, "Aplicar formatação visual a");
        painel.Children.Add(comboEscopo);

        var erro = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetLiveSetting(erro, AutomationLiveSetting.Assertive);
        painel.Children.Add(erro);

        var dialogo = CriarDialogo("Editar legenda", painel);
        var confirmar = new Button { Content = "Aplicar", IsDefault = true };
        void InformarErro(string mensagem, TextBox destino)
        {
            erro.Text = mensagem;
            (UIElementAutomationPeer.FromElement(erro) ?? UIElementAutomationPeer.CreatePeerForElement(erro))
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            destino.Focus();
            destino.SelectAll();
        }
        confirmar.Click += (_, _) =>
        {
            var texto = caixaTexto.Text.Trim();
            if (string.IsNullOrWhiteSpace(texto))
            {
                InformarErro("Informe o texto da legenda.", caixaTexto);
                return;
            }
            if (!int.TryParse(caixaTamanho.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tamanho) || tamanho is < 8 or > 120)
            {
                InformarErro("Tamanho da fonte: informe um número inteiro entre 8 e 120.", caixaTamanho);
                return;
            }
            var fonte = comboFonte.SelectedItem as string ?? "Arial";
            var cor = cores[Math.Clamp(comboCor.SelectedIndex, 0, cores.Count - 1)].Hex;
            var posicao = posicoes[Math.Clamp(comboPosicao.SelectedIndex, 0, posicoes.Length - 1)].Valor;
            var escopo = escopos[Math.Clamp(comboEscopo.SelectedIndex, 0, escopos.Length - 1)].Valor;
            var estilo = new EstiloLegenda(fonte, tamanho, cor, checkNegrito.IsChecked == true, checkItalico.IsChecked == true, checkFundo.IsChecked == true, posicao);
            resultado = (texto, estilo, escopo);
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) dialogo.DialogResult = true;
            else dialogo.Close();
        };
        painel.Children.Add(confirmar);
        AdicionarFechar(dialogo, painel, "Cancelar");
        dialogo.Loaded += (_, _) => { caixaTexto.Focus(); caixaTexto.SelectAll(); };
        return new(dialogo, caixaTexto, comboFonte, caixaTamanho, comboCor, checkNegrito, checkItalico, checkFundo, comboPosicao, comboEscopo, erro, confirmar, () => resultado);
    }
    private static decimal Segundos(TempoMidia tempo) => (decimal)tempo.Numerador / tempo.Denominador;
    private static TempoMidia Tempo(decimal segundos) => new((long)decimal.Round(segundos * 1_000_000), 1_000_000);

    private (TempoMidia Inicio, TempoMidia Disponivel, TempoMidia DuracaoPadrao) IntervaloPadraoInsercao(TempoMidia? duracaoMidia)
    {
        if (sessao is null) return (TempoMidia.Zero, TempoMidia.Zero, TempoMidia.Segundos(5));
        var selecaoAtiva = sessao.Atual.Selecao is { } sel && sel.Fim > sel.Inicio ? sel : null;
        var inicio = selecaoAtiva?.Inicio ?? sessao.Atual.Cursor;
        var disponivel = sessao.Atual.Duracao > inicio ? sessao.Atual.Duracao - inicio : TempoMidia.Zero;
        var baseDuracao = selecaoAtiva is not null ? selecaoAtiva.Fim - selecaoAtiva.Inicio : (duracaoMidia ?? TempoMidia.Segundos(5));
        var duracao = disponivel > TempoMidia.Zero ? TempoMidia.Minimo(baseDuracao, disponivel) : baseDuracao;
        return (inicio, disponivel, duracao);
    }

    private ResultadoPropriedadesVisuais? SolicitarInsercaoImagem(Midia midia, bool permitirCamadaNoIntervalo)
    {
        if (SolicitarPropriedadesVisuaisOverride is { } overrideResultado) return overrideResultado();
        var selecaoAtiva = sessao?.Atual.Selecao is { } sel && sel.Fim > sel.Inicio ? sel : null;
        var duracaoPadraoSequencia = selecaoAtiva is not null
            ? selecaoAtiva.Fim - selecaoAtiva.Inicio
            : (midia.Duracao ?? TempoMidia.Segundos(5));
        var (inicio, disponivel, duracao) = IntervaloPadraoInsercao(midia.Duracao);
        if (!permitirCamadaNoIntervalo)
        {
            duracao = duracaoPadraoSequencia;
        }
        else if (sessao is not null && sessao.Atual.Duracao > TempoMidia.Zero && disponivel <= TempoMidia.Zero)
        {
            inicio = TempoMidia.Zero;
            duracao = TempoMidia.Minimo(midia.Duracao ?? TempoMidia.Segundos(5), sessao.Atual.Duracao);
        }
        var layoutInicial = permitirCamadaNoIntervalo ? LayoutVisual.PadraoCamada : LayoutVisual.PadraoSequencia;
        var configuracao = CriarDialogoPropriedadesVisuais(
            "Inserir imagem",
            "Adicionar à composição",
            midia,
            ehCamada: permitirCamadaNoIntervalo,
            permitirOpcaoSequencia: permitirCamadaNoIntervalo,
            inicioInicial: inicio,
            origemInicial: TempoMidia.Zero,
            duracaoInicial: duracao,
            layoutInicial: layoutInicial,
            duracaoSequenciaInicial: duracaoPadraoSequencia);
        configuracao.Dialogo.ShowDialog();
        return configuracao.ObterResultado();
    }

    private void AdicionarCamadaIndependente(object sender, RoutedEventArgs e)
        => UltimaEdicao = AdicionarCamadaIndependenteAsync();

    internal async Task AdicionarCamadaIndependenteAsync()
    {
        if (editandoPropriedadesVisuais) return;
        if (sessao is null || Biblioteca.SelectedItems.Count != 1) { Anunciar("Selecione uma mídia da biblioteca."); return; }
        var midia = sessao.Atual.Biblioteca.Single(a => a.Id == ((Linha)Biblioteca.SelectedItem).Id);
        var (inicioPadrao, disponivel, duracaoPadrao) = IntervaloPadraoInsercao(midia.Duracao);
        if (disponivel <= TempoMidia.Zero) { Anunciar("Posicione o cursor antes do fim da composição para adicionar uma camada."); return; }
        editandoPropriedadesVisuais = true;
        var tinhaComposicaoVisivel = TinhaPreviaComposicaoVisivel;
        try
        {
            if (!await PrepararEdicaoAsync()) return;
            if (midia.Tipo == TipoMidia.Audio)
            {
                var valores = SolicitarValoresCamada(inicioPadrao, TempoMidia.Zero, duracaoPadrao, midia.Duracao);
                if (valores is { } v && await ExecutarAsync(new AdicionarCamada(midia.Id, v.Inicio, v.Origem, v.Duracao)))
                    Anunciar("Camada adicionada. A duração da sequência principal foi preservada.");
                BotaoAdicionarCamada.Focus();
                await RestaurarQuadroPausadoSeNecessarioAsync(tinhaComposicaoVisivel);
                return;
            }
            ResultadoPropriedadesVisuais? resultado;
            if (SolicitarPropriedadesVisuaisOverride is { } overrideResultado)
                resultado = overrideResultado();
            else
            {
                var dialogo = CriarDialogoPropriedadesVisuais(
                    "Adicionar camada visual",
                    "Adicionar camada",
                    midia,
                    ehCamada: true,
                    permitirOpcaoSequencia: false,
                    inicioInicial: inicioPadrao,
                    origemInicial: TempoMidia.Zero,
                    duracaoInicial: duracaoPadrao,
                    layoutInicial: LayoutVisual.PadraoCamada);
                dialogo.Dialogo.ShowDialog();
                resultado = dialogo.ObterResultado();
            }
            if (resultado is not null)
            {
                var conflitos = new AcoesEdicao(sessao).ConflitosVisuais(resultado.Inicio, resultado.Duracao, resultado.Layout);
                if (await ExecutarAsync(new AdicionarCamada(midia.Id, resultado.Inicio, resultado.InicioNaOrigem, resultado.Duracao, resultado.Layout)))
                {
                    var aviso = conflitos.IsEmpty ? "" : $" Aviso: há sobreposição simultânea na mesma região com {string.Join(", ", conflitos)}.";
                    Anunciar("Camada adicionada. A duração da sequência principal foi preservada." + aviso);
                }
            }
            BotaoAdicionarCamada.Focus();
            await RestaurarQuadroPausadoSeNecessarioAsync(tinhaComposicaoVisivel);
        }
        finally { editandoPropriedadesVisuais = false; }
    }

    private (TempoMidia Inicio, TempoMidia Origem, TempoMidia Duracao)? SolicitarValoresCamada(TempoMidia inicio, TempoMidia origem, TempoMidia duracao, TempoMidia? duracaoMidia)
    {
        var textoInicio = Segundos(inicio).ToString("0.######", CultureInfo.CurrentCulture);
        var textoOrigem = Segundos(origem).ToString("0.######", CultureInfo.CurrentCulture);
        var textoDuracao = Segundos(duracao).ToString("0.######", CultureInfo.CurrentCulture);
        var valores = SolicitarNumeros("Intervalo da camada",
            [("Início na composição, em segundos", Segundos(inicio), 0m, 864000m, false),
             ("Início no arquivo original, em segundos", Segundos(origem), 0m, 864000m, false),
             ("Duração, em segundos", Segundos(duracao), 0.000001m, 864000m, false)]);
        if (valores is null) return null;
        var iniFinal = valores[0].ToString("0.######", CultureInfo.CurrentCulture) == textoInicio ? inicio : Tempo(valores[0]);
        var oriFinal = valores[1].ToString("0.######", CultureInfo.CurrentCulture) == textoOrigem ? origem : Tempo(valores[1]);
        var durFinal = valores[2].ToString("0.######", CultureInfo.CurrentCulture) == textoDuracao ? duracao : Tempo(valores[2]);
        var tolerancia = new TempoMidia(1, 1_000_000);
        if (sessao is not null && iniFinal < sessao.Atual.Duracao
            && iniFinal + durFinal > sessao.Atual.Duracao
            && (iniFinal + durFinal) - sessao.Atual.Duracao <= tolerancia)
            durFinal = sessao.Atual.Duracao - iniFinal;
        if (duracaoMidia is { } limiteOrigem && oriFinal < limiteOrigem
            && oriFinal + durFinal > limiteOrigem
            && (oriFinal + durFinal) - limiteOrigem <= tolerancia)
            durFinal = limiteOrigem - oriFinal;
        return (iniFinal, oriFinal, durFinal);
    }

    private void EditarCamada(object sender, RoutedEventArgs e)
        => UltimaEdicao = EditarCamadaAsync();

    internal async Task EditarCamadaAsync()
    {
        if (sessao is null || editandoPropriedadesVisuais) return;
        var camada = sessao.Atual.Camadas.SingleOrDefault(l => l.Clipe.Id == sessao.Atual.ItemSelecionadoId);
        if (camada is null) { Anunciar("Selecione uma camada da composição."); return; }
        var midia = sessao.Atual.Biblioteca.Single(a => a.Id == camada.Clipe.MidiaId);
        if (midia.Tipo == TipoMidia.Audio)
        {
            editandoPropriedadesVisuais = true;
            var tinhaComposicaoVisivel = TinhaPreviaComposicaoVisivel;
            try
            {
                if (!await PrepararEdicaoAsync()) return;
                var valores = SolicitarValoresCamada(camada.Inicio, camada.Clipe.InicioNaOrigem, camada.Clipe.Duracao, midia.Duracao);
                if (valores is { } v && await ExecutarAsync(new AtualizarCamada(camada.Clipe.Id, v.Inicio, v.Origem, v.Duracao)))
                    Anunciar("Intervalo da camada atualizado.");
                BotaoEditarCamada.Focus();
                await RestaurarQuadroPausadoSeNecessarioAsync(tinhaComposicaoVisivel);
            }
            finally { editandoPropriedadesVisuais = false; }
            return;
        }
        await EditarElementoVisualAsync(camada.Clipe, camada.Inicio, ehCamada: true, BotaoEditarCamada);
    }

    private void EditarPropriedadesVisuais(object sender, RoutedEventArgs e)
    {
        var reserva = ReferenceEquals(sender, MenuPropriedadesVisuais) ? (UIElement)LinhaDoTempo : BotaoPropriedadesVisuais;
        UltimaEdicao = EditarPropriedadesVisuaisAsync(reserva);
    }

    internal async Task EditarPropriedadesVisuaisAsync(UIElement? reservaFoco = null)
    {
        if (sessao is null || editandoPropriedadesVisuais) return;
        var id = sessao.Atual.ItemSelecionadoId;
        var camada = sessao.Atual.Camadas.SingleOrDefault(l => l.Clipe.Id == id);
        if (camada is not null)
        {
            var midiaCamada = sessao.Atual.Biblioteca.Single(a => a.Id == camada.Clipe.MidiaId);
            if (midiaCamada.Tipo == TipoMidia.Audio) { Anunciar("Mídias de áudio não possuem propriedades visuais."); return; }
            await EditarElementoVisualAsync(camada.Clipe, camada.Inicio, ehCamada: true, reservaFoco ?? BotaoPropriedadesVisuais);
            return;
        }
        var clipeSequencia = sessao.Atual.LinhaDoTempo.SingleOrDefault(c => c.Id == id);
        if (clipeSequencia is null) { Anunciar("Selecione um conteúdo visual na composição."); return; }
        var midiaSequencia = sessao.Atual.Biblioteca.Single(a => a.Id == clipeSequencia.MidiaId);
        if (midiaSequencia.Tipo == TipoMidia.Audio) { Anunciar("Mídias de áudio não possuem propriedades visuais."); return; }
        var item = new AcoesEdicao(sessao).Conteudos().Single(c => c.Id == clipeSequencia.Id);
        await EditarElementoVisualAsync(clipeSequencia, item.Inicio, ehCamada: false, reservaFoco ?? BotaoPropriedadesVisuais);
    }

    private async Task EditarElementoVisualAsync(Clipe clipe, TempoMidia inicioAtual, bool ehCamada, UIElement destinoFoco)
    {
        if (sessao is null || editandoPropriedadesVisuais) return;
        editandoPropriedadesVisuais = true;
        var tinhaComposicaoVisivel = TinhaPreviaComposicaoVisivel;
        try
        {
            if (!await PrepararEdicaoAsync()) return;
            var midia = sessao.Atual.Biblioteca.Single(a => a.Id == clipe.MidiaId);
            ResultadoPropriedadesVisuais? resultado;
            if (SolicitarPropriedadesVisuaisOverride is { } overrideResultado)
                resultado = overrideResultado();
            else
            {
                var dialogo = CriarDialogoPropriedadesVisuais(
                    ehCamada ? "Propriedades e intervalo da camada" : "Propriedades visuais do clipe",
                    "Aplicar",
                    midia,
                    ehCamada: ehCamada,
                    permitirOpcaoSequencia: false,
                    inicioInicial: inicioAtual,
                    origemInicial: clipe.InicioNaOrigem,
                    duracaoInicial: clipe.Duracao,
                    layoutInicial: clipe.ObterLayout(ehCamada),
                    ignorarClipeId: clipe.Id);
                dialogo.Dialogo.ShowDialog();
                resultado = dialogo.ObterResultado();
            }
            if (resultado is not null)
            {
                if (ehCamada)
                {
                    var conflitos = new AcoesEdicao(sessao).ConflitosVisuais(resultado.Inicio, resultado.Duracao, resultado.Layout, clipe.Id);
                    if (await ExecutarAsync(new AtualizarCamada(clipe.Id, resultado.Inicio, resultado.InicioNaOrigem, resultado.Duracao, resultado.Layout)))
                    {
                        var aviso = conflitos.IsEmpty ? "" : $" Aviso: há sobreposição simultânea na mesma região com {string.Join(", ", conflitos)}.";
                        Anunciar("Propriedades e intervalo da camada atualizados." + aviso);
                    }
                }
                else
                {
                    TempoMidia? novaDuracao = midia.Tipo == TipoMidia.Imagem ? resultado.Duracao : null;
                    if (await ExecutarAsync(new DefinirLayoutVisual(clipe.Id, resultado.Layout, novaDuracao)))
                        Anunciar("Propriedades visuais atualizadas.");
                }
            }
            if (ReferenceEquals(destinoFoco, LinhaDoTempo)) FocarConteudoSelecionado();
            else RestaurarFocoAposDialogo(destinoFoco, BotaoPropriedadesVisuais);
            await RestaurarQuadroPausadoSeNecessarioAsync(tinhaComposicaoVisivel);
        }
        finally { editandoPropriedadesVisuais = false; }
    }

    internal DialogoPropriedadesVisuais CriarDialogoPropriedadesVisuais(
        string titulo,
        string rotuloConfirmar,
        Midia midia,
        bool ehCamada,
        bool permitirOpcaoSequencia,
        TempoMidia inicioInicial,
        TempoMidia origemInicial,
        TempoMidia duracaoInicial,
        LayoutVisual layoutInicial,
        Guid? ignorarClipeId = null,
        TempoMidia? duracaoSequenciaInicial = null)
    {
        ResultadoPropriedadesVisuais? resultado = null;
        var duracaoPadraoSequencia = duracaoSequenciaInicial ?? duracaoInicial;
        var painel = new StackPanel();
        var opcoesModo = ehCamada
            ? (permitirOpcaoSequencia
                ? new[]
                {
                    "Sobrepor a imagem ao conteúdo visual existente",
                    "Substituir a imagem do vídeo durante o intervalo",
                    "Adicionar como novo clipe na sequência principal"
                }
                : new[]
                {
                    "Sobrepor a imagem ao conteúdo visual existente",
                    "Substituir a imagem do vídeo durante o intervalo"
                })
            : new[] { "Substituir a imagem do vídeo durante o intervalo" };
        var indiceModoInicial = ehCamada ? (layoutInicial.Modo == ModoExibicaoVisual.Substituir ? 1 : 0) : 0;
        var modoExibicao = CriarListaOpcoes(opcoesModo, indiceModoInicial, "Modo de exibição visual");
        var painelModo = new StackPanel { Visibility = ehCamada ? Visibility.Visible : Visibility.Collapsed };
        painelModo.Children.Add(new Label { Content = "_Modo de exibição", Target = modoExibicao });
        painelModo.Children.Add(modoExibicao);
        painel.Children.Add(painelModo);

        var opcoesPosicao = OrdemPosicoesVisuais.Select(DescreverPosicao).ToArray();
        var indicePosicaoInicial = Math.Max(0, Array.IndexOf(OrdemPosicoesVisuais, layoutInicial.Posicao));
        var posicao = CriarListaOpcoes(opcoesPosicao, indicePosicaoInicial, "Posição na tela");
        posicao.MaxHeight = 140;

        var opcoesTamanho = new[]
        {
            "Pequeno (limite de 25%)",
            "Médio (50%)",
            "Grande (75%)",
            "Personalizado (5% a 100%, em passos de 5%)"
        };
        var indiceTamanhoInicial = layoutInicial.EscalaPercentual switch
        {
            25 => 0,
            50 => 1,
            75 => 2,
            _ => 3
        };
        var tamanho = CriarListaOpcoes(opcoesTamanho, indiceTamanhoInicial, "Tamanho da imagem sobreposta");
        var escalaPersonalizada = new TextBox
        {
            Text = layoutInicial.EscalaPercentual.ToString(CultureInfo.CurrentCulture),
            Margin = new Thickness(0, 4, 0, 8)
        };
        AutomationProperties.SetName(escalaPersonalizada, "Escala personalizada, de 5 a 100 por cento, em passos de 5");
        var painelEscalaPersonalizada = new StackPanel();
        painelEscalaPersonalizada.Children.Add(new Label { Content = "_Escala personalizada (5% a 100%, em passos de 5%)", Target = escalaPersonalizada });
        painelEscalaPersonalizada.Children.Add(escalaPersonalizada);

        var painelSobrepor = new StackPanel();
        painelSobrepor.Children.Add(new Label { Content = "_Posição na tela (nove regiões)", Target = posicao });
        painelSobrepor.Children.Add(posicao);
        painelSobrepor.Children.Add(new Label { Content = "_Tamanho da sobreposição", Target = tamanho });
        painelSobrepor.Children.Add(tamanho);
        painelSobrepor.Children.Add(painelEscalaPersonalizada);
        painel.Children.Add(painelSobrepor);

        var opcoesAjuste = new[]
        {
            "Permitir bordas (imagem integral centralizada)",
            "Preencher por inteiro (corte proporcional)"
        };
        var ajuste = CriarListaOpcoes(opcoesAjuste, layoutInicial.Ajuste == AjusteVisual.PreencherInteiro ? 1 : 0, "Ajuste da imagem substitutiva");
        var painelSubstituir = new StackPanel();
        painelSubstituir.Children.Add(new Label { Content = "_Ajuste da imagem substitutiva", Target = ajuste });
        painelSubstituir.Children.Add(ajuste);
        painel.Children.Add(painelSubstituir);

        var textoInicioInicial = Segundos(inicioInicial).ToString("0.######", CultureInfo.CurrentCulture);
        var inicioComposicao = new TextBox
        {
            Text = textoInicioInicial,
            Margin = new Thickness(0, 4, 0, 8)
        };
        AutomationProperties.SetName(inicioComposicao, "Início na composição, em segundos");
        var painelInicioComposicao = new StackPanel();
        painelInicioComposicao.Children.Add(new Label { Content = "_Início na composição, em segundos", Target = inicioComposicao });
        painelInicioComposicao.Children.Add(inicioComposicao);
        painel.Children.Add(painelInicioComposicao);

        var textoOrigemInicial = Segundos(origemInicial).ToString("0.######", CultureInfo.CurrentCulture);
        var inicioOrigem = new TextBox
        {
            Text = textoOrigemInicial,
            Margin = new Thickness(0, 4, 0, 8)
        };
        AutomationProperties.SetName(inicioOrigem, "Início no arquivo original, em segundos");
        var painelInicioOrigem = new StackPanel { Visibility = ehCamada && midia.Tipo == TipoMidia.Video ? Visibility.Visible : Visibility.Collapsed };
        painelInicioOrigem.Children.Add(new Label { Content = "Início no arquivo _original, em segundos", Target = inicioOrigem });
        painelInicioOrigem.Children.Add(inicioOrigem);
        painel.Children.Add(painelInicioOrigem);

        var textoDuracaoInicial = Segundos(duracaoInicial).ToString("0.######", CultureInfo.CurrentCulture);
        var textoDuracaoSequencia = Segundos(duracaoPadraoSequencia).ToString("0.######", CultureInfo.CurrentCulture);
        var duracao = new TextBox
        {
            Text = textoDuracaoInicial,
            Margin = new Thickness(0, 4, 0, 8)
        };
        AutomationProperties.SetName(duracao, "Duração da exibição, em segundos");
        var painelDuracao = new StackPanel { Visibility = ehCamada || midia.Tipo == TipoMidia.Imagem ? Visibility.Visible : Visibility.Collapsed };
        painelDuracao.Children.Add(new Label { Content = "_Duração, em segundos", Target = duracao });
        painelDuracao.Children.Add(duracao);
        painel.Children.Add(painelDuracao);

        var avisoSobreposicao = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8), Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(avisoSobreposicao, AutomationLiveSetting.Polite);
        painel.Children.Add(avisoSobreposicao);

        var erro = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        AutomationProperties.SetLiveSetting(erro, AutomationLiveSetting.Assertive);
        painel.Children.Add(erro);

        bool ModoEhSequencia() => !ehCamada || (permitirOpcaoSequencia && modoExibicao.SelectedIndex == 2);
        bool ModoEhSobrepor() => ehCamada && modoExibicao.SelectedIndex == 0;

        int ObterEscalaAtual() => tamanho.SelectedIndex switch
        {
            0 => 25,
            1 => 50,
            2 => 75,
            _ => int.TryParse(escalaPersonalizada.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var v) ? v : -1
        };

        LayoutVisual ConstruirLayout(int escalaValidada)
        {
            var modo = ModoEhSobrepor() ? ModoExibicaoVisual.Sobrepor : ModoExibicaoVisual.Substituir;
            var pos = posicao.SelectedIndex >= 0 && posicao.SelectedIndex < OrdemPosicoesVisuais.Length
                ? OrdemPosicoesVisuais[posicao.SelectedIndex]
                : PosicaoVisual.Centro;
            var aj = ajuste.SelectedIndex == 1 ? AjusteVisual.PreencherInteiro : AjusteVisual.PermitirBordas;
            return modo == ModoExibicaoVisual.Sobrepor
                ? new(ModoExibicaoVisual.Sobrepor, pos, escalaValidada, AjusteVisual.PermitirBordas)
                : new(ModoExibicaoVisual.Substituir, PosicaoVisual.Centro, 100, aj);
        }

        bool TentarLerTempo(TextBox campo, string textoInicial, TempoMidia valorInicial, decimal minimo, out TempoMidia tempoLido)
        {
            var texto = campo.Text.Trim();
            if (texto == textoInicial)
            {
                tempoLido = valorInicial;
                return true;
            }
            if (decimal.TryParse(texto, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.CurrentCulture, out var valor) && valor >= minimo && valor <= 864000m)
            {
                tempoLido = Tempo(valor);
                return true;
            }
            tempoLido = TempoMidia.Zero;
            return false;
        }

        void AtualizarVisibilidadeEAviso()
        {
            var sobrepor = ModoEhSobrepor();
            var sequencia = ModoEhSequencia();
            painelSobrepor.Visibility = sobrepor ? Visibility.Visible : Visibility.Collapsed;
            painelSubstituir.Visibility = sobrepor ? Visibility.Collapsed : Visibility.Visible;
            painelEscalaPersonalizada.Visibility = sobrepor && tamanho.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            painelInicioComposicao.Visibility = sequencia ? Visibility.Collapsed : Visibility.Visible;
            painelInicioOrigem.Visibility = !sequencia && midia.Tipo == TipoMidia.Video ? Visibility.Visible : Visibility.Collapsed;

            if (!sequencia && sessao is not null
                && TentarLerTempo(inicioComposicao, textoInicioInicial, inicioInicial, 0m, out var iniTempo)
                && TentarLerTempo(duracao, textoDuracaoInicial, duracaoInicial, 0.001m, out var durTempo))
            {
                var escalaTentativa = Math.Clamp(ObterEscalaAtual() is >= 5 and <= 100 ? ObterEscalaAtual() : 100, 5, 100);
                var layoutTentativa = ConstruirLayout(escalaTentativa);
                var conflitos = new AcoesEdicao(sessao).ConflitosVisuais(iniTempo, durTempo, layoutTentativa, ignorarClipeId);
                if (!conflitos.IsEmpty)
                {
                    var novoAviso = $"Aviso: sobreposição simultânea na mesma região com {string.Join(", ", conflitos)}. A operação é permitida.";
                    var mudou = avisoSobreposicao.Text != novoAviso;
                    avisoSobreposicao.Text = novoAviso;
                    avisoSobreposicao.Visibility = Visibility.Visible;
                    if (mudou)
                        (UIElementAutomationPeer.FromElement(avisoSobreposicao) ?? UIElementAutomationPeer.CreatePeerForElement(avisoSobreposicao))
                            ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                    return;
                }
            }
            avisoSobreposicao.Text = "";
            avisoSobreposicao.Visibility = Visibility.Collapsed;
        }

        modoExibicao.SelectionChanged += (_, _) =>
        {
            if (permitirOpcaoSequencia)
            {
                var textoAtual = duracao.Text.Trim();
                if (ModoEhSequencia() && textoAtual == textoDuracaoInicial)
                    duracao.Text = textoDuracaoSequencia;
                else if (!ModoEhSequencia() && textoAtual == textoDuracaoSequencia)
                    duracao.Text = textoDuracaoInicial;
            }
            AtualizarVisibilidadeEAviso();
        };
        posicao.SelectionChanged += (_, _) => AtualizarVisibilidadeEAviso();
        tamanho.SelectionChanged += (_, _) =>
        {
            if (tamanho.SelectedIndex == 0) escalaPersonalizada.Text = "25";
            else if (tamanho.SelectedIndex == 1) escalaPersonalizada.Text = "50";
            else if (tamanho.SelectedIndex == 2) escalaPersonalizada.Text = "75";
            AtualizarVisibilidadeEAviso();
        };
        inicioComposicao.TextChanged += (_, _) => AtualizarVisibilidadeEAviso();
        duracao.TextChanged += (_, _) => AtualizarVisibilidadeEAviso();
        AtualizarVisibilidadeEAviso();

        var rolagem = new ScrollViewer
        {
            Content = painel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        };
        var dialogo = CriarDialogo(titulo, rolagem);
        dialogo.MaxHeight = SystemParameters.WorkArea.Height;
        var confirmar = new Button { Content = rotuloConfirmar, IsDefault = true };

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
            var escala = 100;
            if (ModoEhSobrepor())
            {
                if (posicao.SelectedIndex < 0 || posicao.SelectedIndex >= OrdemPosicoesVisuais.Length)
                {
                    InformarErro("Escolha uma das nove posições na tela.", posicao);
                    return;
                }
                if (tamanho.SelectedIndex < 0)
                {
                    InformarErro("Escolha o tamanho da sobreposição.", tamanho);
                    return;
                }
                escala = ObterEscalaAtual();
                if (escala is < 5 or > 100 || escala % 5 != 0)
                {
                    InformarErro("Informe uma escala personalizada entre 5% e 100%, em passos de 5%.", escalaPersonalizada);
                    return;
                }
            }
            else if (ajuste.SelectedIndex < 0)
            {
                InformarErro("Escolha o ajuste da imagem: Permitir bordas ou Preencher por inteiro.", ajuste);
                return;
            }

            var sequencia = ModoEhSequencia();
            var inicio = inicioInicial;
            var origem = origemInicial;
            var duracaoFinal = sequencia ? duracaoPadraoSequencia : duracaoInicial;

            if (!sequencia)
            {
                if (!TentarLerTempo(inicioComposicao, textoInicioInicial, inicioInicial, 0m, out inicio))
                {
                    InformarErro("Início na composição, em segundos: informe um número entre 0 e 864000.", inicioComposicao);
                    return;
                }
                if (midia.Tipo == TipoMidia.Video && !TentarLerTempo(inicioOrigem, textoOrigemInicial, origemInicial, 0m, out origem))
                {
                    InformarErro("Início no arquivo original, em segundos: informe um número entre 0 e 864000.", inicioOrigem);
                    return;
                }
            }

            if (painelDuracao.Visibility == Visibility.Visible)
            {
                var textoDuracaoRef = sequencia ? textoDuracaoSequencia : textoDuracaoInicial;
                var valorDuracaoRef = sequencia ? duracaoPadraoSequencia : duracaoInicial;
                if (!TentarLerTempo(duracao, textoDuracaoRef, valorDuracaoRef, 0.001m, out duracaoFinal))
                {
                    InformarErro("Informe uma duração entre 0,001 e 864000 segundos.", duracao);
                    return;
                }
            }

            var toleranciaArredondamento = new TempoMidia(1, 1_000_000);
            if (!sequencia && sessao is not null && inicio < sessao.Atual.Duracao
                && inicio + duracaoFinal > sessao.Atual.Duracao
                && (inicio + duracaoFinal) - sessao.Atual.Duracao <= toleranciaArredondamento)
            {
                duracaoFinal = sessao.Atual.Duracao - inicio;
            }
            if (!sequencia && midia.Duracao is { } limiteTol && origem < limiteTol
                && origem + duracaoFinal > limiteTol
                && (origem + duracaoFinal) - limiteTol <= toleranciaArredondamento)
            {
                duracaoFinal = limiteTol - origem;
            }

            if (!sequencia && sessao is not null && inicio + duracaoFinal > sessao.Atual.Duracao)
            {
                InformarErro("O intervalo da camada ultrapassa a duração da composição.", duracao);
                return;
            }

            if (!sequencia && midia.Duracao is { } limiteOrigem && origem + duracaoFinal > limiteOrigem)
            {
                InformarErro("A duração informada ultrapassa a duração do arquivo original.", duracao);
                return;
            }

            if (sequencia && !ehCamada && ignorarClipeId is not null && sessao is not null)
            {
                var novaDuracaoTotal = sessao.Atual.Duracao - duracaoInicial + duracaoFinal;
                if (sessao.Atual.Camadas.Any(c => c.Inicio + c.Clipe.Duracao > novaDuracaoTotal))
                {
                    InformarErro("A nova duração deixaria camadas fora da composição.", duracao);
                    return;
                }
            }

            resultado = new(sequencia, inicio, origem, duracaoFinal, ConstruirLayout(escala));
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) dialogo.DialogResult = true;
            else dialogo.Close();
        };

        painel.Children.Add(confirmar);
        AdicionarFechar(dialogo, painel, "Cancelar");
        dialogo.Loaded += (_, _) =>
        {
            if (ehCamada) FocarOpcaoLista(modoExibicao);
            else FocarOpcaoLista(ajuste);
            if (avisoSobreposicao.Visibility == Visibility.Visible)
                (UIElementAutomationPeer.FromElement(avisoSobreposicao) ?? UIElementAutomationPeer.CreatePeerForElement(avisoSobreposicao))
                    ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        };
        return new(dialogo, modoExibicao, posicao, tamanho, escalaPersonalizada, ajuste, inicioComposicao, inicioOrigem, duracao, avisoSobreposicao, erro, confirmar, () => resultado);
    }

    private async void ExcluirCamada(object sender, RoutedEventArgs e)
    {
        if (sessao?.Atual.ItemSelecionadoId is not { } id) return;
        if (await ExecutarAsync(new RemoverCamada(id))) Anunciar("Camada removida. A sequência principal foi preservada.");
        LinhaDoTempo.Focus();
    }

    private async void LocalizarMidia(object sender, RoutedEventArgs e)
    {
        if (Ocupado || sessao is null || Biblioteca.SelectedItems.Count != 1) { Anunciar("Selecione uma mídia da biblioteca para localizar seu arquivo."); return; }
        var id = ((Linha)Biblioteca.SelectedItem).Id;
        var dialogo = new OpenFileDialog { Title = "Localizar arquivo original", Filter = "Mídias|*.mp4;*.mov;*.png;*.jpg;*.jpeg;*.mp3;*.wav;*.m4a;*.ogg" };
        if (dialogo.ShowDialog(this) != true) return;
        emTransicao = true; Estrutura.IsEnabled = false;
        try
        {
            await LimparPreviaAsync();
            await new AcoesEdicao(sessao).ReligarAsync(id, dialogo.FileName, leitorMetadados);
            exibirPropriedadesBiblioteca = true; Atualizar();
            Anunciar("Localização da mídia atualizada; as edições foram preservadas.");
        }
        catch (Exception ex) { Anunciar("Não foi possível religar a mídia: " + ex.Message); }
        finally { emTransicao = false; Estrutura.IsEnabled = true; Biblioteca.Focus(); }
    }

    private decimal[]? SolicitarNumeros(string titulo, (string Nome, decimal Valor, decimal Minimo, decimal Maximo, bool Inteiro)[] campos)
    {
        decimal[]? resultado = null;
        var painel = new StackPanel(); var entradas = new List<TextBox>();
        foreach (var campo in campos)
        {
            var entrada = new TextBox { Text = campo.Valor.ToString("0.######", CultureInfo.CurrentCulture), Margin = new Thickness(0, 4, 0, 12) };
            painel.Children.Add(new Label { Content = campo.Nome, Target = entrada });
            AutomationProperties.SetName(entrada, campo.Nome); painel.Children.Add(entrada); entradas.Add(entrada);
        }
        var erro = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(erro, AutomationLiveSetting.Assertive); painel.Children.Add(erro);
        var dialogo = CriarDialogo(titulo, painel); var aplicar = new Button { Content = "Aplicar", IsDefault = true };
        aplicar.Click += (_, _) =>
        {
            var valores = new decimal[campos.Length];
            for (var i = 0; i < campos.Length; i++)
            {
                var campo = campos[i];
                if (!decimal.TryParse(entradas[i].Text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.CurrentCulture, out valores[i]) || valores[i] < campo.Minimo || valores[i] > campo.Maximo || (campo.Inteiro && valores[i] != decimal.Truncate(valores[i])))
                {
                    erro.Text = $"{campo.Nome}: informe {(campo.Inteiro ? "um inteiro" : "um número")} entre {campo.Minimo} e {campo.Maximo}.";
                    (UIElementAutomationPeer.FromElement(erro) ?? UIElementAutomationPeer.CreatePeerForElement(erro))?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                    entradas[i].Focus(); entradas[i].SelectAll(); return;
                }
            }
            resultado = valores; dialogo.DialogResult = true;
        };
        painel.Children.Add(aplicar); AdicionarFechar(dialogo, painel, "Cancelar");
        dialogo.Loaded += (_, _) => { entradas[0].Focus(); entradas[0].SelectAll(); };
        dialogo.ShowDialog(); return resultado;
    }
}
