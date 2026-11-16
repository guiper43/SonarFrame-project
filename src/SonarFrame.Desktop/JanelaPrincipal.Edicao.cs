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
        var texto = $"{item.Nome}\n{(item.EhCamada ? "Camada independente" : "Sequência principal")}\nInício na composição: {item.Inicio}\nDuração: {item.Duracao}\nVolume: {item.Volume}%";
        if (sessao is not null)
        {
            var midia = sessao.Atual.Biblioteca.Single(a => a.Id == item.MidiaId);
            if (midia.Tipo != TipoMidia.Audio)
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
