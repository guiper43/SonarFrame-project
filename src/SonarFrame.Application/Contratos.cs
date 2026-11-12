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
public sealed record OpcoesExportacao(string Destino, Orientacao? Orientacao, ModoEnquadramento Enquadramento, int Altura = 1080, bool SubstituirExistente = false)
{
    private static readonly HashSet<string> NomesReservadosWindows = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool NomeArquivoValido(string? nomeOuCaminho)
    {
        if (string.IsNullOrWhiteSpace(nomeOuCaminho)) return false;
        string nomeArquivo;
        try { nomeArquivo = Path.GetFileName(nomeOuCaminho.Trim()); }
        catch (ArgumentException) { return false; }
        if (string.IsNullOrWhiteSpace(nomeArquivo) || nomeArquivo.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;
        if (!string.Equals(Path.GetExtension(nomeArquivo), ".mp4", StringComparison.OrdinalIgnoreCase))
            return false;
        var nomeBase = Path.GetFileNameWithoutExtension(nomeArquivo);
        if (string.IsNullOrWhiteSpace(nomeBase)) return false;
        var radical = nomeBase.Split('.')[0].Trim();
        return !string.IsNullOrEmpty(radical) && !NomesReservadosWindows.Contains(nomeBase.Trim()) && !NomesReservadosWindows.Contains(radical);
    }

    public void Validar()
    {
        if (string.IsNullOrWhiteSpace(Destino) || Orientacao is null || !Enum.IsDefined(Orientacao.Value) || !Enum.IsDefined(Enquadramento) || Altura is not (720 or 1080))
            throw new ArgumentException("Escolha destino, orientação e resolução válidos.");
        if (!string.Equals(Path.GetExtension(Destino), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("O arquivo exportado deve ter a extensão .mp4.");
        if (!NomeArquivoValido(Destino))
            throw new ArgumentException("Informe um nome de arquivo MP4 válido.");
    }
    public (int Largura, int Altura) Dimensoes()
    {
        Validar();
        var lado = Altura * 16 / 9;
        return Orientacao == Application.Orientacao.Horizontal ? (lado, Altura) : (Altura, lado);
    }
}
public sealed record ProgressoOperacao(string Estado, double? Porcentagem, TimeSpan? Restante);
public interface IExportador
{
    Task ExportarAsync(Projeto snapshot, OpcoesExportacao opcoes, IProgress<ProgressoOperacao> progresso, CancellationToken cancellationToken = default);
}
public sealed record NotificacaoEditor(string Codigo, string Mensagem, bool EhErro = false);
