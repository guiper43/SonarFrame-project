namespace SonarFrame.Infrastructure;

internal sealed class ArquivoProgressivo : IAsyncDisposable
{
    private readonly string caminho;
    private readonly long limite;
    private readonly CancellationTokenSource cancelamento = new();
    private readonly object bloqueio = new();
    private TaskCompletionSource alterado = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly FileStream escrita;
    private long tamanho;
    private bool concluido;
    private Exception? falha;
    public string Caminho => caminho;

    public ArquivoProgressivo(string caminho, long limite)
    {
        this.caminho = caminho;
        this.limite = limite;
        escrita = new FileStream(caminho, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite, 65536, FileOptions.Asynchronous);
    }

    public async Task CopiarAsync(Stream origem, CancellationToken token)
    {
        try
        {
            var dados = new byte[65536];
            int quantidade;
            while ((quantidade = await origem.ReadAsync(dados, token).ConfigureAwait(false)) > 0)
            {
                if (tamanho + quantidade > limite) throw new IOException("A prévia excedeu o limite de cache de 2 GB.");
                await escrita.WriteAsync(dados.AsMemory(0, quantidade), token).ConfigureAwait(false);
                await escrita.FlushAsync(token).ConfigureAwait(false);
                lock (bloqueio)
                {
                    tamanho += quantidade;
                    Sinalizar();
                }
            }
        }
        catch (Exception erro) { Encerrar(erro); throw; }
    }

    public void Encerrar(Exception? erro = null)
    {
        lock (bloqueio) { falha ??= erro; concluido = true; Sinalizar(); }
    }

    private void Sinalizar()
    {
        var anterior = alterado;
        alterado = new(TaskCreationOptions.RunContinuationsAsynchronously);
        anterior.TrySetResult();
    }

    public Stream AbrirLeitura() => new Leitura(this);
    public void Cancelar() => cancelamento.Cancel();

    public async ValueTask DisposeAsync()
    {
        Cancelar();
        await escrita.DisposeAsync().ConfigureAwait(false);
        cancelamento.Dispose();
        File.Delete(caminho);
    }

    private sealed class Leitura : Stream
    {
        private readonly ArquivoProgressivo origem;
        private readonly FileStream arquivo;
        public Leitura(ArquivoProgressivo origem)
        {
            this.origem = origem;
            arquivo = new FileStream(origem.caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.RandomAccess);
        }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length { get { lock (origem.bloqueio) return origem.tamanho; } }
        public override long Position { get => arquivo.Position; set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0) return 0;
            while (true)
            {
                Task espera;
                long disponivel;
                lock (origem.bloqueio)
                {
                    origem.cancelamento.Token.ThrowIfCancellationRequested();
                    if (origem.falha is { } erro) throw new IOException("Falha ao preparar a reprodução.", erro);
                    disponivel = origem.tamanho - arquivo.Position;
                    if (disponivel <= 0 && origem.concluido) return 0;
                    espera = origem.alterado.Task;
                }
                if (disponivel > 0) return arquivo.Read(buffer, offset, (int)Math.Min(count, disponivel));
                espera.WaitAsync(origem.cancelamento.Token).GetAwaiter().GetResult();
            }
        }
        public override long Seek(long offset, SeekOrigin sentido)
        {
            var destino = sentido switch { SeekOrigin.Begin => offset, SeekOrigin.Current => checked(Position + offset), SeekOrigin.End => checked(Length + offset), _ => throw new ArgumentOutOfRangeException(nameof(sentido)) };
            if (destino < 0 || destino > Length) throw new IOException("A posição ainda não está disponível.");
            return arquivo.Seek(destino, SeekOrigin.Begin);
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) arquivo.Dispose(); base.Dispose(disposing); }
    }
}
