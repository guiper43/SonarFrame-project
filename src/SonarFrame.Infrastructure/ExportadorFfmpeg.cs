using SonarFrame.Application;
using SonarFrame.Core;

namespace SonarFrame.Infrastructure;

public sealed class ExportadorFfmpeg(string ffmpeg, IExecutorFfprobe ffprobe) : IExportador, IAsyncDisposable
{
    private readonly PreparadorComposicaoFfmpeg motor = new(ffmpeg, ffprobe);
    private bool descartado;

    public Task ExportarAsync(Projeto snapshot, OpcoesExportacao opcoes, IProgress<ProgressoOperacao> progresso, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(descartado, this);
        return motor.ExportarAsync(snapshot, opcoes, progresso, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (descartado) return ValueTask.CompletedTask;
        descartado = true;
        return motor.DisposeAsync();
    }
}
