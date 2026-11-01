using System.Collections.Immutable;
using System.Text.Json;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Tests;

public sealed class MediaImportTests : IDisposable
{
    private readonly string folder = Path.Combine(AppContext.BaseDirectory, "probe-tests", Guid.NewGuid().ToString("N"));
    public MediaImportTests() => Directory.CreateDirectory(folder);
    public void Dispose() => Directory.Delete(folder, true);
    private string FileNamed(string name) { var path = Path.Combine(folder, name); File.WriteAllText(path, "test fixture"); return path; }
    private sealed class StubRunner(string json) : IFfprobeRunner
    { public Task<string> InspectAsync(string fullPath, CancellationToken token) => Task.FromResult(json); }

    [Fact] public async Task ExactStreamDurationAndFrameRateTakePrecedenceOverContainer()
    {
        var probe = new FfprobeMediaProbe(new StubRunner("""
        {"streams":[{"codec_type":"video","codec_name":"h264","duration_ts":30030,"time_base":"1/30000","avg_frame_rate":"30000/1001","r_frame_rate":"30000/1001"}],"format":{"duration":"20.5"}}
        """));
        var asset = await probe.ReadAsync(FileNamed("vídeo com espaços.mp4"));
        Assert.Equal(new MediaTime(1001, 1000), asset.Duration);
        Assert.Equal(new FrameRate(30000, 1001), asset.FrameRate);
        Assert.False(asset.VariableFrameRate);
        Assert.True(Path.IsPathFullyQualified(asset.Path));
    }
    [Fact] public async Task AudioCoverArtDoesNotBecomeVideo()
    {
        var probe = new FfprobeMediaProbe(new StubRunner("""
        {"streams":[{"codec_type":"video","codec_name":"mjpeg","disposition":{"attached_pic":1}},{"codec_type":"audio","codec_name":"opus"}],"format":{"duration":"1.234567"}}
        """));
        var asset = await probe.ReadAsync(FileNamed("mensagem.ogg"));
        Assert.Equal(MediaKind.Audio, asset.Kind); Assert.Null(asset.FrameRate);
        Assert.Equal(new MediaTime(1234567, 1000000), asset.Duration);
    }
    [Fact] public async Task ImageHasNoIntrinsicTimelineDuration()
    {
        var probe = new FfprobeMediaProbe(new StubRunner("""{"streams":[{"codec_type":"video","codec_name":"png"}]}"""));
        var asset = await probe.ReadAsync(FileNamed("foto.PNG"));
        Assert.Equal(MediaKind.Image, asset.Kind); Assert.Null(asset.Duration);
    }
    [Theory]
    [InlineData("{}")] [InlineData("not json")]
    [InlineData("{\"streams\":[{\"codec_type\":\"video\",\"codec_name\":\"unknown\"}]}")]
    [InlineData("{\"streams\":[{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"duration\":\"0\"}]}")]
    public async Task InvalidMetadataIsRejected(string json)
    {
        var probe = new FfprobeMediaProbe(new StubRunner(json));
        await Assert.ThrowsAsync<InvalidDataException>(() => probe.ReadAsync(FileNamed("invalid.mp4")));
    }
    [Fact] public async Task WrongExtensionAndMissingFileAreRejected()
    {
        var probe = new FfprobeMediaProbe(new StubRunner("{}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => probe.ReadAsync(FileNamed("file.exe")));
        await Assert.ThrowsAsync<FileNotFoundException>(() => probe.ReadAsync(Path.Combine(folder, "missing.mov")));
    }
    [Fact] public async Task DifferentRatesMarkPotentialVariableFrameRate()
    {
        var probe = new FfprobeMediaProbe(new StubRunner("""{"streams":[{"codec_type":"video","codec_name":"hevc","duration":"2","avg_frame_rate":"25/1","r_frame_rate":"30/1"}]}"""));
        Assert.True((await probe.ReadAsync(FileNamed("iphone.mov"))).VariableFrameRate);
    }
    [Fact] public async Task MissingExecutableHasUsefulError()
    {
        var runner = new FfprobeRunner(Path.Combine(folder, "does-not-exist.exe"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.InspectAsync(FileNamed("video.mp4"), default));
        Assert.Contains("ffprobe", error.Message);
    }
    [Fact] public async Task CancellationPreservesImportedItemsAndDoesNotInsertIntoTimeline()
    {
        var session = new EditorSession(Project.Create("Lote"));
        using var cancellation = new CancellationTokenSource();
        var result = await MediaImport.ImportAsync(session, new CancelingProbe(cancellation), ["first", "second", "third"], cancellation.Token);
        Assert.True(result.IsCanceled); Assert.Single(result.ImportedIds);
        Assert.Single(session.Current.Library); Assert.Empty(session.Current.Timeline);
    }
    private sealed class CancelingProbe(CancellationTokenSource cancellation) : IMediaProbe
    {
        public Task<MediaAsset> ReadAsync(string path, CancellationToken token = default)
        {
            if (path == "second") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            return Task.FromResult(EditingTests.Video());
        }
    }
    [Fact] public void BatchInsertionIsAtomicAndOneUndoRestoresLibraryOnly()
    {
        var session = new EditorSession(Project.Create("Lote"));
        var video = EditingTests.Video();
        var photo = new MediaAsset(Guid.NewGuid(), "Foto", "foto.png", MediaKind.Image, null);
        session.Execute(new RegisterMedia(video)); session.Execute(new RegisterMedia(photo));
        var before = session.Current;
        Assert.Throws<ArgumentException>(() => session.Execute(new InsertClips([video.Id, photo.Id])));
        Assert.Equal(before, session.Current);
        session.Execute(new InsertClips([video.Id, photo.Id], MediaTime.Seconds(5)));
        Assert.Equal(MediaTime.Seconds(15), session.Current.Duration);
        session.Undo(); Assert.Equal(before, session.Current);
    }
}
