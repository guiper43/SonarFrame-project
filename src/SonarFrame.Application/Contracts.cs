using SonarFrame.Core;

namespace SonarFrame.Application;

public interface IProjectStore
{
    Task SaveAsync(string path, Project project, CancellationToken cancellationToken = default);
    Task<Project> LoadAsync(string path, CancellationToken cancellationToken = default);
}
public interface IMediaProbe
{
    Task<MediaAsset> ReadAsync(string path, CancellationToken cancellationToken = default);
}
/// <summary>Must return a source timestamp in the requested direction; required for variable frame rate.</summary>
public interface IFrameNavigator
{
    MediaTime NextFrame(MediaAsset asset, MediaTime sourcePosition, int direction);
}
public interface IPreviewPlayer
{
    Task LoadAsync(Project snapshot, CancellationToken cancellationToken = default);
    Task SeekAsync(MediaTime position, CancellationToken cancellationToken = default);
    Task PlayAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
}
public enum Orientation { Horizontal, Vertical }
public enum FitMode { Fill, Letterbox }
public sealed record ExportOptions(string Destination, Orientation? Orientation, FitMode Fit, int Height = 1080)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Destination) || Orientation is null || !Enum.IsDefined(Orientation.Value) || !Enum.IsDefined(Fit) || Height is not (720 or 1080))
            throw new ArgumentException("Escolha destino, orientação e resolução válidos.");
    }
}
public sealed record OperationProgress(string State, double? Percent, TimeSpan? Remaining);
public interface IExporter
{
    Task ExportAsync(Project snapshot, ExportOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken = default);
}
public sealed record EditorNotification(string Code, string Message, bool IsError = false);
