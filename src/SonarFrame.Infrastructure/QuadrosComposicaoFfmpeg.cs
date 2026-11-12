using System.Diagnostics;
using System.Text;
using SonarFrame.Application;
using SonarFrame.Core;

namespace SonarFrame.Infrastructure;

public sealed partial class PreparadorComposicaoFfmpeg
{
    public async Task<TempoMidia> ProximaPosicaoAsync(Projeto projeto, int direcao, CancellationToken token)
    {
        if (direcao is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direcao));
        token.ThrowIfCancellationRequested();
        var inicio = TempoMidia.Zero;
        foreach (var clipe in projeto.LinhaDoTempo)
        {
            var fim = inicio + clipe.Duracao;
            if (projeto.Cursor < fim || (direcao < 0 && projeto.Cursor == fim))
            {
                var midia = projeto.Biblioteca.Single(m => m.Id == clipe.MidiaId);
                var origem = clipe.InicioNaOrigem + projeto.Cursor - inicio;
                TempoMidia proximo;
                if (midia.Tipo == TipoMidia.Video)
                {
                    var tempos = await indiceQuadros.LerAsync(midia, origem, token).ConfigureAwait(false);
                    var indice = IndiceQuadros.LimiteSuperior(tempos, origem);
                    if (direcao < 0)
                    {
                        indice--;
                        if (indice >= 0 && tempos[indice] == origem) indice--;
                    }
                    proximo = indice < 0 ? clipe.InicioNaOrigem : indice >= tempos.Length ? clipe.InicioNaOrigem + clipe.Duracao : tempos[indice];
                }
                else proximo = origem.QuadroAdjacente(new(30, 1), direcao);
                return TempoMidia.Maximo(inicio, TempoMidia.Minimo(fim, inicio + proximo - clipe.InicioNaOrigem));
            }
            inicio = fim;
        }
        return projeto.Duracao;
    }

    public async Task<QuadroComposicao?> ObterQuadroAsync(Projeto projeto, TempoMidia posicao, CancellationToken token)
    {
        projeto.Validar();
        if (projeto.LinhaDoTempo.IsEmpty) return null;
        var chave = await Task.Run(() => Chave(projeto), token).ConfigureAwait(false);
        var destino = TempoMidia.Maximo(TempoMidia.Zero, TempoMidia.Minimo(posicao, projeto.Duracao));
        var instante = destino == projeto.Duracao ? TempoMidia.Maximo(TempoMidia.Zero, destino - new TempoMidia(1, 1000000000)) : destino;
        var metadados = await LerMetadadosAsync(Fontes(projeto), null, token).ConfigureAwait(false);
        var (largura, altura) = DimensoesQuadro(projeto, metadados);
        var argumentos = new List<string> { "-v", "error", "-nostdin" };
        var filtros = new StringBuilder($"color=c=black:s={largura}x{altura}:r=1,format=rgba[base];");
        var entrada = 0;
        var visual = "base";
        async Task AdicionarAsync(Clipe clipe, TempoMidia inicio)
        {
            var midia = projeto.Biblioteca.Single(m => m.Id == clipe.MidiaId);
            var dados = metadados[midia.Id];
            if (!dados.Video) return;
            if (midia.Tipo == TipoMidia.Video)
            {
                var tempos = await indiceQuadros.LerAsync(midia, clipe.InicioNaOrigem + instante - inicio, token).ConfigureAwait(false);
                var origem = clipe.InicioNaOrigem + instante - inicio;
                var indice = Math.Max(0, IndiceQuadros.LimiteSuperior(tempos, origem) - 1);
                argumentos.AddRange(["-ss", Segundos(IndiceQuadros.BuscaParaQuadro(tempos, indice))]);
            }
            argumentos.AddRange(["-i", Path.GetFullPath(midia.Caminho)]);
            filtros.Append($"[{entrada}:{dados.IndiceVideo}]trim=end_frame=1,setpts=0,{Escala(largura, altura, true)}[q{entrada}];");
            filtros.Append($"[{visual}][q{entrada}]overlay=shortest=1:format=auto[s{entrada}];");
            visual = $"s{entrada++}";
        }
        var inicio = TempoMidia.Zero;
        foreach (var clipe in projeto.LinhaDoTempo)
        {
            if (instante < inicio + clipe.Duracao) { await AdicionarAsync(clipe, inicio).ConfigureAwait(false); break; }
            inicio += clipe.Duracao;
        }
        foreach (var camada in projeto.Camadas.Where(c => c.Inicio <= instante && instante < c.Inicio + c.Clipe.Duracao))
            await AdicionarAsync(camada.Clipe, camada.Inicio).ConfigureAwait(false);
        filtros.Append($"[{visual}]format=rgb24[quadro]");
        argumentos.AddRange(["-filter_complex_threads", "1", "-filter_complex", filtros.ToString(), "-map", "[quadro]", "-frames:v", "1", "-an", "-threads", "1", "-f", "image2pipe", "-c:v", "png", "pipe:1"]);
        var png = await DecodificarPngAsync(argumentos, token).ConfigureAwait(false);
        if (await Task.Run(() => Chave(projeto), token).ConfigureAwait(false) != chave)
            throw new IOException("Uma mídia mudou durante a busca. Tente novamente.");
        return new(destino, png);
    }

    private async Task<byte[]> DecodificarPngAsync(IEnumerable<string> argumentos, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var inicio = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argumento in argumentos) inicio.ArgumentList.Add(argumento);
        using var processo = IniciarFfmpeg(inicio);
        using var registro = token.Register(() => { try { if (!processo.HasExited) processo.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        var erros = processo.StandardError.ReadToEndAsync();
        using var imagem = new MemoryStream();
        var leitura = processo.StandardOutput.BaseStream.CopyToAsync(imagem, token);
        try
        {
            await Task.WhenAll(leitura, processo.WaitForExitAsync(token)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (processo.ExitCode != 0 || imagem.Length == 0)
            {
                var mensagem = await erros.ConfigureAwait(false);
                throw new InvalidDataException("Não foi possível decodificar o quadro: " + mensagem[..Math.Min(mensagem.Length, 1500)]);
            }
            return imagem.ToArray();
        }
        finally
        {
            if (!processo.HasExited) { processo.Kill(entireProcessTree: true); await processo.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
            try { await leitura.ConfigureAwait(false); } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            await erros.ConfigureAwait(false);
        }
    }
}
