using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SonarFrame.Application;
using SonarFrame.Core;

namespace SonarFrame.Infrastructure;

public sealed partial class PreparadorComposicaoFfmpeg
{
    private SessaoProgressiva? sessaoProgressiva;
    private string ExecutavelFfmpeg => ffmpeg;
    private IExecutorFfprobe LeitorFfprobe => ffprobe;

    private string CaminhoBloco(Projeto bloco, int largura, int altura, bool audio)
    {
        var identidade = JsonSerializer.SerializeToUtf8Bytes(new { Perfil = 2, Largura = audio ? 0 : largura, Altura = audio ? 0 : altura, Conteudo = ChaveConteudo(bloco, audio) });
        return Path.Combine(pasta, "bloco-" + (audio ? "audio-" : "video-") + Convert.ToHexString(SHA256.HashData(identidade)) + ".nut");
    }

    public async Task<IFonteComposicao?> IniciarReproducaoAsync(Projeto projeto, TempoMidia inicio, TempoMidia fim, IProgress<string>? progresso, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) return null;
        projeto.Validar();
        if (inicio < TempoMidia.Zero || inicio >= fim || fim > projeto.Duracao) throw new ArgumentException("Intervalo de reprodução inválido.");
        if (sessaoProgressiva is { } anterior) await anterior.DisposeAsync();
        await acessoPreparacao.WaitAsync(token).ConfigureAwait(false);
        SessaoProgressiva? criada = null;
        try
        {
            criada = await Task.Run(async () =>
            {
                PrepararPasta();
                preparada = null; chaveAtual = null;
                foreach (var nome in new[] { "previa.mp4", "previa.mov", "intervalo.mp4", "preparando.mp4", "preparando.mov", "preparando.nut", "audio.m4a" }) File.Delete(Path.Combine(pasta, nome));
                projeto = Normalizar(projeto);
                var chave = Chave(projeto);
                var metadados = await LerMetadadosAsync(Fontes(projeto), null, token).ConfigureAwait(false);
                var dimensoes = DimensoesQuadro(projeto, metadados);
                var blocos = Segmentos(projeto).Where(s => s.Fim > inicio && s.Inicio < fim).Select(s =>
                {
                    var de = TempoMidia.Maximo(inicio, s.Inicio);
                    var ate = TempoMidia.Minimo(fim, s.Fim);
                    var recorte = RecortarBloco(projeto, de, ate);
                    return new BlocoProgressivo(recorte, ate - inicio, CaminhoBloco(recorte, dimensoes.Largura, dimensoes.Altura, false), CaminhoBloco(recorte, dimensoes.Largura, dimensoes.Altura, true));
                }).ToArray();
                return new SessaoProgressiva(this, projeto, chave, inicio, fim - inicio, metadados, dimensoes, blocos);
            }, token).ConfigureAwait(false);
            sessaoProgressiva = criada;
            progresso?.Report("Carregando vídeo: 0%.");
            await criada.AguardarReservaAsync(TempoMidia.Zero, TempoMidia.Segundos(3), token).ConfigureAwait(false);
            progresso?.Report("Carregando vídeo: 100%.");
            return criada;
        }
        catch
        {
            if (criada is not null) await criada.DisposeAsync().ConfigureAwait(false);
            else acessoPreparacao.Release();
            throw;
        }
    }

    private sealed record BlocoProgressivo(Projeto Projeto, TempoMidia Fim, string Video, string Audio)
    {
        public TaskCompletionSource Pronto { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class SessaoProgressiva : IFonteComposicao
    {
        private readonly PreparadorComposicaoFfmpeg dono;
        private readonly Projeto projeto;
        private readonly string chave;
        private readonly Dictionary<Guid, Metadados> metadados;
        private readonly (int Largura, int Altura) dimensoes;
        private readonly BlocoProgressivo[] blocos;
        private readonly CancellationTokenSource cancelamento = new();
        private readonly object bloqueio = new();
        private readonly ArquivoProgressivo arquivo;
        private readonly string identificador = Guid.NewGuid().ToString("N");
        private readonly List<string> arquivos = [];
        private TaskCompletionSource alterado = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TempoMidia disponivelAte;
        private TempoMidia posicao;
        private Exception? falha;
        private Task? descarte;
        public TempoMidia Inicio { get; }
        public TempoMidia Duracao { get; }
        public Task Conclusao { get; }
        public TempoMidia PreparadoAte { get { lock (bloqueio) return disponivelAte; } }
        public bool EstaAtualizada(Projeto atual)
        {
            lock (bloqueio) return descarte is null && falha is null && Chave(atual) == chave;
        }

        public SessaoProgressiva(PreparadorComposicaoFfmpeg dono, Projeto projeto, string chave, TempoMidia inicio, TempoMidia duracao,
            Dictionary<Guid, Metadados> metadados, (int Largura, int Altura) dimensoes, BlocoProgressivo[] blocos)
        {
            this.dono = dono; this.projeto = projeto; this.chave = chave; this.metadados = metadados; this.dimensoes = dimensoes; this.blocos = blocos;
            Inicio = inicio; Duracao = duracao;
            arquivo = new ArquivoProgressivo(Path.Combine(dono.pasta, identificador + ".nut"), LimiteBytes / 2 - 1024 * 1024);
            Conclusao = Task.Run(ProduzirAsync);
        }

        private void Sinalizar()
        {
            var anterior = alterado;
            alterado = new(TaskCreationOptions.RunContinuationsAsynchronously);
            anterior.TrySetResult();
        }

        public void AtualizarPosicao(TempoMidia atual)
        {
            lock (bloqueio) { posicao = atual; Sinalizar(); }
        }

        public async Task AguardarReservaAsync(TempoMidia atual, TempoMidia reserva, CancellationToken token)
        {
            var destino = TempoMidia.Minimo(atual + reserva, Duracao);
            using var espera = CancellationTokenSource.CreateLinkedTokenSource(token, cancelamento.Token);
            while (true)
            {
                espera.Token.ThrowIfCancellationRequested();
                Task mudanca;
                lock (bloqueio)
                {
                    if (falha is { } erro) throw new IOException("Não foi possível preparar a reprodução.", erro);
                    if (disponivelAte >= destino) return;
                    mudanca = alterado.Task;
                }
                var pronta = await Task.WhenAny(mudanca, Conclusao).WaitAsync(espera.Token).ConfigureAwait(false);
                if (ReferenceEquals(pronta, Conclusao)) await Conclusao.ConfigureAwait(false);
            }
        }

        private async Task RenderizarAsync()
        {
            var token = cancelamento.Token;
            var protegidos = blocos.SelectMany(b => new[] { b.Video, b.Audio }).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var temporario = Path.Combine(dono.pasta, identificador + "-bloco.nut");
            arquivos.Add(temporario);
            foreach (var bloco in blocos)
            {
                while (true)
                {
                    Task espera;
                    lock (bloqueio)
                    {
                        if (bloco.Fim <= posicao + TempoMidia.Segundos(15)) break;
                        espera = alterado.Task;
                    }
                    await espera.WaitAsync(token).ConfigureAwait(false);
                }
                foreach (var audio in new[] { false, true })
                {
                    token.ThrowIfCancellationRequested();
                    var caminho = audio ? bloco.Audio : bloco.Video;
                    if (!File.Exists(caminho))
                    {
                        dono.LiberarBlocos(protegidos, LimiteBytes / 2 - 32 * 1024 * 1024);
                        var disponivel = LimiteBytes / 2 - dono.TamanhoBlocos();
                        if (disponivel < 2 * 1024 * 1024) throw new IOException("O cache atingiu o limite de 2 GB. Reduza a composição para reproduzir.");
                        await dono.RenderizarBlocoAsync(bloco.Projeto, metadados, dimensoes.Largura, dimensoes.Altura, temporario, disponivel, audio, token).ConfigureAwait(false);
                        if (Chave(projeto) != chave) throw new IOException("Uma mídia mudou durante a preparação. Reproduza novamente para atualizar.");
                        File.Move(temporario, caminho);
                    }
                    dono.usosBlocos[caminho] = DateTime.UtcNow;
                }
                bloco.Pronto.TrySetResult();
            }
        }

        private async Task ProduzirAsync()
        {
            await using var servidor = new ServidorBlocos();
            Process? processo = null;
            Task<string>? erros = null;
            Task renderizacao = Task.CompletedTask;
            Task copia = Task.CompletedTask;

            try
            {
                var token = cancelamento.Token;
                var listas = new List<string>();
                foreach (var audio in new[] { false, true })
                {
                    var linhas = new List<string> { "ffconcat version 1.0" };
                    for (var i = 0; i < blocos.Length; i++)
                    {
                        var endereco = servidor.Adicionar(audio ? blocos[i].Audio : blocos[i].Video, blocos[i].Pronto.Task);
                        linhas.Add($"file '{endereco}'");
                        linhas.Add($"duration {Segundos(blocos[i].Projeto.Duracao)}");
                    }
                    var lista = Path.Combine(dono.pasta, identificador + (audio ? "-a.txt" : "-v.txt"));
                    arquivos.Add(lista);
                    await File.WriteAllLinesAsync(lista, linhas, token).ConfigureAwait(false);
                    listas.Add(lista);
                }
                renderizacao = RenderizarAsync();
                var inicio = new ProcessStartInfo(dono.ExecutavelFfmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argumento in new[] { "-v", "error", "-nostdin", "-stats_period", "0.001", "-progress", "pipe:2", "-analyzeduration", "100000", "-probesize", "32768", "-f", "concat", "-safe", "0", "-protocol_whitelist", "file,http,tcp", "-i", listas[0], "-analyzeduration", "100000", "-probesize", "32768", "-f", "concat", "-safe", "0", "-protocol_whitelist", "file,http,tcp", "-i", listas[1], "-map", "0:v:0", "-map", "1:a:0", "-c", "copy", "-flush_packets", "1", "-f", "nut", "pipe:1" }) inicio.ArgumentList.Add(argumento);
                processo = IniciarFfmpeg(inicio);
                erros = LerProgressoAsync(processo.StandardError);
                copia = arquivo.CopiarAsync(processo.StandardOutput.BaseStream, token);
                var fimProcesso = processo.WaitForExitAsync(token);
                var tarefas = new List<Task> { renderizacao, copia, fimProcesso };
                while (tarefas.Count > 0)
                {
                    var pronta = await Task.WhenAny(tarefas).ConfigureAwait(false);
                    await pronta.ConfigureAwait(false);
                    tarefas.Remove(pronta);
                    if (ReferenceEquals(pronta, fimProcesso) && processo.ExitCode != 0)
                    {
                        var erro = await erros.ConfigureAwait(false);
                        throw new IOException("Falha ao preparar composição: " + erro[..Math.Min(erro.Length, 1500)]);
                    }
                    if (ReferenceEquals(pronta, fimProcesso)) { await Task.WhenAll(renderizacao, copia).ConfigureAwait(false); break; }
                }
                if (Chave(projeto) != chave) throw new IOException("Uma mídia mudou durante a reprodução. Reproduza novamente para atualizar.");
                using var verificacao = JsonDocument.Parse(await dono.LeitorFfprobe.InspecionarAsync(arquivo.Caminho, token).ConfigureAwait(false));
                var formato = verificacao.RootElement.GetProperty("format");
                var duracao = decimal.Parse(formato.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
                if (formato.TryGetProperty("start_time", out var comeco)) duracao -= decimal.Parse(comeco.GetString()!, CultureInfo.InvariantCulture);
                if (Math.Abs(duracao - (decimal)Duracao.Numerador / Duracao.Denominador) > 0.08m) throw new InvalidDataException("A duração da reprodução não corresponde à composição.");
                arquivo.Encerrar();
                lock (bloqueio) { disponivelAte = Duracao; Sinalizar(); }
            }
            catch (Exception erro)
            {
                lock (bloqueio) { falha = erro; Sinalizar(); }
                arquivo.Encerrar(erro);
                cancelamento.Cancel();
                if (erro is not OperationCanceledException) throw;
            }
            finally
            {

                if (processo is not null)
                {
                    if (!processo.HasExited) { processo.Kill(true); await processo.WaitForExitAsync().ConfigureAwait(false); }
                    if (erros is not null) await erros.ConfigureAwait(false);
                    processo.Dispose();
                }

                try { await Task.WhenAll(renderizacao, copia).ConfigureAwait(false); }
                catch (Exception) when (cancelamento.IsCancellationRequested) { }
            }
        }

        private async Task<string> LerProgressoAsync(StreamReader leitor)
        {
            var erros = new StringBuilder();
            while (await leitor.ReadLineAsync().ConfigureAwait(false) is { } linha)
            {
                if (linha.StartsWith("out_time_us=", StringComparison.Ordinal)
                    && long.TryParse(linha.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var microssegundos) && microssegundos >= 0)
                {
                    lock (bloqueio)
                    {
                        disponivelAte = TempoMidia.Minimo(Duracao, TempoMidia.Maximo(disponivelAte, new(microssegundos, 1000000)));
                        Sinalizar();
                    }
                }
                else if (!linha.Contains('=') && erros.Length < 1500) erros.AppendLine(linha[..Math.Min(linha.Length, 1500 - erros.Length)]);
            }
            return erros.ToString();
        }

        public Stream AbrirLeitura() => arquivo.AbrirLeitura();
        public void Cancelar() { cancelamento.Cancel(); arquivo.Cancelar(); }
        public ValueTask DisposeAsync()
        {
            lock (bloqueio) return new(descarte ??= DescartarAsync());
        }
        private async Task DescartarAsync()
        {
            Cancelar();
            try { await Conclusao.ConfigureAwait(false); }
            catch (Exception) when (cancelamento.IsCancellationRequested) { }
            try
            {
                await arquivo.DisposeAsync().ConfigureAwait(false);
                foreach (var caminho in arquivos) File.Delete(caminho);
            }
            finally { cancelamento.Dispose(); dono.acessoPreparacao.Release(); }
        }
    }
}
