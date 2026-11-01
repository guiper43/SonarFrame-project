using System.Diagnostics;
using System.Security.Cryptography;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Tests;

public sealed class MediaToolsFactAttribute : FactAttribute
{
    public MediaToolsFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")) || !File.Exists(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")))
            Skip = "Execute setup-media.ps1 e build.ps1 para habilitar a integração com FFmpeg real.";
    }
}

public sealed class RealMediaTests
{
    [MediaToolsFact]
    public async Task RealFormatsBatchImportAndSqlitePreserveOriginals()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "real-media", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var paths = new List<string>();
            foreach (var (extension, codec) in new[] { ("mp3", "libmp3lame"), ("wav", "pcm_s16le"), ("m4a", "aac"), ("ogg", "libopus") })
            {
                var path = Path.Combine(folder, $"áudio d'exemplo.{extension}");
                await Generate("-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "0.5", "-c:a", codec, path);
                paths.Add(path);
            }
            foreach (var extension in new[] { "mp4", "mov" })
            {
                var path = Path.Combine(folder, $"vídeo de teste.{extension}");
                await Generate("-f", "lavfi", "-i", "color=c=blue:s=64x64:r=30000/1001", "-t", "1.001", "-c:v", "libx264", "-pix_fmt", "yuv420p", path);
                paths.Add(path);
            }
            foreach (var extension in new[] { "png", "jpg", "jpeg" })
            {
                var path = Path.Combine(folder, $"imagem.{extension}");
                await Generate("-f", "lavfi", "-i", "color=c=blue:s=64x64", "-frames:v", "1", "-update", "1", path);
                paths.Add(path);
            }
            var hashes = paths.Select(p => SHA256.HashData(File.ReadAllBytes(p))).ToArray();
            var broken = Path.Combine(folder, "corrompido.mp4");
            await File.WriteAllTextAsync(broken, "not media"); paths.Insert(3, broken);
            var session = new EditorSession(Project.Create("Importação real"));
            var probe = new FfprobeMediaProbe(new FfprobeRunner(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!));
            var result = await MediaImport.ImportAsync(session, probe, paths);
            Assert.Equal(9, result.ImportedIds.Count); Assert.Single(result.Failures);
            Assert.Empty(session.Current.Timeline);
            Assert.Equal(4, session.Current.Library.Count(a => a.Kind == MediaKind.Audio));
            Assert.Equal(3, session.Current.Library.Count(a => a.Kind == MediaKind.Image));
            Assert.All(session.Current.Library.Where(a => a.Kind == MediaKind.Video), a =>
            {
                Assert.Equal(new FrameRate(30000, 1001), a.FrameRate);
                Assert.Equal(new MediaTime(1001, 1000), a.Duration);
            });
            session.Execute(new InsertClips([.. result.ImportedIds], MediaTime.Seconds(3)));
            var store = new SqliteProjectStore(); var projectFile = Path.Combine(folder, "project.sonarframe");
            await store.SaveAsync(projectFile, session.Current);
            var reopened = await store.LoadAsync(projectFile);
            Assert.Equal(9, reopened.Library.Length); Assert.Equal(9, reopened.Timeline.Length);
            paths.Remove(broken);
            for (var i = 0; i < paths.Count; i++) Assert.Equal(hashes[i], SHA256.HashData(await File.ReadAllBytesAsync(paths[i])));
        }
        finally { Directory.Delete(folder, true); }
    }

    [MediaToolsFact]
    public async Task RealProcessHonorsCancellationAndTimeout()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "probe-cancellation-" + Guid.NewGuid() + ".mp4");
        await File.WriteAllTextAsync(path, "fixture");
        try
        {
            var runner = new FfprobeRunner(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!, TimeSpan.FromTicks(1));
            await Assert.ThrowsAsync<TimeoutException>(() => runner.InspectAsync(path, default));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.InspectAsync(path, new CancellationToken(true)));
        }
        finally { File.Delete(path); }
    }

    private static async Task Generate(params string[] arguments)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        start.ArgumentList.Add("-v"); start.ArgumentList.Add("error"); start.ArgumentList.Add("-y");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            Assert.True(process.ExitCode == 0, await error);
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
}
