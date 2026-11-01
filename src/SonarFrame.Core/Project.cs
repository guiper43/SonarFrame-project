using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SonarFrame.Core;

public enum MediaKind { Video, Audio, Image }
public sealed record MediaAsset(Guid Id, string Name, string Path, MediaKind Kind,
    MediaTime? Duration, FrameRate? FrameRate = null, bool VariableFrameRate = false);
public sealed record Clip(Guid Id, Guid AssetId, string Name, MediaTime SourceStart, MediaTime Duration, int Volume = 100);
public sealed record TimedClip(Clip Clip, MediaTime Start);
public sealed record TimeSelection(MediaTime Anchor, MediaTime Active)
{
    [JsonIgnore] public MediaTime Start => MediaTime.Min(Anchor, Active);
    [JsonIgnore] public MediaTime End => MediaTime.Max(Anchor, Active);
}
public sealed record Project(Guid Id, string Name, ImmutableArray<MediaAsset> Library,
    ImmutableArray<Clip> Timeline, ImmutableArray<TimedClip> Layers, MediaTime Cursor,
    TimeSelection? Selection = null, Guid? SelectedItemId = null)
{
    [JsonIgnore] public MediaTime Duration => Timeline.Aggregate(MediaTime.Zero, (t, c) => t + c.Duration);
    public static Project Create(string name) => new Project(Guid.NewGuid(), name, [], [], [], MediaTime.Zero).Validate();
    public Project Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("Projeto sem identificação.");
        if (Library.IsDefault || Timeline.IsDefault || Layers.IsDefault) throw new ArgumentException("Coleções ausentes.");
        if (Library.Any(a => a is null) || Timeline.Any(c => c is null) || Layers.Any(l => l is null || l.Clip is null)) throw new ArgumentException("Elementos ausentes.");
        if (Library.Select(a => a.Id).Distinct().Count() != Library.Length) throw new ArgumentException("Mídias duplicadas.");
        foreach (var a in Library)
        {
            if (a.Id == Guid.Empty || string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.Path) || !Enum.IsDefined(a.Kind)) throw new ArgumentException("Mídia inválida.");
            if (a.Duration is { } d && d <= MediaTime.Zero) throw new ArgumentException("Duração inválida.");
            if (a.Kind != MediaKind.Image && a.Duration is null) throw new ArgumentException("Duração da mídia necessária.");
            if (a.FrameRate is { } rate) _ = rate.FrameDuration;
        }
        var clips = Timeline.Concat(Layers.Select(l => l.Clip)).ToArray();
        if (clips.Select(c => c.Id).Distinct().Count() != clips.Length) throw new ArgumentException("Clipes duplicados.");
        foreach (var c in clips)
        {
            var asset = Library.SingleOrDefault(a => a.Id == c.AssetId) ?? throw new ArgumentException("Mídia não encontrada.");
            if (c.Id == Guid.Empty || string.IsNullOrWhiteSpace(c.Name) || c.SourceStart < MediaTime.Zero || c.Duration <= MediaTime.Zero || c.Volume is < 0 or > 200) throw new ArgumentException("Clipe inválido.");
            if (asset.Duration is { } d && c.SourceStart + c.Duration > d) throw new ArgumentException("Clipe ultrapassa a origem.");
        }
        if (Layers.Any(l => l.Start < MediaTime.Zero || l.Start + l.Clip.Duration > Duration)) throw new ArgumentException("Camada fora da composição.");
        if (Cursor < MediaTime.Zero || Cursor > Duration) throw new ArgumentException("Cursor fora da composição.");
        if (Selection is { } s && (s.Start < MediaTime.Zero || s.End > Duration)) throw new ArgumentException("Seleção fora da composição.");
        if (SelectedItemId is { } id && !clips.Any(c => c.Id == id)) throw new ArgumentException("Seleção de item inválida.");
        return this;
    }
}
