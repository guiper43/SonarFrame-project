using System.Text.Json;
using Microsoft.Data.Sqlite;
using SonarFrame.Application;
using SonarFrame.Core;

namespace SonarFrame.Infrastructure;

/// <summary>Version 1 stores an immutable JSON document inside a transactional SQLite container.</summary>
public sealed class SqliteProjectStore : IProjectStore
{
    private const int ApplicationId = 1397117517;
    private const int SchemaVersion = 1;
    private static SqliteConnection Connect(string path, SqliteOpenMode mode) => new(new SqliteConnectionStringBuilder
    { DataSource = System.IO.Path.GetFullPath(path), Mode = mode, Pooling = false }.ToString());

    // SQLite's ADO.NET async methods are synchronous; move disk work off the UI thread.
    public Task SaveAsync(string path, Project project, CancellationToken cancellationToken = default) =>
        Task.Run(() => SaveCoreAsync(path, project, cancellationToken), cancellationToken);

    private static async Task SaveCoreAsync(string path, Project project, CancellationToken cancellationToken)
    {
        project.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path)) { await WriteAsync(path, project, true, cancellationToken); return; }
        var staging = Path.GetFullPath(path) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await WriteAsync(staging, project, false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staging, Path.GetFullPath(path), overwrite: false);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    private static async Task WriteAsync(string path, Project project, bool exists, CancellationToken cancellationToken)
    {
        project.Validate();
        var json = JsonSerializer.Serialize(project);
        using var connection = Connect(path, SqliteOpenMode.ReadWriteCreate);
        await connection.OpenAsync(cancellationToken);
        if (exists) await ValidateContainer(connection, cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            PRAGMA application_id = {ApplicationId};
            PRAGMA user_version = {SchemaVersion};
            CREATE TABLE IF NOT EXISTS project (id INTEGER PRIMARY KEY CHECK(id = 1), document TEXT NOT NULL);
            INSERT INTO project (id, document) VALUES (1, $document)
            ON CONFLICT(id) DO UPDATE SET document = excluded.document;
            """;
        command.Parameters.AddWithValue("$document", json);
        await command.ExecuteNonQueryAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }
    public Task<Project> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadCoreAsync(path, cancellationToken), cancellationToken);

    private static async Task<Project> LoadCoreAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = Connect(path, SqliteOpenMode.ReadOnly);
            await connection.OpenAsync(cancellationToken);
            await ValidateContainer(connection, cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT document FROM project WHERE id = 1";
            var json = await command.ExecuteScalarAsync(cancellationToken) as string ?? throw new InvalidDataException("Projeto sem conteúdo.");
            return (JsonSerializer.Deserialize<Project>(json) ?? throw new InvalidDataException("Projeto vazio.")).Validate();
        }
        catch (Exception e) when (e is SqliteException or JsonException or ArgumentException or InvalidOperationException or OverflowException)
        { throw new InvalidDataException("Não foi possível abrir um projeto SonarFrame válido.", e); }
    }
    private static async Task ValidateContainer(SqliteConnection connection, CancellationToken token)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA application_id";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != ApplicationId) throw new InvalidDataException("Arquivo não reconhecido como projeto SonarFrame.");
        command.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != SchemaVersion) throw new InvalidDataException("Versão do projeto não suportada.");
        command.CommandText = "PRAGMA quick_check";
        if (!string.Equals(await command.ExecuteScalarAsync(token) as string, "ok", StringComparison.Ordinal)) throw new InvalidDataException("Banco de projeto corrompido.");
        command.CommandText = "SELECT COUNT(*) FROM project WHERE id = 1";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != 1) throw new InvalidDataException("Estrutura de projeto inválida.");
    }
}
