using Microsoft.Data.Sqlite;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using System.Text.Json;
using Xunit;
using static SonarFrame.Tests.TestesEdicao;

namespace SonarFrame.Tests;

public sealed class TestesPersistencia : IDisposable
{
    private readonly string diretorio = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    private string CaminhoArquivo => Path.Combine(diretorio, "project.sonarframe");
    public TestesPersistencia() => Directory.CreateDirectory(diretorio);
    public void Dispose() => Directory.Delete(diretorio, true);
    [Fact] public async Task FluxoCompletoDeEdicaoSalvaEReabreSemAlterarMidias()
    {
        var origem = Path.Combine(diretorio, "source.mp4"); await File.WriteAllTextAsync(origem, "bytes originais");
        var s = new SessaoEdicao(Projeto.Criar("Meu projeto"));
        var a = Video() with { Caminho = origem }; var b = Video();
        s.Executar(new RegistrarMidia(a)); s.Executar(new RegistrarMidia(b));
        s.Executar(new InserirClipe(a.Id, 0)); s.Executar(new InserirClipe(b.Id, 1));
        s.Executar(new DividirClipe(s.Atual.LinhaDoTempo[0].Id, Segundos(4)));
        s.Executar(new MoverClipe(s.Atual.LinhaDoTempo[2].Id, 0));
        s.Executar(new ExcluirIntervalo(Segundos(2), Segundos(8))); s.Desfazer(); s.Refazer();
        var repositorio = new RepositorioProjetosSqlite(); await repositorio.SalvarAsync(CaminhoArquivo, s.Atual);
        var carregado = await repositorio.CarregarAsync(CaminhoArquivo);
        Assert.Equal(JsonSerializer.Serialize(s.Atual), JsonSerializer.Serialize(carregado));
        Assert.Equal("bytes originais", await File.ReadAllTextAsync(origem));
        Assert.Equal(2, carregado.Biblioteca.Length);
        s.Executar(new ReligarMidia(b.Id, "new/location.mp4"));
        await repositorio.SalvarAsync(CaminhoArquivo, s.Atual);
        Assert.Equal("new/location.mp4", (await repositorio.CarregarAsync(CaminhoArquivo)).Biblioteca[1].Caminho);
    }
    [Fact] public async Task SalvamentoAutomaticoOcorreAposDoisMinutosESessaoSemAlteracoesNaoGravaNovamente()
    {
        var s = Sessao(); var relogio = new RelogioManual(); var repositorio = new RepositorioContador();
        var persistencia = new PersistenciaProjeto(s, repositorio, CaminhoArquivo, relogio);
        relogio.Avancar(119); Assert.False(await persistencia.SalvarAutomaticamenteSeNecessarioAsync());
        relogio.Avancar(1); Assert.True(await persistencia.SalvarAutomaticamenteSeNecessarioAsync()); Assert.False(s.TemAlteracoes);
        relogio.Avancar(120); Assert.False(await persistencia.SalvarAutomaticamenteSeNecessarioAsync()); Assert.Equal(2, repositorio.Gravacoes);
    }
    [Fact] public async Task SessaoNaoEncerradaOfereceRecuperacaoEFechamentoNormalARemove()
    {
        var s = Sessao(); var repositorio = new RepositorioProjetosSqlite();
        var persistencia = new PersistenciaProjeto(s, repositorio, CaminhoArquivo);
        await persistencia.SalvarAsync();
        var reaberto = new SessaoEdicao(await repositorio.CarregarAsync(CaminhoArquivo), true);
        var proximoProcesso = new PersistenciaProjeto(reaberto, repositorio, CaminhoArquivo);
        Assert.NotNull(await proximoProcesso.ObterRecuperacaoAsync());
        await proximoProcesso.FecharAsync(); Assert.Null(await proximoProcesso.ObterRecuperacaoAsync());
    }
    [Fact] public async Task FalhaNoSalvamentoMantemAlteracoesPendentesEDadosExistentes()
    {
        var s = Sessao(); var repositorio = new RepositorioProjetosSqlite(); await repositorio.SalvarAsync(CaminhoArquivo, s.Atual);
        var anterior = await File.ReadAllBytesAsync(CaminhoArquivo);
        var persistencia = new PersistenciaProjeto(s, new RepositorioComFalha(), CaminhoArquivo);
        await Assert.ThrowsAsync<IOException>(() => persistencia.SalvarAsync());
        Assert.True(s.TemAlteracoes); Assert.Equal(anterior, await File.ReadAllBytesAsync(CaminhoArquivo));
    }
    [Fact] public async Task CancelamentoPreservaUltimoProjetoSalvo()
    {
        var s = Sessao(); var repositorio = new RepositorioProjetosSqlite(); await repositorio.SalvarAsync(CaminhoArquivo, s.Atual);
        var anterior = JsonSerializer.Serialize(await repositorio.CarregarAsync(CaminhoArquivo));
        s.Executar(new ExcluirIntervalo(Segundos(0), Segundos(5)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repositorio.SalvarAsync(CaminhoArquivo, s.Atual, new(true)));
        Assert.Equal(anterior, JsonSerializer.Serialize(await repositorio.CarregarAsync(CaminhoArquivo)));
    }
    [Fact] public async Task BancosInvalidosEMaisNovosSaoRejeitadosSemSobrescrita()
    {
        await File.WriteAllTextAsync(CaminhoArquivo, "SQLite inválido"); var repositorio = new RepositorioProjetosSqlite();
        await Assert.ThrowsAsync<InvalidDataException>(() => repositorio.CarregarAsync(CaminhoArquivo));
        await Assert.ThrowsAnyAsync<Exception>(() => repositorio.SalvarAsync(CaminhoArquivo, Projeto.Criar("x")));
        Assert.Equal("SQLite inválido", await File.ReadAllTextAsync(CaminhoArquivo));
        File.Delete(CaminhoArquivo); await repositorio.SalvarAsync(CaminhoArquivo, Projeto.Criar("x"));
        using (var connection = new SqliteConnection($"Data Source={CaminhoArquivo};Pooling=False"))
        {
            connection.Open(); using var comando = connection.CreateCommand();
            comando.CommandText = "PRAGMA user_version=999"; comando.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => repositorio.CarregarAsync(CaminhoArquivo));
        await Assert.ThrowsAsync<InvalidDataException>(() => repositorio.SalvarAsync(CaminhoArquivo, Projeto.Criar("x")));
    }
    [Fact] public async Task EdicaoDuranteSalvamentoPermanecePendente()
    {
        var s = Sessao(); var repositorio = new RepositorioBloqueante(); var persistencia = new PersistenciaProjeto(s, repositorio, CaminhoArquivo);
        var salvando = persistencia.SalvarAsync(); await repositorio.Iniciado.Task;
        s.Executar(new DefinirVolume(s.Atual.LinhaDoTempo[0].Id, 50)); repositorio.Continuar.SetResult(); await salvando;
        Assert.True(s.TemAlteracoes);
    }
    private sealed class RelogioManual : TimeProvider
    {
        private DateTimeOffset agora = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => agora;
        public void Avancar(int segundos) => agora = agora.AddSeconds(segundos);
    }
    [Fact] public async Task FalhaNoPrimeiroSalvamentoNaoDeixaDestinoInvalido()
    {
        var repositorio = new RepositorioProjetosSqlite();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repositorio.SalvarAsync(CaminhoArquivo, Projeto.Criar("x"), new(true)));
        Assert.False(File.Exists(CaminhoArquivo));
        await repositorio.SalvarAsync(CaminhoArquivo, Projeto.Criar("x")); Assert.True(File.Exists(CaminhoArquivo));
    }
    [Fact] public async Task RecuperacaoDeOutroProjetoEhRejeitada()
    {
        var repositorio = new RepositorioProjetosSqlite(); var persistencia = new PersistenciaProjeto(Sessao(), repositorio, CaminhoArquivo);
        await repositorio.SalvarAsync(persistencia.CaminhoRecuperacao, Projeto.Criar("Outro"));
        await Assert.ThrowsAsync<InvalidDataException>(() => persistencia.ObterRecuperacaoAsync());
    }
    [Fact] public async Task FechamentoComAlteracoesExigeDescarteExplicito()
    {
        var persistencia = new PersistenciaProjeto(Sessao(), new RepositorioProjetosSqlite(), CaminhoArquivo);
        await Assert.ThrowsAsync<InvalidOperationException>(() => persistencia.FecharAsync());
        await persistencia.FecharAsync(descartarAlteracoesNaoSalvas: true);
    }
    private class RepositorioContador : IRepositorioProjetos
    {
        public int Gravacoes { get; private set; }
        public virtual Task SalvarAsync(string caminho, Projeto projeto, CancellationToken cancellationToken = default) { Gravacoes++; return Task.CompletedTask; }
        public Task<Projeto> CarregarAsync(string caminho, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    [Fact] public async Task SolicitacoesConcorrentesDeSalvamentoAutomaticoGravamUmaVez()
    {
        var sessao = Sessao(); var relogio = new RelogioManual(); relogio.Avancar(1);
        var repositorio = new RepositorioBloqueante(); var persistencia = new PersistenciaProjeto(sessao, repositorio, CaminhoArquivo, relogio);
        relogio.Avancar(120);
        var primeiro = persistencia.SalvarAutomaticamenteSeNecessarioAsync(); await repositorio.Iniciado.Task;
        var segundo = persistencia.SalvarAutomaticamenteSeNecessarioAsync(); repositorio.Continuar.SetResult();
        Assert.True(await primeiro); Assert.False(await segundo); Assert.False(sessao.TemAlteracoes);
    }
    private sealed class RepositorioComFalha : RepositorioContador
    { public override Task SalvarAsync(string caminho, Projeto projeto, CancellationToken cancellationToken = default) => throw new IOException("Disco cheio"); }
    private sealed class RepositorioBloqueante : RepositorioContador
    {
        public TaskCompletionSource Iniciado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continuar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task SalvarAsync(string caminho, Projeto projeto, CancellationToken cancellationToken = default)
        { Iniciado.TrySetResult(); await Continuar.Task; }
    }
}
