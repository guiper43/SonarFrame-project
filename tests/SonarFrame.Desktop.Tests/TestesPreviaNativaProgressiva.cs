using System.IO;
using System.Windows.Input;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class TestesPreviaNativaProgressiva
{
    [PreviaNativaFact]
    public Task FaltaDeBlocosPausaPreservaCursorERetomaNoMesmoPlayer() => ComAtraso(async (janela, atraso) =>
    {
        var preparando = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retomou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        janela.PreviaAtual.Alterado += e =>
        {
            if (e.Estado == EstadoPrevia.Preparando && e.Mensagem?.Contains("continuará") == true) preparando.TrySetResult();
            if (e.Estado == EstadoPrevia.Reproduzindo && preparando.Task.IsCompleted) retomou.TrySetResult();
        };
        await janela.ReproduzirComposicaoAsync(false).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(EstadoPrevia.Reproduzindo, janela.PreviaAtual.Atual.Estado);
        Assert.False(atraso.Liberacao.Task.IsCompleted);
        await preparando.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var view = Assert.IsType<LibVLCSharp.WPF.VideoView>(janela.SuperficiePrevia.Content);
        var posicao = janela.PreviaAtual.Posicao;
        var foco = Keyboard.FocusedElement;
        await Task.Delay(2200);
        Assert.Equal(EstadoPrevia.Preparando, janela.PreviaAtual.Atual.Estado);
        Assert.Equal("Pausar", janela.BotaoReproduzirPrevia.Content);
        Assert.Equal("Pausar", System.Windows.Automation.AutomationProperties.GetName(janela.BotaoReproduzirPrevia));
        Assert.Equal(posicao, janela.PreviaAtual.Posicao);
        Assert.Same(foco, Keyboard.FocusedElement);
        Assert.Equal(LibVLCSharp.Shared.VLCState.Paused, view.MediaPlayer!.State);
        atraso.Liberacao.TrySetResult();
        await retomou.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Same(view, janela.SuperficiePrevia.Content);
        Assert.False(view.MediaPlayer.Mute);
        Assert.True(janela.PreviaAtual.Posicao >= posicao);
        await janela.LimparPreviaAsync();
    });

    [PreviaNativaFact]
    public Task CancelarRecuperacaoEncerraProdutorEPermiteReabrir() => ComAtraso(async (janela, atraso) =>
    {
        var preparando = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        janela.PreviaAtual.Alterado += e => { if (e.Estado == EstadoPrevia.Preparando && e.Mensagem?.Contains("continuará") == true) preparando.TrySetResult(); };
        await janela.ReproduzirComposicaoAsync(false).WaitAsync(TimeSpan.FromSeconds(20));
        await preparando.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await janela.LimparPreviaAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EstadoPrevia.Vazia, janela.PreviaAtual.Atual.Estado);
        Assert.True(atraso.Cancelado);
        atraso.Liberacao.TrySetResult();
        await janela.ReproduzirComposicaoAsync(false).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(EstadoPrevia.Reproduzindo, janela.PreviaAtual.Atual.Estado);
        await janela.LimparPreviaAsync();
    });

    private static Task ComAtraso(Func<JanelaPrincipal, LeitorAtrasado, Task> corpo)
    {
        var ferramentas = Path.Combine(AppContext.BaseDirectory, "tools");
        var atraso = new LeitorAtrasado(new ExecutorFfprobe(Path.Combine(ferramentas, "ffprobe.exe")));
        var preparador = new PreparadorComposicaoFfmpeg(Path.Combine(ferramentas, "ffmpeg.exe"), atraso);
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var origem = Path.Combine(Environment.GetEnvironmentVariable("SONARFRAME_MIDIAS_PREVIA")!, "origem.mp4");
            var lenta = Path.Combine(Path.GetDirectoryName(caminho)!, "lenta.mp4");
            File.Copy(origem, lenta);
            var primeira = new Midia(Guid.NewGuid(), "Primeira", origem, TipoMidia.Video, TempoMidia.Segundos(8));
            var segunda = primeira with { Id = Guid.NewGuid(), Nome = "Segunda", Caminho = lenta };
            var projeto = Projeto.Criar("Recuperação") with
            {
                Biblioteca = [primeira, segunda],
                LinhaDoTempo = [new(Guid.NewGuid(), primeira.Id, "Primeiro", TempoMidia.Zero, TempoMidia.Segundos(5)), new(Guid.NewGuid(), segunda.Id, "Segundo", TempoMidia.Zero, TempoMidia.Segundos(5))]
            };
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            janela.ExibirProjeto(projeto, salvo: true, caminho: caminho);
            try { await corpo(janela, atraso); }
            finally { atraso.Liberacao.TrySetResult(); }
        }, preparador: preparador, prazoSegundos: 120);
    }

    private sealed class LeitorAtrasado(IExecutorFfprobe original) : IExecutorFfprobe
    {
        public TaskCompletionSource Liberacao { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelado { get; private set; }
        public Task<string> InspecionarAsync(string caminho, CancellationToken token) => original.InspecionarAsync(caminho, token);
        public Task<string> InspecionarQuadrosAsync(string caminho, CancellationToken token) => original.InspecionarQuadrosAsync(caminho, token);
        public async Task<string> InspecionarJanelaQuadrosAsync(string caminho, TempoMidia inicio, TempoMidia fim, CancellationToken token)
        {
            if (Path.GetFileName(caminho) == "lenta.mp4")
            {
                try { await Liberacao.Task.WaitAsync(token); }
                catch (OperationCanceledException) { Cancelado = true; throw; }
            }
            return await original.InspecionarJanelaQuadrosAsync(caminho, inicio, fim, token);
        }
    }
}
