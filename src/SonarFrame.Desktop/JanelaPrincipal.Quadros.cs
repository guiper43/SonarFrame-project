using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SonarFrame.Core;

namespace SonarFrame.Desktop;

public partial class JanelaPrincipal
{
    private CancellationTokenSource? buscaQuadro;
    private CancellationTokenSource? navegacaoTemporal;
    private TaskCompletionSource? navegacaoFinalizada;
    private long geracaoNavegacao;
    private bool navegandoTemporal;
    internal Task UltimaNavegacaoTemporal { get; private set; } = Task.CompletedTask;
    internal Task UltimoQuadro { get; private set; } = Task.CompletedTask;

    private void OcultarQuadro()
    {
        buscaQuadro?.Cancel();
        QuadroDaComposicao.Visibility = Visibility.Collapsed;
        QuadroDaComposicao.Source = null;
    }

    private async Task<bool> ExibirQuadroAsync(TempoMidia posicao)
    {
        if (!composicaoAtiva || sessao is null) return false;
        var anterior = UltimoQuadro;
        buscaQuadro?.Cancel();
        using var cancelamento = new CancellationTokenSource();
        buscaQuadro = cancelamento;
        QuadroDaComposicao.Visibility = Visibility.Collapsed; QuadroDaComposicao.Source = null;
        SuperficiePrevia.Visibility = Visibility.Collapsed;
        var origem = sessao;
        try
        {
            await anterior;
            cancelamento.Token.ThrowIfCancellationRequested();
            var quadro = await preparadorComposicao.ObterQuadroAsync(origem.Atual, posicao, cancelamento.Token);
            if (quadro is null) return false;
            var bitmap = await Task.Run(() =>
            {
                using var stream = new MemoryStream(quadro.Png);
                var imagem = new BitmapImage(); imagem.BeginInit(); imagem.CacheOption = BitmapCacheOption.OnLoad;
                imagem.StreamSource = stream; imagem.EndInit(); imagem.Freeze(); return imagem;
            }, cancelamento.Token);
            cancelamento.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(sessao, origem) || !composicaoAtiva || previaEncerrada) return false;
            QuadroDaComposicao.Source = bitmap;
            SuperficiePrevia.Visibility = Visibility.Collapsed;
            QuadroDaComposicao.Visibility = Visibility.Visible;
            System.Windows.Automation.AutomationProperties.SetName(QuadroDaComposicao, $"Vídeo em {TempoFalado(quadro.Posicao)}");
            return true;
        }
        catch (OperationCanceledException) when (cancelamento.IsCancellationRequested) { }
        catch (Exception ex) { if (!cancelamento.IsCancellationRequested) Anunciar(ex.Message); }
        finally { if (ReferenceEquals(buscaQuadro, cancelamento)) buscaQuadro = null; }
        return false;
    }

    internal async Task NavegarTemporalAsync(Key tecla, ModifierKeys modificadores, TempoMidia? destinoSolicitado = null)
    {
        if (sessao is null) return;
        navegacaoTemporal?.Cancel();
        var anterior = navegacaoFinalizada?.Task ?? Task.CompletedTask;
        var finalizada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        navegacaoFinalizada = finalizada;
        using var cancelamento = new CancellationTokenSource();
        navegacaoTemporal = cancelamento;
        var versao = ++geracaoNavegacao;
        if (!navegandoTemporal) SincronizarCursorComposicao();
        navegandoTemporal = true;
        buscaQuadro?.Cancel();
        var origem = sessao;
        var shift = modificadores.HasFlag(ModifierKeys.Shift);
        try
        {
            AtualizarControlesPrevia();
            await anterior;
            cancelamento.Token.ThrowIfCancellationRequested();
            await InterromperComposicaoAsync(false);
            cancelamento.Token.ThrowIfCancellationRequested();
            if (versao != geracaoNavegacao || !ReferenceEquals(origem, sessao)) return;
            var posicaoAnterior = origem.Atual.Cursor;
            if (destinoSolicitado is { } solicitado) origem.Posicionar(solicitado, shift);
            else switch (tecla)
            {
                case Key.Left: case Key.Right:
                    var direcao = tecla == Key.Left ? -1 : 1;
                    if (modificadores.HasFlag(ModifierKeys.Control)) origem.Deslocar(TempoMidia.Segundos(direcao), shift);
                    else
                    {
                        var posicao = await preparadorComposicao.ProximaPosicaoAsync(origem.Atual, direcao, cancelamento.Token);
                        cancelamento.Token.ThrowIfCancellationRequested();
                        if (versao != geracaoNavegacao || !ReferenceEquals(origem, sessao)) return;
                        origem.Posicionar(posicao, shift);
                    }
                    break;
                case Key.Home: origem.Posicionar(TempoMidia.Zero, shift); break;
                case Key.End: origem.Posicionar(origem.Atual.Duracao, shift); break;
                case Key.PageUp: origem.Deslocar(TempoMidia.Segundos(-10), shift); break;
                case Key.PageDown: origem.Deslocar(TempoMidia.Segundos(10), shift); break;
                default: return;
            }
            var destino = origem.Atual.Cursor;
            Atualizar();
            if (versao != geracaoNavegacao || !ReferenceEquals(origem, sessao)) return;
            if (intervaloPreparado)
            {
                await previa.LimparAsync(); intervaloPreparado = false; composicaoProgressiva = false; inicioArquivoComposicao = TempoMidia.Zero;
            }
            fimReproducaoSelecao = null;
            if (composicaoAtiva && previa.Atual.Midia?.Duracao is { } duracao)
            {
                var ultimo = TempoMidia.Maximo(TempoMidia.Zero, duracao - new TempoMidia(1, 30));
                await previa.PosicionarAsync(TempoMidia.Minimo(destino, ultimo));
                if (versao != geracaoNavegacao) return;
                origem.Posicionar(destino, reproducao: true);
                destinoManualComposicao = destino;
            }
            else if (!origem.Atual.LinhaDoTempo.IsEmpty)
            {
                await previa.LimparAsync();
                if (versao != geracaoNavegacao) return;
                composicaoAtiva = true;
                destinoManualComposicao = destino;
            }
            if (composicaoAtiva)
            {
                TituloPrevia.Text = "Prévia da composição pausada.";
                PosicaoPrevia.Text = $"Posição: {destino} de {origem.Atual.Duracao}";
                var quadro = ExibirQuadroAsync(destino);
                UltimoQuadro = quadro;
                if (!await quadro) return;
            }
            if (versao == geracaoNavegacao)
            {
                Atualizar();
                Anunciar(shift && origem.Atual.Selecao is null ? $"Posição {destino}. Nenhum intervalo selecionado." : AnuncioDeslocamento(posicaoAnterior, destino, shift));
            }
        }
        catch (OperationCanceledException) when (cancelamento.IsCancellationRequested) { }
        catch (Exception ex) { if (versao == geracaoNavegacao) Anunciar(ex.Message); }
        finally
        {
            if (versao == geracaoNavegacao) navegandoTemporal = false;
            if (ReferenceEquals(navegacaoTemporal, cancelamento)) navegacaoTemporal = null;
            finalizada.TrySetResult();
            AtualizarControlesPrevia();
        }
    }
}
