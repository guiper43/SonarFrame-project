using SonarFrame.Core;

namespace SonarFrame.Application;

/// <summary>Thread-safe owner of one project. Observers run on the caller's thread; UI adapters must dispatch.</summary>
public sealed class EditorSession
{
    private readonly object gate = new();
    private readonly Stack<Project> undo = new();
    private readonly Stack<Project> redo = new();
    private Project current;
    private Project? saved;
    private readonly Queue<EditorNotification> messages = new();
    public EditorSession(Project project, bool isSaved = false) { current = project.Validate(); saved = isSaved ? project : null; }
    public Project Current { get { lock (gate) return current; } }
    public bool IsDirty { get { lock (gate) return current != saved; } }
    public bool CanUndo { get { lock (gate) return undo.Count > 0; } }
    public bool CanRedo { get { lock (gate) return redo.Count > 0; } }
    public IReadOnlyList<EditorNotification> Messages { get { lock (gate) return messages.ToArray(); } }
    public event Action<EditorNotification>? Changed;
    public void Execute(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            lock (gate)
            {
                var next = command.Apply(current).Validate();
                if (next == current) return;
                undo.Push(current); redo.Clear(); current = next;
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException)
        { Notify(new("edit.failed", e.Message, true)); throw; }
        Notify(new("edit.completed", "Edição aplicada."));
    }
    public void Undo() => Restore(undo, redo, "edit.undone", "Edição desfeita.");
    public void Redo() => Restore(redo, undo, "edit.redone", "Edição refeita.");
    private void Restore(Stack<Project> from, Stack<Project> to, string code, string message)
    {
        lock (gate)
        {
            if (from.Count == 0) return;
            to.Push(current); current = from.Pop();
        }
        Notify(new(code, message));
    }
    public void Seek(MediaTime target, bool extendSelection = false, bool playback = false)
    {
        lock (gate)
        {
            target = MediaTime.Max(MediaTime.Zero, MediaTime.Min(target, current.Duration));
            var selection = extendSelection ? new TimeSelection(current.Selection?.Anchor ?? current.Cursor, target) : null;
            current = current with { Cursor = target, Selection = selection?.Start == selection?.End ? null : selection };
        }
        // Playback updates are deliberately silent for screen readers.
        if (!playback) Notify(new("cursor.moved", Current.Cursor.ToString()));
    }
    public void MoveBy(MediaTime delta, bool extendSelection = false) => Seek(Current.Cursor + delta, extendSelection);
    public void SelectItem(Guid? id)
    {
        lock (gate) current = (current with { SelectedItemId = id }).Validate();
        Notify(new("selection.changed", "Seleção alterada."));
    }
    public void StepFrame(int direction, bool extendSelection = false, IFrameNavigator? navigator = null)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var p = Current;
        if (p.Timeline.IsEmpty || (direction < 0 && p.Cursor <= MediaTime.Zero) || (direction > 0 && p.Cursor >= p.Duration)) return;
        var start = MediaTime.Zero;
        foreach (var clip in p.Timeline)
        {
            var end = start + clip.Duration;
            if (p.Cursor < end || (direction < 0 && p.Cursor == end))
            {
                var asset = p.Library.Single(a => a.Id == clip.AssetId);
                var source = clip.SourceStart + p.Cursor - start;
                MediaTime next;
                if (navigator is not null) next = navigator.NextFrame(asset, source, direction);
                else
                {
                    if (asset.VariableFrameRate || asset.FrameRate is null) throw new InvalidOperationException("Esta mídia requer um navegador de quadros.");
                    next = source.AdjacentFrame(asset.FrameRate, direction);
                }
                if (direction > 0 ? next <= source : next >= source) throw new InvalidOperationException("Timestamp de quadro inválido.");
                Seek(MediaTime.Max(start, MediaTime.Min(end, start + next - clip.SourceStart)), extendSelection);
                return;
            }
            start = end;
        }
    }
    public void MarkSaved(Project snapshot) { lock (gate) saved = snapshot; }
    public void Notify(EditorNotification notification)
    {
        lock (gate)
        {
            messages.Enqueue(notification);
            while (messages.Count > 100) messages.Dequeue();
        }
        if (Changed is { } handlers)
            foreach (Action<EditorNotification> handler in handlers.GetInvocationList())
                try { handler(notification); } catch { /* A UI observer cannot roll back a committed edit. */ }
    }
}
