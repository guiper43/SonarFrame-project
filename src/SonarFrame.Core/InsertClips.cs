using System.Collections.Immutable;

namespace SonarFrame.Core;

/// <summary>Inserts an explicit ordered selection as one undoable edit.</summary>
public sealed record InsertClips(ImmutableArray<Guid> AssetIds, MediaTime? ImageDuration = null) : IEditCommand
{
    public Project Apply(Project project)
    {
        if (AssetIds.IsDefaultOrEmpty) throw new ArgumentException("Selecione pelo menos uma mídia.");
        foreach (var id in AssetIds)
            project = new InsertClip(id, project.Timeline.Length, ImageDuration).Apply(project);
        return project;
    }
}
