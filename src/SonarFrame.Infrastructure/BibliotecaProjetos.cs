using System.Text.Json;
using SonarFrame.Application;

namespace SonarFrame.Infrastructure;

public sealed record ConfiguracoesDesktop(
    [property: System.Text.Json.Serialization.JsonPropertyName("ProjectsDirectory")] string DiretorioProjetos,
    string? DiretorioExportacoes = null)
{
    public static string PastaProjetosPadrao => Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "sonarFrame"));
    public static string PastaDownloadsPadrao => PastasUsuario.Downloads;

    public static string PastaExportacoesPadrao(string pastaProjetos, string? pastaDownloads = null)
    {
        var baseDownloads = string.IsNullOrWhiteSpace(pastaDownloads) ? PastaDownloadsPadrao : pastaDownloads;
        return MesmaPasta(pastaProjetos, baseDownloads)
            ? Path.Combine(baseDownloads, "sonarFrame-exportacoes") : baseDownloads;
    }

    [System.Text.Json.Serialization.JsonPropertyName("ExportsDirectory")]
    public string DiretorioExportacoes { get; init; } = string.IsNullOrWhiteSpace(DiretorioExportacoes) ? PastaExportacoesPadrao(DiretorioProjetos) : DiretorioExportacoes;

    public static ConfiguracoesDesktop Padrao => new(PastaProjetosPadrao);

    public void Validar()
    {
        if (string.IsNullOrWhiteSpace(DiretorioProjetos) || !Path.IsPathFullyQualified(DiretorioProjetos))
            throw new ArgumentException("Escolha uma pasta absoluta para os projetos.");
        if (string.IsNullOrWhiteSpace(DiretorioExportacoes) || !Path.IsPathFullyQualified(DiretorioExportacoes))
            throw new ArgumentException("Escolha uma pasta absoluta para os vídeos exportados.");
        if (MesmaPasta(DiretorioProjetos, DiretorioExportacoes))
            throw new ArgumentException("A pasta de vídeos exportados deve ser diferente da pasta de projetos para não misturar os arquivos.");
    }

    public static bool MesmaPasta(string? primeira, string? segunda)
    {
        if (string.IsNullOrWhiteSpace(primeira) || string.IsNullOrWhiteSpace(segunda)) return false;
        try
        {
            var caminhoA = Path.TrimEndingDirectorySeparator(Path.GetFullPath(primeira));
            var caminhoB = Path.TrimEndingDirectorySeparator(Path.GetFullPath(segunda));
            return string.Equals(caminhoA, caminhoB, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
public sealed class RepositorioConfiguracoes(string caminho)
{
    public ConfiguracoesDesktop Carregar()
    {
        if (!File.Exists(caminho)) return ConfiguracoesDesktop.Padrao;
        var configuracoes = JsonSerializer.Deserialize<ConfiguracoesDesktop>(File.ReadAllText(caminho)) ?? throw new InvalidDataException("Configurações inválidas.");
        if (string.IsNullOrWhiteSpace(configuracoes.DiretorioProjetos) || !Path.IsPathFullyQualified(configuracoes.DiretorioProjetos))
            throw new InvalidDataException("Pasta de projetos inválida.");
        if (string.IsNullOrWhiteSpace(configuracoes.DiretorioExportacoes) || !Path.IsPathFullyQualified(configuracoes.DiretorioExportacoes))
            throw new InvalidDataException("Pasta de exportação inválida.");
        if (ConfiguracoesDesktop.MesmaPasta(configuracoes.DiretorioProjetos, configuracoes.DiretorioExportacoes))
            throw new InvalidDataException("A pasta de exportação não pode ser igual à pasta de projetos.");
        return configuracoes;
    }
    public void Salvar(ConfiguracoesDesktop configuracoes)
    {
        ArgumentNullException.ThrowIfNull(configuracoes);
        configuracoes.Validar();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caminho))!);
        var temporario = caminho + ".tmp";
        File.WriteAllText(temporario, JsonSerializer.Serialize(configuracoes));
        File.Move(temporario, caminho, true);
    }
}
public sealed record EntradaProjeto(string Caminho, string Nome, DateTime UltimaModificacaoUtc, bool TemRecuperacao);
public sealed record ResultadoCatalogo(IReadOnlyList<EntradaProjeto> Projetos, IReadOnlyList<string> Erros);
public sealed class CatalogoProjetos(IRepositorioProjetos repositorio)
{
    public async Task<ResultadoCatalogo> LerAsync(string diretorio, CancellationToken token = default)
    {
        var entradas = new List<EntradaProjeto>(); var erros = new List<string>();
        if (!Directory.Exists(diretorio)) return new(entradas, erros);
        var candidatos = Directory.EnumerateFiles(diretorio, "*.sonarframe")
            .Concat(Directory.EnumerateFiles(diretorio, "*.sonarframe.recovery").Select(p => p[..^9]))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var caminho in candidatos)
        {
            token.ThrowIfCancellationRequested();
            var origem = File.Exists(caminho) ? caminho : caminho + ".recovery";
            try
            {
                SonarFrame.Core.Projeto projeto;
                try { projeto = await repositorio.CarregarAsync(origem, token); }
                catch (Exception e) when ((e is IOException or InvalidDataException) && origem == caminho && File.Exists(caminho + ".recovery"))
                {
                    origem = caminho + ".recovery";
                    projeto = await repositorio.CarregarAsync(origem, token);
                }
                entradas.Add(new(caminho, projeto.Nome, File.GetLastWriteTimeUtc(origem), File.Exists(caminho + ".recovery")));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            { erros.Add($"{Path.GetFileName(caminho)}: {e.Message}"); }
        }
        return new(entradas.OrderByDescending(e => e.UltimaModificacaoUtc).ToArray(), erros);
    }
    public static string GerarCaminho(string diretorio, string nome)
    {
        var nomeSeguro = string.Concat(nome.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (nomeSeguro.Length > 60) nomeSeguro = nomeSeguro[..60];
        if (nomeSeguro.Length == 0) nomeSeguro = "Projeto";
        return Path.Combine(Path.GetFullPath(diretorio), $"{nomeSeguro}-{Guid.NewGuid():N}.sonarframe");
    }
}
