using SonarFrame.Core;

namespace SonarFrame.Application;

public sealed class PersistenciaProjeto
{
    private readonly SessaoEdicao sessao;
    private readonly IRepositorioProjetos repositorio;
    private readonly SemaphoreSlim gravacoes = new(1, 1);
    private readonly TimeProvider relogio;
    private DateTimeOffset ultimaVerificacao;
    public string CaminhoProjeto { get; }
    public string CaminhoRecuperacao => CaminhoProjeto + ".recovery";
    public static TimeSpan IntervaloSalvamentoAutomatico => TimeSpan.FromMinutes(2);
    public PersistenciaProjeto(SessaoEdicao sessao, IRepositorioProjetos repositorio, string caminhoProjeto, TimeProvider? relogio = null)
    {
        this.sessao = sessao; this.repositorio = repositorio;
        CaminhoProjeto = Path.GetFullPath(caminhoProjeto);
        this.relogio = relogio ?? TimeProvider.System;
        ultimaVerificacao = this.relogio.GetUtcNow();
    }
    public async Task SalvarAsync(CancellationToken token = default)
    {
        await gravacoes.WaitAsync(token);
        try { await SalvarSnapshotAsync(token); }
        finally { gravacoes.Release(); }
    }
    private async Task SalvarSnapshotAsync(CancellationToken token)
    {
        try
        {
            var snapshot = sessao.Atual;

            await repositorio.SalvarAsync(CaminhoRecuperacao, snapshot, token);
            await repositorio.SalvarAsync(CaminhoProjeto, snapshot, token);
            sessao.MarcarComoSalvo(snapshot);
            ultimaVerificacao = relogio.GetUtcNow();
            sessao.Notificar(new("project.saved", "Projeto salvo."));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        { sessao.Notificar(new("save.failed", e.Message, true)); throw; }
    }
    public async Task<bool> SalvarAutomaticamenteSeNecessarioAsync(CancellationToken token = default)
    {
        await gravacoes.WaitAsync(token);
        try
        {
            if (relogio.GetUtcNow() - ultimaVerificacao < IntervaloSalvamentoAutomatico || !sessao.TemAlteracoes) return false;
            await SalvarSnapshotAsync(token);
            return true;
        }
        finally { gravacoes.Release(); }
    }
    public async Task ExecutarSalvamentoAutomaticoAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(IntervaloSalvamentoAutomatico, relogio);
        while (await timer.WaitForNextTickAsync(token))
        {
            try { await SalvarAutomaticamenteSeNecessarioAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e) { sessao.Notificar(new("autosave.failed", e.Message, true)); }
        }
    }
    public Task<Projeto?> ObterRecuperacaoAsync(CancellationToken token = default) => LerRecuperacao(token);
    private async Task<Projeto?> LerRecuperacao(CancellationToken token)
    {
        if (!File.Exists(CaminhoRecuperacao)) return null;
        var recuperado = await repositorio.CarregarAsync(CaminhoRecuperacao, token);
        if (recuperado.Id != sessao.Atual.Id) throw new InvalidDataException("Recuperação pertence a outro projeto.");
        return recuperado;
    }
    public async Task FecharAsync(bool descartarAlteracoesNaoSalvas = false, CancellationToken token = default)
    {
        await gravacoes.WaitAsync(token);
        try
        {
            if (sessao.TemAlteracoes && !descartarAlteracoesNaoSalvas) throw new InvalidOperationException("Salve ou descarte explicitamente as alterações antes de fechar.");
            if (File.Exists(CaminhoRecuperacao)) File.Delete(CaminhoRecuperacao);
        }
        finally { gravacoes.Release(); }
    }
}
