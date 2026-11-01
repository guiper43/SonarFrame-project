using SonarFrame.Application;
using SonarFrame.Core;
using Xunit;

namespace SonarFrame.Tests;

public class EditingTests
{
    internal static MediaTime S(long value) => MediaTime.Seconds(value);
    internal static MediaAsset Video(long seconds = 10) => new(Guid.NewGuid(), "Vídeo", "original.mp4", MediaKind.Video, S(seconds), new(30000, 1001));
    internal static EditorSession Session()
    {
        var s = new EditorSession(Project.Create("Teste"));
        var video = Video(); s.Execute(new RegisterMedia(video)); s.Execute(new InsertClip(video.Id, 0));
        return s;
    }
    [Fact] public void ImportDoesNotInsertIntoTimeline()
    {
        var s = new EditorSession(Project.Create("Vazio"));
        s.Execute(new RegisterMedia(Video())); s.Execute(new RegisterMedia(Video()));
        Assert.Equal(2, s.Current.Library.Length); Assert.Empty(s.Current.Timeline);
    }
    [Fact] public void SplitMoveDeleteUndoRedoPreserveSource()
    {
        var s = Session(); var asset = s.Current.Library[0];
        s.Execute(new SplitClip(s.Current.Timeline[0].Id, S(4)));
        Assert.Equal(S(4), s.Current.Timeline[1].SourceStart);
        Assert.Equal(S(10), s.Current.Duration);
        s.Execute(new MoveClip(s.Current.Timeline[1].Id, 0));
        Assert.Equal(S(4), s.Current.Timeline[0].SourceStart);
        var before = s.Current;
        s.Execute(new DeleteRange(S(2), S(8)));
        Assert.Equal(S(4), s.Current.Duration);
        Assert.Equal(S(2), s.Current.Cursor);
        var after = s.Current; s.Undo(); Assert.Equal(before, s.Current);
        s.Redo(); Assert.Equal(after, s.Current); Assert.Equal(asset, s.Current.Library[0]);
    }
    [Fact] public void InvalidEditIsAtomicAndKeepsRedo()
    {
        var s = Session(); s.Execute(new SplitClip(s.Current.Timeline[0].Id, S(4))); s.Undo();
        var before = s.Current;
        Assert.Throws<ArgumentException>(() => s.Execute(new SetVolume(s.Current.Timeline[0].Id, 201)));
        Assert.Equal(before, s.Current); Assert.True(s.CanRedo); Assert.True(s.Messages.Last().IsError);
        s.Execute(new SetVolume(s.Current.Timeline[0].Id, 0)); Assert.False(s.CanRedo);
    }
    [Theory]
    [InlineData(0, 10, 0)] [InlineData(0, 3, 7)] [InlineData(7, 10, 7)] [InlineData(3, 7, 6)]
    public void DeleteRangesHaveNoGap(int start, int end, int duration)
    {
        var s = Session(); s.Execute(new DeleteRange(S(start), S(end)));
        Assert.Equal(S(duration), s.Current.Duration); s.Current.Validate();
    }
    [Fact] public void RippleDeleteTrimsAndShiftsIndependentAudio()
    {
        var s = Session(); var audio = Video() with { Kind = MediaKind.Audio };
        s.Execute(new RegisterMedia(audio)); s.Execute(new AddLayer(audio.Id, S(1), S(0), S(8)));
        s.Execute(new DeleteRange(S(3), S(6)));
        Assert.Equal(2, s.Current.Layers.Length);
        Assert.Equal(S(2), s.Current.Layers[0].Clip.Duration);
        Assert.Equal(S(3), s.Current.Layers[1].Start);
        Assert.Equal(S(5), s.Current.Layers[1].Clip.SourceStart);
        Assert.Equal(S(3), s.Current.Layers[1].Clip.Duration);
    }
    [Fact] public void AudioVolumeDoesNotChangeVideoDuration()
    {
        var s = Session(); s.Execute(new SetVolume(s.Current.Timeline[0].Id, 200));
        Assert.Equal(S(10), s.Current.Duration);
    }
    [Fact] public void RationalFramesDoNotAccumulateRoundingErrors()
    {
        var time = MediaTime.Zero;
        for (var i = 0; i < 30000; i++) time += new MediaTime(1001, 30000);
        Assert.Equal(S(1001), time); Assert.Equal(MediaTime.Zero, default(MediaTime));
        var s = Session(); s.StepFrame(1); Assert.Equal(new MediaTime(1001, 30000), s.Current.Cursor);
        s.StepFrame(-1); Assert.Equal(MediaTime.Zero, s.Current.Cursor);
    }
    [Fact] public void SelectionKeepsAnchorAndPlaybackIsSilent()
    {
        var s = Session(); s.Seek(S(4)); s.Seek(S(2), true); s.Seek(S(8), true);
        Assert.Equal(S(4), s.Current.Selection!.Anchor); Assert.Equal(S(8), s.Current.Selection.End);
        s.Seek(S(4), true); Assert.Null(s.Current.Selection);
        var count = s.Messages.Count; s.Seek(S(5), playback: true); Assert.Equal(count, s.Messages.Count);
    }
    [Fact] public void SelectingItemDoesNotSeekOrEdit()
    {
        var s = Session(); s.Seek(S(5)); s.SelectItem(s.Current.Timeline[0].Id);
        Assert.Equal(S(5), s.Current.Cursor); Assert.Single(s.Current.Timeline);
    }
    [Fact] public void BrokenObserverDoesNotFailCommittedEdit()
    {
        var s = Session(); s.Changed += _ => throw new Exception("UI error");
        s.Execute(new SetVolume(s.Current.Timeline[0].Id, 40));
        Assert.Equal(40, s.Current.Timeline[0].Volume);
    }
    [Fact] public void VariableRateRequiresTimestampNavigator()
    {
        var s = new EditorSession(Project.Create("VFR")); var a = Video() with { VariableFrameRate = true };
        s.Execute(new RegisterMedia(a)); s.Execute(new InsertClip(a.Id, 0));
        Assert.Throws<InvalidOperationException>(() => s.StepFrame(1));
        s.StepFrame(1, navigator: new Frames()); Assert.Equal(new MediaTime(41, 1000), s.Current.Cursor);
    }
    private sealed class Frames : IFrameNavigator
    { public MediaTime NextFrame(MediaAsset asset, MediaTime sourcePosition, int direction) => new(41, 1000); }
    [Fact] public void FrameNavigationSnapsAfterSeekingBySeconds()
    {
        var s = Session(); s.Seek(S(1)); s.StepFrame(-1);
        Assert.Equal(new MediaTime(29 * 1001, 30000), s.Current.Cursor);
        s.Seek(S(1)); s.StepFrame(1);
        Assert.Equal(new MediaTime(30 * 1001, 30000), s.Current.Cursor);
    }
    [Fact] public void FrameNavigationAtSplitUsesCorrectSource()
    {
        var s = Session(); s.Execute(new SplitClip(s.Current.Timeline[0].Id, S(4)));
        s.Seek(S(4)); s.StepFrame(-1); Assert.True(s.Current.Cursor < S(4));
        s.Seek(S(4)); s.StepFrame(1); Assert.True(s.Current.Cursor > S(4));
    }
    [Fact] public void ImageRequiresExplicitDurationAndLayerCannotExceedComposition()
    {
        var s = Session(); var a = new MediaAsset(Guid.NewGuid(), "Foto", "foto.png", MediaKind.Image, null);
        s.Execute(new RegisterMedia(a));
        Assert.Throws<ArgumentException>(() => s.Execute(new InsertClip(a.Id, 1)));
        s.Execute(new InsertClip(a.Id, 1, S(3))); Assert.Equal(S(13), s.Current.Duration);
        Assert.Throws<ArgumentException>(() => s.Execute(new AddLayer(a.Id, S(12), S(0), S(3))));
    }
    [Fact] public async Task BatchImportContinuesAfterInvalidFile()
    {
        var s = new EditorSession(Project.Create("Importação"));
        var result = await MediaImport.ImportAsync(s, new Probe(), ["a.mp4", "bad.mp4", "b.mp4"]);
        Assert.Equal(2, result.ImportedIds.Count); Assert.Single(result.Failures);
        Assert.Equal(2, s.Current.Library.Length); Assert.Empty(s.Current.Timeline);
    }
    [Fact] public void ExportRequiresExplicitOrientation()
    {
        Assert.Throws<ArgumentException>(() => new ExportOptions("out.mp4", null, FitMode.Fill).Validate());
        new ExportOptions("out.mp4", Orientation.Vertical, FitMode.Letterbox).Validate();
    }
    private sealed class Probe : IMediaProbe
    {
        public Task<MediaAsset> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            path == "bad.mp4" ? throw new InvalidDataException("Mídia inválida.") : Task.FromResult(Video() with { Path = path });
    }
}
