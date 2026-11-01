using SonarFrame.Core;

namespace SonarFrame.Application;

public sealed record ImportFailure(string Path, string Message);
public sealed record ImportResult(IReadOnlyList<Guid> ImportedIds, IReadOnlyList<ImportFailure> Failures, bool IsCanceled = false);
public sealed record ImportProgress(int Completed, int Total, string CurrentFile);
public static class MediaImport
{
    public static async Task<ImportResult> ImportAsync(EditorSession session, IMediaProbe probe, IEnumerable<string> paths, CancellationToken token = default, IProgress<ImportProgress>? progress = null)
    {
        var imported = new List<Guid>();
        var failures = new List<ImportFailure>();
        var files = paths.ToArray();
        for (var i = 0; i < files.Length; i++)
        {
            if (token.IsCancellationRequested) return new(imported, failures, true);
            var path = files[i];
            progress?.Report(new(i, files.Length, Path.GetFileName(path)));
            try
            {
                var asset = await probe.ReadAsync(path, token);
                token.ThrowIfCancellationRequested();
                session.Execute(new RegisterMedia(asset));
                imported.Add(asset.Id);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { return new(imported, failures, true); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failures.Add(new(path, e.Message));
                session.Notify(new("import.failed", $"{path}: {e.Message}", true));
            }
            progress?.Report(new(i + 1, files.Length, Path.GetFileName(path)));
        }
        return new(imported, failures);
    }
    public static IReadOnlyList<MediaAsset> FindMissing(Project project) => project.Library.Where(a => !File.Exists(a.Path)).ToArray();
}
