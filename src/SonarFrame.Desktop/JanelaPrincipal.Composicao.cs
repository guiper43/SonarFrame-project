using System.IO;
using System.Windows;
using System.Windows.Controls;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;

namespace SonarFrame.Desktop;

public partial class JanelaPrincipal
{
    private IPreparadorComposicao preparadorComposicao = null!;
    private CancellationTokenSource? preparacaoComposicao;
    private TaskCompletionSource? preparacaoFinalizada;
    private bool composicaoAtiva;
    private bool encerrandoSelecao;
    private TempoMidia? fimReproducaoSelecao;
    private TempoMidia? destinoManualComposicao;
    private TempoMidia inicioArquivoComposicao;
    private bool intervaloPreparado;
    internal Task UltimaOperacaoComposicao { get; private set; } = Task.CompletedTask;

    private void ConfigurarComposicao(IPreparadorComposicao preparador)
    {
        preparadorComposicao = preparador;
    }

    private void AlternarComposicao(object sender, RoutedEventArgs e) => UltimaOperacaoComposicao = ReproduzirComposicaoAsync(false);
    private void ReproduzirSelecaoComposicao(object sender, RoutedEventArgs e) => UltimaOperacaoComposicao = ReproduzirComposicaoAsync(true);
    private void CancelarPreparacaoComposicao(object sender, RoutedEventArgs e)
    {
        preparacaoComposicao?.Cancel(); navegacaoTemporal?.Cancel(); buscaQuadro?.Cancel(); previa.Cancelar();
    }
    private void PararComposicao(object sender, RoutedEventArgs e) => UltimaOperacaoComposicao = PararComposicaoAsync();

    internal async Task ReproduzirComposicaoAsync(bool selecao)
    {
        if (sessao is null || Ocupado || previaEncerrada) return;
        if (preparacaoComposicao is not null) { Anunciar("Carregando vídeo. Você pode cancelar."); return; }
        navegacaoTemporal?.Cancel(); geracaoNavegacao++; navegandoTemporal = false;
        if (composicaoAtiva && previa.Atual.Estado == EstadoPrevia.Reproduzindo && !selecao)
        {
            await previa.PausarAsync(); SincronizarCursorComposicao();
            UltimoQuadro = ExibirQuadroAsync(sessao.Atual.Cursor); await UltimoQuadro; return;
        }
        var origem = sessao;
        var snapshot = origem.Atual;
        if (selecao && (snapshot.Selecao is null || snapshot.Selecao.Inicio == snapshot.Selecao.Fim)) return;
        using var cancelamento = new CancellationTokenSource();
        preparacaoComposicao = cancelamento;
        var finalizada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        preparacaoFinalizada = finalizada;
        try
        {
            OcultarQuadro();
            await UltimoQuadro;
            destinoManualComposicao = null;
            if (composicaoAtiva && previa.Atual.Estado is EstadoPrevia.Pausada or EstadoPrevia.Concluida && !selecao && !intervaloPreparado
                && await preparadorComposicao.EstaAtualizadaAsync(snapshot, cancelamento.Token))
            {
                cancelamento.Token.ThrowIfCancellationRequested();
                fimReproducaoSelecao = null;
                var destino = snapshot.Cursor >= snapshot.Duracao ? TempoMidia.Zero : snapshot.Cursor;
                if (previa.Atual.Estado == EstadoPrevia.Concluida || destino != previa.Posicao)
                    await previa.PosicionarAsync(destino);
                cancelamento.Token.ThrowIfCancellationRequested();
                origem.Posicionar(destino, reproducao: true);
                await previa.AlternarAsync();
                return;
            }
            await previa.LimparAsync();
            inicioArquivoComposicao = TempoMidia.Zero; intervaloPreparado = false;
            composicaoAtiva = true;
            fimReproducaoSelecao = selecao ? snapshot.Selecao!.Fim : null;
            AtualizarControlesPrevia();
            Anunciar("Carregando vídeo: 0%.");
            var progresso = new Progress<string>(mensagem =>
            {
                if (ReferenceEquals(preparacaoComposicao, cancelamento) && !cancelamento.IsCancellationRequested && Situacao.Text != mensagem) Anunciar(mensagem);
            });
            var midia = await preparadorComposicao.PrepararAsync(snapshot, progresso, cancelamento.Token);
            if (selecao && await preparadorComposicao.PrepararIntervaloAsync(snapshot, snapshot.Selecao!.Inicio, snapshot.Selecao.Fim, cancelamento.Token) is { } intervalo)
            {
                midia = intervalo; intervaloPreparado = true; inicioArquivoComposicao = snapshot.Selecao.Inicio;
            }
            cancelamento.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(origem, sessao)) return;
            var inicio = selecao ? snapshot.Selecao!.Inicio : snapshot.Cursor >= snapshot.Duracao ? TempoMidia.Zero : snapshot.Cursor;
            origem.Posicionar(inicio, reproducao: true);
            using var registro = cancelamento.Token.Register(previa.Cancelar);
            await previa.AbrirAsync(midia, inicio - inicioArquivoComposicao);
            cancelamento.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            await previa.LimparAsync(); composicaoAtiva = false;
            Anunciar("Preparação da composição cancelada.");
        }
        catch (Exception ex)
        {
            await previa.LimparAsync(); composicaoAtiva = false; Anunciar(ex.Message);
        }
        finally
        {
            preparacaoComposicao = null;
            finalizada.TrySetResult();
            AtualizarControlesPrevia(); AtualizarPosicaoPrevia();
        }
    }

    internal void SincronizarCursorComposicao()
    {
        if (!composicaoAtiva || sessao is null || preparacaoComposicao is not null || navegandoTemporal) return;
        if (previa.Atual.Estado is not (EstadoPrevia.Reproduzindo or EstadoPrevia.Pausada or EstadoPrevia.Concluida)) return;
        var posicao = previa.Atual.Estado == EstadoPrevia.Concluida ? intervaloPreparado && fimReproducaoSelecao is { } final ? final : sessao.Atual.Duracao
            : previa.Atual.Estado == EstadoPrevia.Pausada && destinoManualComposicao is { } manual ? manual
            : TempoMidia.Minimo(inicioArquivoComposicao + previa.Posicao, sessao.Atual.Duracao);
        if (fimReproducaoSelecao is { } fim) posicao = TempoMidia.Minimo(posicao, fim);
        sessao.Posicionar(posicao, reproducao: true);
        NavegadorTemporal.Content = $"Posição: {posicao} · Duração: {sessao.Atual.Duracao}";
        if (previa.Atual.Estado != EstadoPrevia.Reproduzindo)
            System.Windows.Automation.AutomationProperties.SetName(NavegadorTemporal, $"Posição {TempoFalado(posicao)}");
        if (!intervaloPreparado && fimReproducaoSelecao is { } limite && posicao >= limite && previa.Atual.Estado == EstadoPrevia.Reproduzindo && !encerrandoSelecao)
            UltimaOperacaoComposicao = ConcluirSelecaoAsync(limite);
    }

    private async Task ConcluirSelecaoAsync(TempoMidia fim)
    {
        encerrandoSelecao = true;
        try
        {
            await previa.PausarAsync();
            if (fim < sessao!.Atual.Duracao) await previa.PosicionarAsync(fim);
            sessao.Posicionar(fim, reproducao: true);
            destinoManualComposicao = fim;
            UltimoQuadro = ExibirQuadroAsync(fim); await UltimoQuadro;
            Anunciar("Reprodução do intervalo concluída.");
        }
        finally { encerrandoSelecao = false; }
    }

    private async Task PararComposicaoAsync()
    {
        if (!composicaoAtiva || sessao is null) return;
        navegacaoTemporal?.Cancel(); geracaoNavegacao++; navegandoTemporal = false;
        fimReproducaoSelecao = null;
        destinoManualComposicao = TempoMidia.Zero;
        if (intervaloPreparado) { await previa.LimparAsync(); intervaloPreparado = false; inicioArquivoComposicao = TempoMidia.Zero; }
        else await previa.PararAsync();
        sessao.Posicionar(TempoMidia.Zero, reproducao: true);
        UltimoQuadro = ExibirQuadroAsync(TempoMidia.Zero); await UltimoQuadro;
        AtualizarPosicaoPrevia();
    }

    private async Task InterromperComposicaoAsync(bool limpar)
    {
        if (limpar) { navegacaoTemporal?.Cancel(); geracaoNavegacao++; navegandoTemporal = false; }
        OcultarQuadro();
        await UltimoQuadro;
        if (limpar && navegacaoFinalizada is { } navegacao) await navegacao.Task;
        preparacaoComposicao?.Cancel(); previa.Cancelar();
        if (preparacaoComposicao is not null && preparacaoFinalizada is { } finalizada) await finalizada.Task;
        if (!composicaoAtiva) return;
        await previa.PausarAsync(); SincronizarCursorComposicao();
        if (limpar)
        {
            await previa.LimparAsync(); composicaoAtiva = false; destinoManualComposicao = null;
            fimReproducaoSelecao = null; intervaloPreparado = false; inicioArquivoComposicao = TempoMidia.Zero;
        }
    }

    private void RemoverDaBiblioteca(object sender, RoutedEventArgs e) => UltimaRemocaoBiblioteca = RemoverSelecionadasDaBibliotecaAsync();
    private void RemoverDaComposicao(object sender, RoutedEventArgs e) => UltimaOperacaoComposicao = RemoverDaComposicaoAsync();

    internal async Task RemoverDaComposicaoAsync()
    {
        if (sessao is null || LinhaDoTempo.SelectedItem is not Linha linha) { Anunciar("Selecione um elemento da composição para remover."); return; }
        if (!await PrepararEdicaoAsync()) return;
        var indice = LinhaDoTempo.SelectedIndex;
        var item = new AcoesEdicao(sessao).Conteudos().Single(c => c.Id == linha.Id);
        if (!await ExecutarAsync(item.EhCamada ? new RemoverCamada(item.Id) : new ExcluirIntervalo(item.Inicio, item.Inicio + item.Duracao))) return;
        if (LinhaDoTempo.Items.Count > 0) LinhaDoTempo.SelectedIndex = Math.Min(indice, LinhaDoTempo.Items.Count - 1);
        LinhaDoTempo.Focus();
        Anunciar(item.EhCamada ? "Camada removida. Control Z desfaz." : "Elemento removido e intervalo fechado, ajustando as camadas nesse intervalo. A mídia permanece na biblioteca. Control Z desfaz.");
    }
}
