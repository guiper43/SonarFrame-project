using SonarFrame.Core;

namespace SonarFrame.Application;

/// <summary>One coordinator per session. Recovery is offered to the caller, never applied implicitly.</summary>
public sealed class ProjectPersistence
{
    private readonly EditorSession session;
    private readonly IProjectStore store;
    private readonly SemaphoreSlim writes = new(1, 1);
    private readonly TimeProvider clock;
    private DateTimeOffset lastCheck;
    public string ProjectPath { get; }
    public string RecoveryPath => ProjectPath + ".recovery";
    public static TimeSpan AutoSaveInterval => TimeSpan.FromMinutes(2);
    public ProjectPersistence(EditorSession session, IProjectStore store, string projectPath, TimeProvider? clock = null)
    {
        this.session = session; this.store = store;
        ProjectPath = Path.GetFullPath(projectPath);
        this.clock = clock ?? TimeProvider.System;
        lastCheck = this.clock.GetUtcNow();
    }
    public async Task SaveAsync(CancellationToken token = default)
    {
        await writes.WaitAsync(token);
        try
        {
            var snapshot = session.Current;
            // A recovery file represents an unclosed session, even after a completed autosave.
            await store.SaveAsync(RecoveryPath, snapshot, token);
            await store.SaveAsync(ProjectPath, snapshot, token);
            session.MarkSaved(snapshot);
            lastCheck = clock.GetUtcNow();
            session.Notify(new("project.saved", "Projeto salvo."));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        { session.Notify(new("save.failed", e.Message, true)); throw; }
        finally { writes.Release(); }
    }
    public async Task<bool> AutoSaveIfDueAsync(CancellationToken token = default)
    {
        if (clock.GetUtcNow() - lastCheck < AutoSaveInterval || !session.IsDirty) return false;
        await SaveAsync(token);
        return true;
    }
    public async Task RunAutoSaveAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(AutoSaveInterval, clock);
        while (await timer.WaitForNextTickAsync(token))
        {
            try { await AutoSaveIfDueAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e) { session.Notify(new("autosave.failed", e.Message, true)); }
        }
    }
    public Task<Project?> GetRecoveryAsync(CancellationToken token = default) => ReadRecovery(token);
    private async Task<Project?> ReadRecovery(CancellationToken token)
    {
        if (!File.Exists(RecoveryPath)) return null;
        var recovered = await store.LoadAsync(RecoveryPath, token);
        if (recovered.Id != session.Current.Id) throw new InvalidDataException("Recuperação pertence a outro projeto.");
        return recovered;
    }
    /// <summary>Stop and await the autosave loop before closing. Unsaved edits require explicit discard.</summary>
    public async Task CloseAsync(bool discardUnsavedChanges = false, CancellationToken token = default)
    {
        await writes.WaitAsync(token);
        try
        {
            if (session.IsDirty && !discardUnsavedChanges) throw new InvalidOperationException("Salve ou descarte explicitamente as alterações antes de fechar.");
            if (File.Exists(RecoveryPath)) File.Delete(RecoveryPath);
        }
        finally { writes.Release(); }
    }
}
