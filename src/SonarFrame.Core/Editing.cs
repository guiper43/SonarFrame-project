using System.Collections.Immutable;

namespace SonarFrame.Core;

public interface IEditCommand { Project Apply(Project project); }

public sealed record RegisterMedia(MediaAsset Asset) : IEditCommand
{
    public Project Apply(Project p) => p with { Library = p.Library.Add(Asset) };
}
public sealed record RelinkMedia(Guid AssetId, string Path) : IEditCommand
{
    public Project Apply(Project p)
    {
        var index = p.Library.FindIndex(a => a.Id == AssetId);
        if (index < 0) throw new ArgumentException("Mídia não encontrada.");
        return p with { Library = p.Library.SetItem(index, p.Library[index] with { Path = Path }) };
    }
}
public sealed record InsertClip(Guid AssetId, int Index, MediaTime? ImageDuration = null) : IEditCommand
{
    public Project Apply(Project p)
    {
        var asset = p.Library.Single(a => a.Id == AssetId);
        var duration = asset.Duration ?? ImageDuration ?? throw new ArgumentException("Informe a duração da imagem.");
        var clip = new Clip(Guid.NewGuid(), asset.Id, asset.Name, MediaTime.Zero, duration);
        return p with { Timeline = p.Timeline.Insert(Index, clip), SelectedItemId = clip.Id };
    }
}
public sealed record AddLayer(Guid AssetId, MediaTime Start, MediaTime SourceStart, MediaTime Duration) : IEditCommand
{
    public Project Apply(Project p)
    {
        var asset = p.Library.Single(a => a.Id == AssetId);
        var clip = new Clip(Guid.NewGuid(), asset.Id, asset.Name, SourceStart, Duration);
        return p with { Layers = p.Layers.Add(new(clip, Start)), SelectedItemId = clip.Id };
    }
}
public sealed record SplitClip(Guid ClipId, MediaTime Offset) : IEditCommand
{
    public Project Apply(Project p)
    {
        var index = p.Timeline.FindIndex(c => c.Id == ClipId);
        if (index < 0) throw new ArgumentException("Clipe não encontrado.");
        var clip = p.Timeline[index];
        if (Offset <= MediaTime.Zero || Offset >= clip.Duration) throw new ArgumentException("Divisão deve ocorrer dentro do clipe.");
        var first = clip with { Name = clip.Name + " — Parte 1", Duration = Offset };
        var second = clip with { Id = Guid.NewGuid(), Name = clip.Name + " — Parte 2", SourceStart = clip.SourceStart + Offset, Duration = clip.Duration - Offset };
        return p with { Timeline = p.Timeline.SetItem(index, first).Insert(index + 1, second) };
    }
}
/// <summary>Destination is the final zero-based position. Independent layers stay at their absolute times.</summary>
public sealed record MoveClip(Guid ClipId, int Destination) : IEditCommand
{
    public Project Apply(Project p)
    {
        var index = p.Timeline.FindIndex(c => c.Id == ClipId);
        if (index < 0 || Destination < 0 || Destination >= p.Timeline.Length) throw new ArgumentException("Destino ou clipe inválido.");
        return p with { Timeline = p.Timeline.RemoveAt(index).Insert(Destination, p.Timeline[index]) };
    }
}
public sealed record SetVolume(Guid ClipId, int Volume) : IEditCommand
{
    public Project Apply(Project p)
    {
        if (!p.Timeline.Any(c => c.Id == ClipId) && !p.Layers.Any(l => l.Clip.Id == ClipId)) throw new ArgumentException("Clipe não encontrado.");
        return p with
        {
            Timeline = p.Timeline.Select(c => c.Id == ClipId ? c with { Volume = Volume } : c).ToImmutableArray(),
            Layers = p.Layers.Select(l => l.Clip.Id == ClipId ? l with { Clip = l.Clip with { Volume = Volume } } : l).ToImmutableArray()
        };
    }
}
/// <summary>Deletes a composition interval, including overlapping layers, and closes the gap.</summary>
public sealed record DeleteRange(MediaTime Start, MediaTime End) : IEditCommand
{
    public Project Apply(Project p)
    {
        if (Start < MediaTime.Zero || End > p.Duration || Start >= End) throw new ArgumentException("Intervalo de remoção inválido.");
        var timeline = ImmutableArray.CreateBuilder<Clip>();
        var position = MediaTime.Zero;
        foreach (var clip in p.Timeline)
        {
            timeline.AddRange(Remaining(clip, position).Select(x => x.Clip));
            position += clip.Duration;
        }
        var layers = p.Layers.SelectMany(l => Remaining(l.Clip, l.Start)).ToImmutableArray();
        var ids = timeline.Select(c => c.Id).Concat(layers.Select(l => l.Clip.Id)).ToHashSet();
        return p with { Timeline = timeline.ToImmutable(), Layers = layers, Cursor = Start,
            Selection = null, SelectedItemId = p.SelectedItemId is { } id && ids.Contains(id) ? id : null };
    }
    private IEnumerable<TimedClip> Remaining(Clip clip, MediaTime position)
    {
        var end = position + clip.Duration;
        if (end <= Start) { yield return new(clip, position); yield break; }
        if (position >= End) { yield return new(clip, position - (End - Start)); yield break; }
        var left = MediaTime.Max(MediaTime.Zero, Start - position);
        var right = MediaTime.Max(MediaTime.Zero, end - End);
        if (left > MediaTime.Zero) yield return new(clip with { Duration = left }, position);
        if (right > MediaTime.Zero) yield return new(clip with
        {
            Id = left > MediaTime.Zero ? Guid.NewGuid() : clip.Id,
            SourceStart = clip.SourceStart + (clip.Duration - right), Duration = right
        }, Start);
    }
}

internal static class ImmutableArrayExtensions
{
    public static int FindIndex<T>(this ImmutableArray<T> items, Func<T, bool> match)
    {
        for (var i = 0; i < items.Length; i++) if (match(items[i])) return i;
        return -1;
    }
}
