using Microsoft.Data.Sqlite;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;

if (args.Length != 2 || args[0] is not ("create" or "inspect" or "simulate-crash"))
{
    Console.WriteLine("Uso: SonarFrame.Demo <create|inspect|simulate-crash> <arquivo.sonarframe>");
    return 1;
}
var path = Path.GetFullPath(args[1]);
var store = new SqliteProjectStore();
if (args[0] == "simulate-crash")
{
    // Integration fixture: terminate without disposing an uncommitted SQLite transaction.
    _ = await store.LoadAsync(path);
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
    connection.Open();
    using var transaction = connection.BeginTransaction();
    using var command = connection.CreateCommand(); command.Transaction = transaction;
    command.CommandText = "UPDATE project SET document = 'interrupted write' WHERE id = 1";
    command.ExecuteNonQuery();
    Environment.Exit(73);
}
if (args[0] == "create")
{
    if (File.Exists(path)) throw new IOException("O exemplo exige um destino novo.");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var session = new EditorSession(Project.Create("Demonstração do núcleo"));
    session.Changed += n => Console.WriteLine($"{n.Code}: {n.Message}");
    var video = new MediaAsset(Guid.NewGuid(), "Vídeo de exemplo", Path.GetFullPath("exemplo.mp4"), MediaKind.Video, MediaTime.Seconds(10), new(30, 1));
    session.Execute(new RegisterMedia(video)); session.Execute(new InsertClip(video.Id, 0));
    session.Execute(new SplitClip(session.Current.Timeline[0].Id, MediaTime.Seconds(4)));
    session.Execute(new MoveClip(session.Current.Timeline[1].Id, 0));
    session.Execute(new DeleteRange(MediaTime.Seconds(2), MediaTime.Seconds(5)));
    session.Undo(); session.Redo();
    var persistence = new ProjectPersistence(session, store, path);
    await persistence.SaveAsync(); await persistence.CloseAsync();
}
var project = await store.LoadAsync(path);
Console.WriteLine($"{project.Name}: {project.Library.Length} mídia(s), {project.Timeline.Length} clipe(s), duração {project.Duration}");
Console.WriteLine($"Mídias ausentes: {MediaImport.FindMissing(project).Count}. A demonstração usa metadados fictícios.");
return 0;
