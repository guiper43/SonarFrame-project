using System.IO;
using System.Windows;

namespace SonarFrame.ProvaReproducao;

internal static class Programa
{
    [STAThread]
    private static int Main(string[] argumentos)
    {
        var pasta = Path.GetFullPath(Valor(argumentos, "--pasta") ?? ".tools/test-artifacts/prova-reproducao");
        Directory.CreateDirectory(pasta);
        try
        {
            var ffmpeg = Path.GetFullPath(Valor(argumentos, "--ffmpeg") ?? Ferramenta("ffmpeg"));
            var ffprobe = Path.GetFullPath(Valor(argumentos, "--ffprobe") ?? Ferramenta("ffprobe"));
            var app = new System.Windows.Application();
            var janela = new JanelaProva(pasta, ffmpeg, ffprobe, argumentos.Contains("--automatico"), argumentos.Contains("--benchmark"));
            return app.Run(janela);
        }
        catch (Exception erro)
        {
            File.WriteAllText(Path.Combine(pasta, "falha.txt"), erro.ToString());
            return 1;
        }
    }

    private static string Ferramenta(string nome)
    {
        var juntoAoExecutavel = Path.Combine(AppContext.BaseDirectory, nome + ".exe");
        return File.Exists(juntoAoExecutavel) ? juntoAoExecutavel : $".tools/ffmpeg/{nome}.exe";
    }

    private static string? Valor(string[] argumentos, string nome)
    {
        var indice = Array.IndexOf(argumentos, nome);
        return indice >= 0 && indice + 1 < argumentos.Length ? argumentos[indice + 1] : null;
    }
}
