using SonarFrame.Core;

namespace SonarFrame.Application;

public sealed record FalhaImportacao(string Caminho, string Mensagem);
public sealed record ResultadoImportacao(IReadOnlyList<Guid> MidiasImportadasIds, IReadOnlyList<FalhaImportacao> Falhas, bool FoiCancelado = false);
public sealed record ProgressoImportacao(int Concluidos, int Total, string ArquivoAtual);
public static class ImportacaoMidias
{
    public static async Task<ResultadoImportacao> ImportarAsync(SessaoEdicao sessao, ILeitorMetadadosMidia leitorMetadados, IEnumerable<string> caminhos, CancellationToken token = default, IProgress<ProgressoImportacao>? progresso = null)
    {
        var importados = new List<Guid>();
        var falhas = new List<FalhaImportacao>();
        var arquivos = caminhos.ToArray();
        for (var i = 0; i < arquivos.Length; i++)
        {
            if (token.IsCancellationRequested) return new(importados, falhas, true);
            var caminho = arquivos[i];
            progresso?.Report(new(i, arquivos.Length, Path.GetFileName(caminho)));
            try
            {
                var midia = await leitorMetadados.LerAsync(caminho, token);
                token.ThrowIfCancellationRequested();
                sessao.Executar(new RegistrarMidia(midia));
                importados.Add(midia.Id);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { return new(importados, falhas, true); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                falhas.Add(new(caminho, e.Message));
                sessao.Notificar(new("importacao.falhou", $"{caminho}: {e.Message}", true));
            }
            progresso?.Report(new(i + 1, arquivos.Length, Path.GetFileName(caminho)));
        }
        return new(importados, falhas);
    }
    public static IReadOnlyList<Midia> EncontrarAusentes(Projeto projeto) => projeto.Biblioteca.Where(a => !File.Exists(a.Caminho)).ToArray();
}
