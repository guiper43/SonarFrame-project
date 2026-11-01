using SonarFrame.Core;

namespace SonarFrame.Application;

public interface IRepositorioProjetos
{
    Task SalvarAsync(string caminho, Projeto projeto, CancellationToken cancellationToken = default);
    Task<Projeto> CarregarAsync(string caminho, CancellationToken cancellationToken = default);
}
public interface ILeitorMetadadosMidia
{
    Task<Midia> LerAsync(string caminho, CancellationToken cancellationToken = default);
}
public interface INavegadorQuadros
{
    TempoMidia ProximoQuadro(Midia midia, TempoMidia posicaoNaOrigem, int direcao);
}
public interface IReprodutorPrevia
{
    Task CarregarAsync(Projeto snapshot, CancellationToken cancellationToken = default);
    Task PosicionarAsync(TempoMidia posicao, CancellationToken cancellationToken = default);
    Task ReproduzirAsync(CancellationToken cancellationToken = default);
    Task PausarAsync(CancellationToken cancellationToken = default);
}
public enum Orientacao { Horizontal, Vertical }
public enum ModoEnquadramento { Preencher, Letterbox }
public sealed record OpcoesExportacao(string Destino, Orientacao? Orientacao, ModoEnquadramento Enquadramento, int Altura = 1080)
{
    public void Validar()
    {
        if (string.IsNullOrWhiteSpace(Destino) || Orientacao is null || !Enum.IsDefined(Orientacao.Value) || !Enum.IsDefined(Enquadramento) || Altura is not (720 or 1080))
            throw new ArgumentException("Escolha destino, orientação e resolução válidos.");
    }
}
public sealed record ProgressoOperacao(string Estado, double? Porcentagem, TimeSpan? Restante);
public interface IExportador
{
    Task ExportarAsync(Projeto snapshot, OpcoesExportacao opcoes, IProgress<ProgressoOperacao> progresso, CancellationToken cancellationToken = default);
}
public sealed record NotificacaoEditor(string Codigo, string Mensagem, bool EhErro = false);
