using System.IO;

namespace SonarFrame.Desktop;

public partial class JanelaPrincipal
{
    internal async Task AbrirArgumentosAsync(IReadOnlyList<string> argumentos)
    {
        if (argumentos.Count == 0) return;
        if (argumentos.Count != 1 || string.IsNullOrWhiteSpace(argumentos[0])
            || !string.Equals(Path.GetExtension(argumentos[0]), ".sonarframe", StringComparison.OrdinalIgnoreCase))
        {
            Anunciar("Informe apenas um arquivo de projeto com extensão .sonarframe para abrir.");
            return;
        }
        try
        {
            var caminho = Path.GetFullPath(argumentos[0]);
            if (!File.Exists(caminho) && !File.Exists(caminho + ".recovery"))
            {
                Anunciar("Não foi possível abrir: arquivo de projeto não encontrado. " + caminho);
                return;
            }
            var sessaoAnterior = sessao;
            await AbrirCaminhoAsync(caminho);
            if (sessao is not null && !ReferenceEquals(sessao, sessaoAnterior))
            {
                UpdateLayout();
                BotaoAdicionar.Focus();
            }
        }
        catch (Exception erro) when (erro is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Anunciar("Não foi possível abrir o projeto recebido: " + erro.Message);
        }
    }
}
