using System.IO;
using System.Diagnostics;
using System.Text.Json;
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
    private bool composicaoProgressiva;
    private bool cancelamentoExplicitoComposicao;
    internal Task UltimaOperacaoComposicao { get; private set; } = Task.CompletedTask;

    private void ConfigurarComposicao(IPreparadorComposicao? preparador = null)
    {
        preparadorComposicao = preparador ?? new PreparadorComposicaoFfmpeg(
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"),
            new ExecutorFfprobe(Path.Combine(AppContext.BaseDirectory, "tools", "ffprobe.exe")));
    }

    private void AlternarComposicao(object sender, RoutedEventArgs e) => UltimaOperacaoComposicao = ReproduzirComposicaoAsync(false);
    private void ReproduzirSelecaoComposicao(object sender, RoutedEventArgs e) => UltimaOperacaoComposicao = ReproduzirComposicaoAsync(true);
    private void CancelarPreparacaoComposicao(object sender, RoutedEventArgs e)
    {
        cancelamentoExplicitoComposicao = true;
        preparacaoComposicao?.Cancel(); navegacaoTemporal?.Cancel(); buscaQuadro?.Cancel(); previa.Cancelar();
        if (preparacaoComposicao is null && composicaoProgressiva) UltimaOperacaoComposicao = InterromperComposicaoAsync(true);
    }
    private void PararComposicao(object sender, RoutedEventArgs e) => UltimaOperacaoComposicao = PararComposicaoAsync();

    internal async Task ReproduzirComposicaoAsync(bool selecao)
    {
        if (sessao is null || Ocupado || previaEncerrada) return;
        if (preparacaoComposicao is not null) { Anunciar("Carregando vídeo. Você pode cancelar."); return; }
        if (composicaoProgressiva && previa.Atual.Estado == EstadoPrevia.Preparando && !selecao)
        {
            await previa.PausarAsync(); SincronizarCursorComposicao(); return;
        }
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
        cancelamentoExplicitoComposicao = false;
        var finalizada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        preparacaoFinalizada = finalizada;
        var disponibilidade = Stopwatch.StartNew();
        var inicioMedicao = Stopwatch.GetTimestamp();
        var executouPreparacao = false;
        DiagnosticoPreparacao? diagnosticoPreparacao = null;
        double intervaloMs = 0;
        IFonteComposicao? fonte = null;
        try
        {
            OcultarQuadro();
            await UltimoQuadro;
            destinoManualComposicao = null;
            if (composicaoProgressiva && previa.Atual.Estado == EstadoPrevia.Pausada && !selecao
                && fimReproducaoSelecao is null && snapshot.Cursor == inicioArquivoComposicao + previa.Posicao
                && await preparadorComposicao.EstaAtualizadaAsync(snapshot, cancelamento.Token))
            {
                await previa.AlternarAsync(); cancelamento.Token.ThrowIfCancellationRequested(); return;
            }
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
            inicioArquivoComposicao = TempoMidia.Zero; intervaloPreparado = false; composicaoProgressiva = false;
            composicaoAtiva = true;
            fimReproducaoSelecao = selecao ? snapshot.Selecao!.Fim : null;
            AtualizarControlesPrevia();
            Anunciar("Carregando vídeo: 0%.");
            var progresso = new Progress<string>(mensagem =>
            {
                if (ReferenceEquals(preparacaoComposicao, cancelamento) && !cancelamento.IsCancellationRequested && Situacao.Text != mensagem) Anunciar(mensagem);
            });
            executouPreparacao = true;
            var inicio = selecao ? snapshot.Selecao!.Inicio : snapshot.Cursor >= snapshot.Duracao ? TempoMidia.Zero : snapshot.Cursor;
            fonte = await preparadorComposicao.IniciarReproducaoAsync(snapshot, inicio, selecao ? snapshot.Selecao!.Fim : snapshot.Duracao, progresso, cancelamento.Token);
            var midia = fonte is null ? await preparadorComposicao.PrepararAsync(snapshot, progresso, cancelamento.Token)
                : new Midia(Guid.NewGuid(), "Composição", "", TipoMidia.Video, selecao ? fonte.Duracao : snapshot.Duracao, new(30, 1));
            if (fonte is not null) { composicaoProgressiva = true; intervaloPreparado = true; inicioArquivoComposicao = fonte.Inicio; }
            diagnosticoPreparacao = (preparadorComposicao as PreparadorComposicaoFfmpeg)?.UltimoDiagnostico;
            if (selecao && fonte is null)
            {
                var relogioIntervalo = Stopwatch.StartNew();
                try
                {
                    if (await preparadorComposicao.PrepararIntervaloAsync(snapshot, snapshot.Selecao!.Inicio, snapshot.Selecao.Fim, cancelamento.Token) is { } intervalo)
                    {
                        midia = intervalo; intervaloPreparado = true; inicioArquivoComposicao = snapshot.Selecao.Inicio;
                    }
                }
                finally { intervaloMs = relogioIntervalo.Elapsed.TotalMilliseconds; }
            }
            cancelamento.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(origem, sessao)) return;
            origem.Posicionar(inicio, reproducao: true);
            using var registro = cancelamento.Token.Register(previa.Cancelar);
            await previa.AbrirAsync(midia, inicio - inicioArquivoComposicao, fonte: fonte);
            fonte = null;
            cancelamento.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            await previa.LimparAsync(); composicaoAtiva = false;
            if (cancelamentoExplicitoComposicao) Anunciar("Preparação da composição cancelada.");
        }
        catch (Exception ex)
        {
            await previa.LimparAsync(); composicaoAtiva = false; Anunciar(ex.Message);
        }
        finally
        {
            if (fonte is not null) await fonte.DisposeAsync();
            disponibilidade.Stop();
            if (preparadorComposicao is PreparadorComposicaoFfmpeg medido)
            {
                var diagnostico = new
                {
                    DataUtc = DateTime.UtcNow,
                    Disponivel = previa.Atual.Estado == EstadoPrevia.Reproduzindo,
                    TempoAteComandoConcluidoMs = disponibilidade.Elapsed.TotalMilliseconds,
                    PreparacaoIntervaloMs = intervaloMs,
                    PreparacaoExecutada = executouPreparacao,
                    Preparacao = executouPreparacao ? diagnosticoPreparacao ?? medido.UltimoDiagnostico : null
                };
                try
                {
                    var diretorio = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sonarFrame");
                    Directory.CreateDirectory(diretorio);
                    _ = RegistrarSaidaAsync(diretorio, diagnostico, inicioMedicao, previa.Atual.Estado == EstadoPrevia.Reproduzindo ? SaidaNativa : null);
                }
                catch (Exception erro) when (erro is IOException or UnauthorizedAccessException) { Debug.WriteLine(erro.Message); }
            }
            preparacaoComposicao = null;
            finalizada.TrySetResult();
            AtualizarControlesPrevia(); AtualizarPosicaoPrevia();
        }
    }

    private static async Task RegistrarSaidaAsync(string diretorio, object comando, long inicio, Task<DiagnosticoSaida>? observacao)
    {
        try
        {
            var saida = observacao is null ? null : await observacao;
            double? Decorrido(long? instante) => instante is { } valor && valor >= inicio ? Stopwatch.GetElapsedTime(inicio, valor).TotalMilliseconds : null;
            var diagnostico = new
            {
                Comando = comando,
                InicializacaoLibVlcMs = saida?.InicializacaoLibVlcMs,
                PrimeiroQuadroApresentadoMs = Decorrido(saida?.PrimeiroQuadro),
                PrimeiroBufferAudioMs = Decorrido(saida?.PrimeiroAudio)
            };
            await File.WriteAllTextAsync(Path.Combine(diretorio, "diagnostico-reproducao.json"), JsonSerializer.Serialize(diagnostico, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception erro) when (erro is IOException or UnauthorizedAccessException) { Debug.WriteLine(erro.Message); }
    }

    internal void SincronizarCursorComposicao()
    {
        if (!composicaoAtiva || sessao is null || preparacaoComposicao is not null || navegandoTemporal) return;
        if (previa.Atual.Estado is not (EstadoPrevia.Reproduzindo or EstadoPrevia.Pausada or EstadoPrevia.Concluida)
            && !(composicaoProgressiva && previa.Atual.Estado == EstadoPrevia.Preparando)) return;
        var posicao = previa.Atual.Estado == EstadoPrevia.Concluida ? intervaloPreparado && fimReproducaoSelecao is { } final ? final : sessao.Atual.Duracao
            : previa.Atual.Estado == EstadoPrevia.Pausada && destinoManualComposicao is { } manual ? manual
            : TempoMidia.Minimo(inicioArquivoComposicao + previa.Posicao, sessao.Atual.Duracao);
        if (fimReproducaoSelecao is { } fim) posicao = TempoMidia.Minimo(posicao, fim);
        sessao.Posicionar(posicao, reproducao: true);
        NavegadorTemporal.Text = $"Posição: {posicao} · Duração: {sessao.Atual.Duracao}";
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
        if (intervaloPreparado) { await previa.LimparAsync(); intervaloPreparado = false; composicaoProgressiva = false; inicioArquivoComposicao = TempoMidia.Zero; }
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
            fimReproducaoSelecao = null; intervaloPreparado = false; composicaoProgressiva = false; inicioArquivoComposicao = TempoMidia.Zero;
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
