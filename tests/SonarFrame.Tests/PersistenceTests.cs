using Microsoft.Data.Sqlite;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using System.Text.Json;
using Xunit;
using static SonarFrame.Tests.EditingTests;

namespace SonarFrame.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "project.sonarframe");
    public PersistenceTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    [Fact] public async Task FullEditingWorkflowRoundTripsWithoutTouchingMedia()
    {
        var source = Path.Combine(directory, "source.mp4"); await File.WriteAllTextAsync(source, "original bytes");
        var s = new EditorSession(Project.Create("Meu projeto"));
        var a = Video() with { Path = source }; var b = Video();
        s.Execute(new RegisterMedia(a)); s.Execute(new RegisterMedia(b));
        s.Execute(new InsertClip(a.Id, 0)); s.Execute(new InsertClip(b.Id, 1));
        s.Execute(new SplitClip(s.Current.Timeline[0].Id, S(4)));
        s.Execute(new MoveClip(s.Current.Timeline[2].Id, 0));
        s.Execute(new DeleteRange(S(2), S(8))); s.Undo(); s.Redo();
        var store = new SqliteProjectStore(); await store.SaveAsync(FilePath, s.Current);
        var loaded = await store.LoadAsync(FilePath);
        Assert.Equal(JsonSerializer.Serialize(s.Current), JsonSerializer.Serialize(loaded));
        Assert.Equal("original bytes", await File.ReadAllTextAsync(source));
        Assert.Equal(2, loaded.Library.Length);
        s.Execute(new RelinkMedia(b.Id, "new/location.mp4"));
        await store.SaveAsync(FilePath, s.Current);
        Assert.Equal("new/location.mp4", (await store.LoadAsync(FilePath)).Library[1].Path);
    }
    [Fact] public async Task AutosaveRunsAfterTwoMinutesAndCleanSessionDoesNotWriteAgain()
    {
        var s = Session(); var clock = new ManualClock(); var store = new CountingStore();
        var persistence = new ProjectPersistence(s, store, FilePath, clock);
        clock.Advance(119); Assert.False(await persistence.AutoSaveIfDueAsync());
        clock.Advance(1); Assert.True(await persistence.AutoSaveIfDueAsync()); Assert.False(s.IsDirty);
        clock.Advance(120); Assert.False(await persistence.AutoSaveIfDueAsync()); Assert.Equal(2, store.Writes);
    }
    [Fact] public async Task UnclosedSessionOffersRecoveryAndCleanCloseRemovesIt()
    {
        var s = Session(); var store = new SqliteProjectStore();
        var persistence = new ProjectPersistence(s, store, FilePath);
        await persistence.SaveAsync();
        var reopened = new EditorSession(await store.LoadAsync(FilePath), true);
        var nextProcess = new ProjectPersistence(reopened, store, FilePath);
        Assert.NotNull(await nextProcess.GetRecoveryAsync());
        await nextProcess.CloseAsync(); Assert.Null(await nextProcess.GetRecoveryAsync());
    }
    [Fact] public async Task FailedSaveKeepsDirtyStateAndExistingData()
    {
        var s = Session(); var store = new SqliteProjectStore(); await store.SaveAsync(FilePath, s.Current);
        var old = await File.ReadAllBytesAsync(FilePath);
        var persistence = new ProjectPersistence(s, new FailingStore(), FilePath);
        await Assert.ThrowsAsync<IOException>(() => persistence.SaveAsync());
        Assert.True(s.IsDirty); Assert.Equal(old, await File.ReadAllBytesAsync(FilePath));
    }
    [Fact] public async Task CancellationPreservesLastCommittedProject()
    {
        var s = Session(); var store = new SqliteProjectStore(); await store.SaveAsync(FilePath, s.Current);
        var old = JsonSerializer.Serialize(await store.LoadAsync(FilePath));
        s.Execute(new DeleteRange(S(0), S(5)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(FilePath, s.Current, new(true)));
        Assert.Equal(old, JsonSerializer.Serialize(await store.LoadAsync(FilePath)));
    }
    [Fact] public async Task InvalidAndNewerDatabasesAreRejectedWithoutOverwrite()
    {
        await File.WriteAllTextAsync(FilePath, "not sqlite"); var store = new SqliteProjectStore();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(FilePath));
        await Assert.ThrowsAnyAsync<Exception>(() => store.SaveAsync(FilePath, Project.Create("x")));
        Assert.Equal("not sqlite", await File.ReadAllTextAsync(FilePath));
        File.Delete(FilePath); await store.SaveAsync(FilePath, Project.Create("x"));
        using (var connection = new SqliteConnection($"Data Source={FilePath};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=999"; command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(FilePath));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(FilePath, Project.Create("x")));
    }
    [Fact] public async Task EditDuringSaveRemainsDirty()
    {
        var s = Session(); var store = new BlockingStore(); var persistence = new ProjectPersistence(s, store, FilePath);
        var saving = persistence.SaveAsync(); await store.Started.Task;
        s.Execute(new SetVolume(s.Current.Timeline[0].Id, 50)); store.Continue.SetResult(); await saving;
        Assert.True(s.IsDirty);
    }
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }
    [Fact] public async Task FailedFirstSaveDoesNotLeaveInvalidDestination()
    {
        var store = new SqliteProjectStore();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(FilePath, Project.Create("x"), new(true)));
        Assert.False(File.Exists(FilePath));
        await store.SaveAsync(FilePath, Project.Create("x")); Assert.True(File.Exists(FilePath));
    }
    [Fact] public async Task RecoveryFromDifferentProjectIsRejected()
    {
        var store = new SqliteProjectStore(); var persistence = new ProjectPersistence(Session(), store, FilePath);
        await store.SaveAsync(persistence.RecoveryPath, Project.Create("Outro"));
        await Assert.ThrowsAsync<InvalidDataException>(() => persistence.GetRecoveryAsync());
    }
    [Fact] public async Task DirtyCloseRequiresExplicitDiscard()
    {
        var persistence = new ProjectPersistence(Session(), new SqliteProjectStore(), FilePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => persistence.CloseAsync());
        await persistence.CloseAsync(discardUnsavedChanges: true);
    }
    private class CountingStore : IProjectStore
    {
        public int Writes { get; private set; }
        public virtual Task SaveAsync(string path, Project project, CancellationToken cancellationToken = default) { Writes++; return Task.CompletedTask; }
        public Task<Project> LoadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class FailingStore : CountingStore
    { public override Task SaveAsync(string path, Project project, CancellationToken cancellationToken = default) => throw new IOException("disk full"); }
    private sealed class BlockingStore : CountingStore
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task SaveAsync(string path, Project project, CancellationToken cancellationToken = default)
        { Started.TrySetResult(); await Continue.Task; }
    }
}
