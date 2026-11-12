using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SonarFrame.Infrastructure;

internal sealed class ServidorBlocos : IAsyncDisposable
{
    private readonly TcpListener servidor = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource vida = new();
    private readonly Dictionary<string, (string Caminho, Task Pronto)> arquivos = [];
    private readonly List<Task> requisicoes = [];
    private readonly SemaphoreSlim limite = new(4, 4);
    private readonly string identificador = Guid.NewGuid().ToString("N");
    private readonly Task aceitacao;

    public ServidorBlocos()
    {
        servidor.Start(4);
        aceitacao = AceitarAsync();
    }

    public string Adicionar(string caminho, Task pronto)
    {
        lock (arquivos)
        {
            var rota = "/" + identificador + "/" + arquivos.Count.ToString(CultureInfo.InvariantCulture);
            arquivos.Add(rota, (caminho, pronto));
            return "http://127.0.0.1:" + ((IPEndPoint)servidor.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture) + rota;
        }
    }

    private async Task AceitarAsync()
    {
        try
        {
            while (!vida.IsCancellationRequested)
            {
                await limite.WaitAsync(vida.Token).ConfigureAwait(false);
                TcpClient cliente;
                try { cliente = await servidor.AcceptTcpClientAsync(vida.Token).ConfigureAwait(false); }
                catch { limite.Release(); throw; }
                requisicoes.RemoveAll(t => t.IsCompletedSuccessfully);
                requisicoes.Add(AtenderAsync(cliente));
            }
        }
        catch (OperationCanceledException) when (vida.IsCancellationRequested) { }
        catch (SocketException) when (vida.IsCancellationRequested) { }
    }

    private async Task AtenderAsync(TcpClient cliente)
    {
        using (cliente)
        using (var prazo = CancellationTokenSource.CreateLinkedTokenSource(vida.Token))
        {
            try
            {
                prazo.CancelAfter(TimeSpan.FromSeconds(5));
                var rede = cliente.GetStream();
                var cabecalho = new List<byte>();
                var caractere = new byte[1];
                while (cabecalho.Count < 8192)
                {
                    if (await rede.ReadAsync(caractere, prazo.Token).ConfigureAwait(false) == 0) return;
                    cabecalho.Add(caractere[0]);
                    if (cabecalho.Count >= 4 && cabecalho[^4] == 13 && cabecalho[^3] == 10 && cabecalho[^2] == 13 && cabecalho[^1] == 10) break;
                }
                if (cabecalho.Count >= 8192) return;
                var linhas = Encoding.ASCII.GetString(cabecalho.ToArray()).Split("\r\n");
                var pedido = linhas[0].Split(' ');
                if (pedido.Length != 3 || pedido[0] is not ("GET" or "HEAD")) return;
                (string Caminho, Task Pronto) arquivo;
                lock (arquivos) { if (!arquivos.TryGetValue(pedido[1], out arquivo)) return; }
                prazo.CancelAfter(Timeout.InfiniteTimeSpan);
                await arquivo.Pronto.WaitAsync(vida.Token).ConfigureAwait(false);
                await using var entrada = new FileStream(arquivo.Caminho, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
                var inicio = 0L;
                var fim = entrada.Length - 1;
                var faixa = linhas.Skip(1).FirstOrDefault(l => l.StartsWith("Range:", StringComparison.OrdinalIgnoreCase));
                if (faixa is not null)
                {
                    var valor = faixa[(faixa.IndexOf(':') + 1)..].Trim();
                    if (!valor.StartsWith("bytes=", StringComparison.Ordinal) || valor.Contains(',')) return;
                    var partes = valor[6..].Split('-');
                    if (partes.Length != 2 || !long.TryParse(partes[0], NumberStyles.None, CultureInfo.InvariantCulture, out inicio)) return;
                    if (partes[1].Length > 0 && !long.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out fim)) return;
                    fim = Math.Min(fim, entrada.Length - 1);
                    if (inicio > fim) return;
                }
                var tamanho = fim - inicio + 1;
                var resposta = "HTTP/1.1 " + (faixa is null ? "200 OK" : "206 Partial Content") + "\r\nConnection: close\r\nAccept-Ranges: bytes\r\nContent-Type: application/octet-stream\r\nContent-Length: " + tamanho.ToString(CultureInfo.InvariantCulture) + "\r\n";
                if (faixa is not null) resposta += $"Content-Range: bytes {inicio.ToString(CultureInfo.InvariantCulture)}-{fim.ToString(CultureInfo.InvariantCulture)}/{entrada.Length.ToString(CultureInfo.InvariantCulture)}\r\n";
                await rede.WriteAsync(Encoding.ASCII.GetBytes(resposta + "\r\n"), vida.Token).ConfigureAwait(false);
                if (pedido[0] == "HEAD") return;
                entrada.Position = inicio;
                var buffer = new byte[65536];
                while (tamanho > 0)
                {
                    var lidos = await entrada.ReadAsync(buffer.AsMemory(0, (int)Math.Min(tamanho, buffer.Length)), vida.Token).ConfigureAwait(false);
                    if (lidos == 0) throw new EndOfStreamException("O bloco de reprodução foi alterado durante a leitura.");
                    await rede.WriteAsync(buffer.AsMemory(0, lidos), vida.Token).ConfigureAwait(false);
                    tamanho -= lidos;
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (SocketException) { }
            finally { limite.Release(); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        vida.Cancel(); servidor.Stop();
        await aceitacao.ConfigureAwait(false);
        await Task.WhenAll(requisicoes).ConfigureAwait(false);
        vida.Dispose(); limite.Dispose();
    }
}
