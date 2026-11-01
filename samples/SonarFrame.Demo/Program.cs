using Microsoft.Data.Sqlite;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;

if (args.Length != 2 || args[0] is not ("create" or "inspect" or "simulate-crash"))
{
    Console.WriteLine("Uso: SonarFrame.Demo <create|inspect|simulate-crash> <arquivo.sonarframe>");
    return 1;
}
var caminho = Path.GetFullPath(args[1]);
var repositorio = new RepositorioProjetosSqlite();
if (args[0] == "simulate-crash")
{

    _ = await repositorio.CarregarAsync(caminho);
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = caminho, Pooling = false }.ToString());
    connection.Open();
    using var transacao = connection.BeginTransaction();
    using var comando = connection.CreateCommand(); comando.Transaction = transacao;
    comando.CommandText = "UPDATE project SET document = 'gravação interrompida' WHERE id = 1";
    comando.ExecuteNonQuery();
    Environment.Exit(73);
}
if (args[0] == "create")
{
    if (File.Exists(caminho)) throw new IOException("O exemplo exige um destino novo.");
    Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
    var sessao = new SessaoEdicao(Projeto.Criar("Demonstração do núcleo"));
    sessao.Alterado += n => Console.WriteLine($"{n.Codigo}: {n.Mensagem}");
    var video = new Midia(Guid.NewGuid(), "Vídeo de exemplo", Path.GetFullPath("exemplo.mp4"), TipoMidia.Video, TempoMidia.Segundos(10), new(30, 1));
    sessao.Executar(new RegistrarMidia(video)); sessao.Executar(new InserirClipe(video.Id, 0));
    sessao.Executar(new DividirClipe(sessao.Atual.LinhaDoTempo[0].Id, TempoMidia.Segundos(4)));
    sessao.Executar(new MoverClipe(sessao.Atual.LinhaDoTempo[1].Id, 0));
    sessao.Executar(new ExcluirIntervalo(TempoMidia.Segundos(2), TempoMidia.Segundos(5)));
    sessao.Desfazer(); sessao.Refazer();
    var persistencia = new PersistenciaProjeto(sessao, repositorio, caminho);
    await persistencia.SalvarAsync(); await persistencia.FecharAsync();
}
var projeto = await repositorio.CarregarAsync(caminho);
Console.WriteLine($"{projeto.Nome}: {projeto.Biblioteca.Length} mídia(s), {projeto.LinhaDoTempo.Length} clipe(s), duração {projeto.Duracao}");
Console.WriteLine($"Mídias ausentes: {ImportacaoMidias.EncontrarAusentes(projeto).Count}. A demonstração usa metadados fictícios.");
return 0;
