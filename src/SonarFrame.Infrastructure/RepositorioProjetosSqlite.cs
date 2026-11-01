using System.Text.Json;
using Microsoft.Data.Sqlite;
using SonarFrame.Application;
using SonarFrame.Core;

namespace SonarFrame.Infrastructure;

public sealed class RepositorioProjetosSqlite : IRepositorioProjetos
{
    private const int ApplicationId = 1397117517;
    private const int SchemaVersion = 1;
    private static SqliteConnection Connect(string caminho, SqliteOpenMode modo) => new(new SqliteConnectionStringBuilder
    { DataSource = System.IO.Path.GetFullPath(caminho), Mode = modo, Pooling = false }.ToString());


    public Task SalvarAsync(string caminho, Projeto projeto, CancellationToken cancellationToken = default) =>
        Task.Run(() => SalvarCoreAsync(caminho, projeto, cancellationToken), cancellationToken);

    private static async Task SalvarCoreAsync(string caminho, Projeto projeto, CancellationToken cancellationToken)
    {
        projeto.Validar();
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(caminho)) { await WriteAsync(caminho, projeto, true, cancellationToken); return; }
        var temporario = Path.GetFullPath(caminho) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await WriteAsync(temporario, projeto, false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporario, Path.GetFullPath(caminho), overwrite: false);
        }
        finally { if (File.Exists(temporario)) File.Delete(temporario); }
    }

    private static async Task WriteAsync(string caminho, Projeto projeto, bool existe, CancellationToken cancellationToken)
    {
        projeto.Validar();
        var json = JsonSerializer.Serialize(projeto);
        using var connection = Connect(caminho, SqliteOpenMode.ReadWriteCreate);
        await connection.OpenAsync(cancellationToken);
        if (existe) await ValidarContainer(connection, cancellationToken);
        using var transacao = connection.BeginTransaction();
        using var comando = connection.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = $"""
            PRAGMA application_id = {ApplicationId};
            PRAGMA user_version = {SchemaVersion};
            CREATE TABLE IF NOT EXISTS project (id INTEGER PRIMARY KEY CHECK(id = 1), document TEXT NOT NULL);
            INSERT INTO project (id, document) VALUES (1, $document)
            ON CONFLICT(id) DO UPDATE SET document = excluded.document;
            """;
        comando.Parameters.AddWithValue("$document", json);
        await comando.ExecuteNonQueryAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        transacao.Commit();
    }
    public Task<Projeto> CarregarAsync(string caminho, CancellationToken cancellationToken = default) =>
        Task.Run(() => CarregarCoreAsync(caminho, cancellationToken), cancellationToken);

    private static async Task<Projeto> CarregarCoreAsync(string caminho, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = Connect(caminho, SqliteOpenMode.ReadOnly);
            await connection.OpenAsync(cancellationToken);
            await ValidarContainer(connection, cancellationToken);
            using var comando = connection.CreateCommand();
            comando.CommandText = "SELECT document FROM project WHERE id = 1";
            var json = await comando.ExecuteScalarAsync(cancellationToken) as string ?? throw new InvalidDataException("Projeto sem conteúdo.");
            return (JsonSerializer.Deserialize<Projeto>(json) ?? throw new InvalidDataException("Projeto vazio.")).Validar();
        }
        catch (Exception e) when (e is SqliteException or JsonException or ArgumentException or InvalidOperationException or OverflowException)
        { throw new InvalidDataException("Não foi possível abrir um projeto SonarFrame válido.", e); }
    }
    private static async Task ValidarContainer(SqliteConnection connection, CancellationToken token)
    {
        using var comando = connection.CreateCommand();
        comando.CommandText = "PRAGMA application_id";
        if (Convert.ToInt32(await comando.ExecuteScalarAsync(token)) != ApplicationId) throw new InvalidDataException("Arquivo não reconhecido como projeto SonarFrame.");
        comando.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(await comando.ExecuteScalarAsync(token)) != SchemaVersion) throw new InvalidDataException("Versão do projeto não suportada.");
        comando.CommandText = "PRAGMA quick_check";
        if (!string.Equals(await comando.ExecuteScalarAsync(token) as string, "ok", StringComparison.Ordinal)) throw new InvalidDataException("Banco de projeto corrompido.");
        comando.CommandText = "SELECT COUNT(*) FROM project WHERE id = 1";
        if (Convert.ToInt32(await comando.ExecuteScalarAsync(token)) != 1) throw new InvalidDataException("Estrutura de projeto inválida.");
    }
}
