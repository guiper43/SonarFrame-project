using System.Runtime.InteropServices;

namespace SonarFrame.Infrastructure;

internal static class PastasUsuario
{
    internal static string Downloads => OperatingSystem.IsWindows()
        ? ResolverDownloads(ConsultarDownloadsWindows)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    internal static string ResolverDownloads(Func<string> consultar)
    {
        var caminho = consultar();
        if (string.IsNullOrWhiteSpace(caminho) || !Path.IsPathFullyQualified(caminho))
            throw new IOException("O Windows não informou uma pasta Downloads válida.");
        return Path.GetFullPath(caminho);
    }

    private static string ConsultarDownloadsWindows()
    {
        var inicializacao = CoInitializeEx(IntPtr.Zero, 2);
        if (inicializacao < 0 && inicializacao != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(inicializacao);
        var ponteiro = IntPtr.Zero;
        try
        {
            var downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(ref downloads, 0x4000, IntPtr.Zero, out ponteiro));
            return Marshal.PtrToStringUni(ponteiro) ?? throw new IOException("Não foi possível localizar Downloads.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(ponteiro);
            if (inicializacao >= 0) CoUninitialize();
        }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint flags, IntPtr token, out IntPtr caminho);

    [DllImport("ole32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CoInitializeEx(IntPtr reservado, uint modelo);

    [DllImport("ole32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void CoUninitialize();
}
