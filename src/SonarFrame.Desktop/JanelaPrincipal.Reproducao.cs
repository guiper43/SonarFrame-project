using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SonarFrame.Application;
using SonarFrame.Core;

namespace SonarFrame.Desktop;

public partial class JanelaPrincipal
{
    private PreviaBiblioteca previa = null!;
    private readonly DispatcherTimer relogioPrevia = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool comandoPreviaEmAndamento;
    private bool previaEncerrada;
    internal PreviaBiblioteca PreviaAtual => previa;
    internal Task UltimaOperacaoPrevia { get; private set; } = Task.CompletedTask;

    private void ConfigurarPrevia(IFabricaReprodutorMidia? fabrica)
    {
        previa = new PreviaBiblioteca(fabrica ?? new FabricaPreviaVlc(SuperficiePrevia));
        previa.Alterado += PreviaAlterada;
        relogioPrevia.Tick += (_, _) => AtualizarPosicaoPrevia();
    }

    private void PreviaAlterada(SituacaoPrevia situacao)
    {
        if (previaEncerrada || Dispatcher.HasShutdownStarted) return;
        if (Dispatcher.CheckAccess()) ExibirSituacaoPrevia(situacao);
        else _ = Dispatcher.InvokeAsync(() => ExibirSituacaoPrevia(situacao));
    }

    private void ExibirSituacaoPrevia(SituacaoPrevia situacao)
    {
        if (previaEncerrada || !ReferenceEquals(previa.Atual, situacao)) return;
        if (situacao.Estado is EstadoPrevia.Reproduzindo or EstadoPrevia.Vazia or EstadoPrevia.Erro) OcultarQuadro();
        var mensagem = situacao.Mensagem ?? situacao.Estado switch
        {
            EstadoPrevia.Preparando => "Preparando prévia. A primeira abertura pode levar alguns segundos. Você pode cancelar.",
            EstadoPrevia.Imagem => "Imagem estática aberta. Controles temporais indisponíveis.",
            EstadoPrevia.Reproduzindo => "Reproduzindo.",
            EstadoPrevia.Pausada => "Pausado.",
            EstadoPrevia.Concluida => "Reprodução concluída.",
            EstadoPrevia.Erro => "Não foi possível abrir a prévia.",
            _ => "Nenhuma prévia aberta."
        };
        if (composicaoAtiva) mensagem = mensagem.Replace("mídia da biblioteca", "composição");
        TituloPrevia.Text = situacao.Midia is { } midia ? $"Prévia da biblioteca: {midia.Nome}. {mensagem}" : mensagem;
        if (composicaoAtiva) TituloPrevia.Text = $"Prévia da composição. {mensagem}";
        SuperficiePrevia.Visibility = situacao.Midia?.Tipo is TipoMidia.Imagem or TipoMidia.Video && situacao.Estado is not (EstadoPrevia.Vazia or EstadoPrevia.Erro)
            ? Visibility.Visible : Visibility.Collapsed;
        if (situacao.Estado == EstadoPrevia.Reproduzindo) relogioPrevia.Start(); else relogioPrevia.Stop();
        AtualizarControlesPrevia(); AtualizarPosicaoPrevia();
        if (composicaoAtiva && sessao is not null && situacao.Estado == EstadoPrevia.Concluida)
            UltimoQuadro = ExibirQuadroAsync(sessao.Atual.Cursor);
        if ((!navegandoTemporal || situacao.Estado == EstadoPrevia.Erro)
            && (situacao.Estado != EstadoPrevia.Vazia || situacao.Mensagem is not null)) Anunciar(mensagem);
    }

    private void AtualizarControlesPrevia()
    {
        if (previa is null) return;
        var estado = previa.Atual.Estado;
        BotaoRemoverMidia.IsEnabled = sessao is not null && Biblioteca.SelectedItems.Count > 0;
        var temporal = !comandoPreviaEmAndamento && (estado is EstadoPrevia.Reproduzindo or EstadoPrevia.Pausada or EstadoPrevia.Concluida
            || composicaoAtiva && sessao?.Atual.LinhaDoTempo.Length > 0 && preparacaoComposicao is null);
        BotaoReproduzirPrevia.IsEnabled = BotaoPararPrevia.IsEnabled = BotaoBuscarPrevia.IsEnabled = DestinoPrevia.IsEnabled = temporal;
        BotaoReproduzirPrevia.Content = estado == EstadoPrevia.Reproduzindo ? "Pausar" : "Reproduzir";
        BotaoCancelarPrevia.IsEnabled = estado == EstadoPrevia.Preparando;
        BotaoAbrirPrevia.IsEnabled = sessao is not null && Biblioteca.SelectedItems.Count == 1 && !previaEncerrada;
        var podeReproduzirVideo = sessao?.Atual.LinhaDoTempo.Length > 0 && !previaEncerrada;
        if (composicaoAtiva || previa.Atual.Midia is null) BotaoReproduzirPrevia.IsEnabled = podeReproduzirVideo;
        System.Windows.Automation.AutomationProperties.SetName(BotaoReproduzirPrevia, BotaoReproduzirPrevia.Content.ToString());
        BotaoPararComposicao.IsEnabled = composicaoAtiva && temporal && preparacaoComposicao is null;
        BotaoReproduzirSelecao.IsEnabled = podeReproduzirVideo && preparacaoComposicao is null && sessao?.Atual.Selecao is { } selecao && selecao.Inicio < selecao.Fim;
        BotaoCancelarComposicao.IsEnabled = preparacaoComposicao is not null || navegacaoTemporal is not null || buscaQuadro is not null;
    }

    private void AtualizarPosicaoPrevia()
    {
        SincronizarCursorComposicao();
        var situacao = previa.Atual;
        if (composicaoAtiva && sessao is not null)
        {
            PosicaoPrevia.Text = $"Posição: {sessao.Atual.Cursor} de {sessao.Atual.Duracao}";
            return;
        }
        PosicaoPrevia.Text = situacao.Estado switch
        {
            EstadoPrevia.Imagem => "Imagem estática, sem duração na biblioteca.",
            EstadoPrevia.Reproduzindo or EstadoPrevia.Pausada or EstadoPrevia.Concluida =>
                $"Posição: {(situacao.Estado == EstadoPrevia.Concluida ? situacao.Midia!.Duracao : previa.Posicao)} de {situacao.Midia!.Duracao}",
            _ => ""
        };
    }

    private void AbrirPreviaSelecionada(object sender, RoutedEventArgs e) => UltimaOperacaoPrevia = AbrirPreviaSelecionadaAsync();
    internal async Task AbrirPreviaSelecionadaAsync()
    {
        if (sessao is null || emTransicao || previaEncerrada) return;
        if (Biblioteca.SelectedItems.Count != 1 || Biblioteca.SelectedItem is not Linha linha)
        { Anunciar("Selecione uma única mídia da biblioteca para abrir a prévia."); return; }
        var midia = sessao.Atual.Biblioteca.Single(m => m.Id == linha.Id);
        await InterromperComposicaoAsync(true);
        BotaoPrevia.Focus(); BotaoPrevia.BringIntoView();
        await previa.AbrirAsync(midia);
    }

    private void ConsultarPrevia(object sender, RoutedEventArgs e)
    {
        AtualizarPosicaoPrevia();
        var camadas = composicaoAtiva && sessao is not null
            ? sessao.Atual.Camadas.Where(c => c.Inicio <= sessao.Atual.Cursor && sessao.Atual.Cursor < c.Inicio + c.Clipe.Duracao).Select(c => c.Clipe.Nome).ToArray()
            : [];
        Anunciar($"{TituloPrevia.Text} {PosicaoPrevia.Text}" + (camadas.Length > 0 ? $" Camadas ativas: {string.Join(", ", camadas)}." : ""));
    }
    private void AlternarPrevia(object sender, RoutedEventArgs e)
    {
        if (composicaoAtiva || previa.Atual.Midia is null) AlternarComposicao(sender, e);
        else UltimaOperacaoPrevia = ExecutarComandoPreviaAsync(previa.AlternarAsync);
    }
    private void PararPrevia(object sender, RoutedEventArgs e)
    {
        if (composicaoAtiva) PararComposicao(sender, e);
        else UltimaOperacaoPrevia = ExecutarComandoPreviaAsync(previa.PararAsync);
    }
    private void CancelarPrevia(object sender, RoutedEventArgs e) { previa.Cancelar(); BotaoPrevia.Focus(); }
    private void BuscarPrevia(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(DestinoPrevia.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var segundos) || segundos < 0 || segundos > 864000)
        { Anunciar("Informe uma posição válida em segundos."); DestinoPrevia.Focus(); return; }
        if (composicaoAtiva && sessao is not null)
        {
            var destino = new TempoMidia((long)decimal.Round(segundos * 1000000), 1000000);
            if (destino > sessao.Atual.Duracao) { Anunciar("A posição ultrapassa o fim da composição."); return; }
            UltimaOperacaoPrevia = NavegarTemporalAsync(Key.None, ModifierKeys.None, destino);
            return;
        }
        UltimaOperacaoPrevia = ExecutarComandoPreviaAsync(async () =>
        {
            destinoManualComposicao = null;
            await previa.PosicionarAsync(new((long)decimal.Round(segundos * 1000000), 1000000));
            if (composicaoAtiva && sessao is not null)
            {
                SincronizarCursorComposicao();
                UltimoQuadro = ExibirQuadroAsync(sessao.Atual.Cursor); await UltimoQuadro;
            }
            if (previa.Atual.Estado == EstadoPrevia.Pausada && previa.Atual.Mensagem is null)
            { AtualizarPosicaoPrevia(); Anunciar(PosicaoPrevia.Text); }
        });
    }
    private async Task ExecutarComandoPreviaAsync(Func<Task> acao)
    {
        if (comandoPreviaEmAndamento || previaEncerrada) return;
        var foco = Keyboard.FocusedElement;
        comandoPreviaEmAndamento = true; AtualizarControlesPrevia();
        try { await acao(); }
        finally
        {
            comandoPreviaEmAndamento = false; AtualizarControlesPrevia();
            if (foco is UIElement { IsEnabled: true, IsVisible: true } elemento && !Ocupado && !previaEncerrada) elemento.Focus();
        }
    }

    internal async Task LimparPreviaAsync()
    {
        await InterromperComposicaoAsync(true);
        await previa.LimparAsync();
    }
    private async Task<bool> PrepararEdicaoAsync()
    {
        var origem = sessao;
        if (origem is null || emTransicao || previaEncerrada) return false;
        await InterromperComposicaoAsync(true);
        if (previa.Atual.Estado is not (EstadoPrevia.Preparando or EstadoPrevia.Reproduzindo)) return true;
        var foco = Keyboard.FocusedElement;
        emTransicao = true; Estrutura.IsEnabled = false;
        try
        {
            if (previa.Atual.Estado == EstadoPrevia.Preparando) await previa.LimparAsync();
            else await previa.PausarAsync();
            return ReferenceEquals(origem, sessao) && !previaEncerrada;
        }
        finally { emTransicao = false; Estrutura.IsEnabled = true; foco?.Focus(); }
    }
    internal async Task EncerrarPreviaAsync()
    {
        if (previaEncerrada) return;
        await InterromperComposicaoAsync(true);
        previaEncerrada = true; relogioPrevia.Stop(); previa.Alterado -= PreviaAlterada;
        await previa.DisposeAsync();
        await preparadorComposicao.DisposeAsync();
    }
}
