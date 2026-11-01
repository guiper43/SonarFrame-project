using System.Diagnostics;
using System.Security.Cryptography;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Tests;

public sealed class MidiasReaisFactAttribute : FactAttribute
{
    public MidiasReaisFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")) || !File.Exists(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")))
            Skip = "Execute setup-media.ps1 e build.ps1 para habilitar a integração com FFmpeg real.";
    }
}

public sealed class TestesMidiasReais
{
    [MidiasReaisFact]
    public async Task ImportacaoEmLoteDeFormatosReaisESqlitePreservamOriginais()
    {
        var pasta = Path.Combine(AppContext.BaseDirectory, "real-media", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var caminhos = new List<string>();
            foreach (var (extensao, codec) in new[] { ("mp3", "libmp3lame"), ("wav", "pcm_s16le"), ("m4a", "aac"), ("ogg", "libopus") })
            {
                var caminho = Path.Combine(pasta, $"áudio d'exemplo.{extensao}");
                await Gerar("-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "0.5", "-c:a", codec, caminho);
                caminhos.Add(caminho);
            }
            foreach (var extensao in new[] { "mp4", "mov" })
            {
                var caminho = Path.Combine(pasta, $"vídeo de teste.{extensao}");
                await Gerar("-f", "lavfi", "-i", "color=c=blue:s=64x64:r=30000/1001", "-t", "1.001", "-c:v", "libx264", "-pix_fmt", "yuv420p", caminho);
                caminhos.Add(caminho);
            }
            foreach (var extensao in new[] { "png", "jpg", "jpeg" })
            {
                var caminho = Path.Combine(pasta, $"imagem.{extensao}");
                await Gerar("-f", "lavfi", "-i", "color=c=blue:s=64x64", "-frames:v", "1", "-update", "1", caminho);
                caminhos.Add(caminho);
            }
            var hashes = caminhos.Select(p => SHA256.HashData(File.ReadAllBytes(p))).ToArray();
            var corrompido = Path.Combine(pasta, "corrompido.mp4");
            await File.WriteAllTextAsync(corrompido, "not media"); caminhos.Insert(3, corrompido);
            var sessao = new SessaoEdicao(Projeto.Criar("Importação real"));
            var leitorMetadados = new LeitorMetadadosFfprobe(new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!));
            var resultado = await ImportacaoMidias.ImportarAsync(sessao, leitorMetadados, caminhos);
            Assert.Equal(9, resultado.MidiasImportadasIds.Count); Assert.Single(resultado.Falhas);
            Assert.Empty(sessao.Atual.LinhaDoTempo);
            Assert.Equal(4, sessao.Atual.Biblioteca.Count(a => a.Tipo == TipoMidia.Audio));
            Assert.Equal(3, sessao.Atual.Biblioteca.Count(a => a.Tipo == TipoMidia.Imagem));
            Assert.All(sessao.Atual.Biblioteca.Where(a => a.Tipo == TipoMidia.Video), a =>
            {
                Assert.Equal(new TaxaQuadros(30000, 1001), a.TaxaQuadros);
                Assert.Equal(new TempoMidia(1001, 1000), a.Duracao);
            });
            sessao.Executar(new InserirClipes([.. resultado.MidiasImportadasIds], TempoMidia.Segundos(3)));
            var repositorio = new RepositorioProjetosSqlite(); var arquivoProjeto = Path.Combine(pasta, "project.sonarframe");
            await repositorio.SalvarAsync(arquivoProjeto, sessao.Atual);
            var reaberto = await repositorio.CarregarAsync(arquivoProjeto);
            Assert.Equal(9, reaberto.Biblioteca.Length); Assert.Equal(9, reaberto.LinhaDoTempo.Length);
            caminhos.Remove(corrompido);
            for (var i = 0; i < caminhos.Count; i++) Assert.Equal(hashes[i], SHA256.HashData(await File.ReadAllBytesAsync(caminhos[i])));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [MidiasReaisFact]
    public async Task ProcessoRealRespeitaCancelamentoETimeout()
    {
        var caminho = Path.Combine(AppContext.BaseDirectory, "probe-cancellation-" + Guid.NewGuid() + ".mp4");
        await File.WriteAllTextAsync(caminho, "fixture");
        try
        {
            var executor = new ExecutorFfprobe(Environment.GetEnvironmentVariable("SONARFRAME_FFPROBE")!, TimeSpan.FromTicks(1));
            await Assert.ThrowsAsync<TimeoutException>(() => executor.InspecionarAsync(caminho, default));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.InspecionarAsync(caminho, new CancellationToken(true)));
        }
        finally { File.Delete(caminho); }
    }

    private static async Task Gerar(params string[] argumentos)
    {
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var inicio = new ProcessStartInfo(Environment.GetEnvironmentVariable("SONARFRAME_FFMPEG")!)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        inicio.ArgumentList.Add("-v"); inicio.ArgumentList.Add("error"); inicio.ArgumentList.Add("-y");
        foreach (var argumento in argumentos) inicio.ArgumentList.Add(argumento);
        using var processo = Process.Start(inicio)!;
        try
        {
            var erro = processo.StandardError.ReadToEndAsync(prazo.Token);
            await processo.WaitForExitAsync(prazo.Token);
            Assert.True(processo.ExitCode == 0, await erro);
        }
        finally { if (!processo.HasExited) { processo.Kill(true); await processo.WaitForExitAsync(); } }
    }
}
