using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using SonarFrame.Application;
using SonarFrame.Core;

namespace SonarFrame.Desktop;

internal sealed class FabricaPreviaVlc : IFabricaReprodutorMidia
{
    private readonly Lazy<Task<LibVLC>> biblioteca = new(() => Task.Run(() => new LibVLC("--no-video-title-show", "--no-osd", "--no-spu")));
    private ContentControl? superficie;
    private int descartada;

    public FabricaPreviaVlc() { }

    internal FabricaPreviaVlc(ContentControl superficie) => this.superficie = superficie;

    internal void AssociarSuperficie(ContentControl destino) => superficie = destino;

    public async Task<IReprodutorMidia> CriarAsync(Midia midia, CancellationToken token)
    {
        var destino = superficie ?? throw new InvalidOperationException("Superfície de prévia não configurada.");
        if (!File.Exists(midia.Caminho)) throw new FileNotFoundException("Arquivo ausente. Use Localizar arquivo na biblioteca.");
        if (midia.Tipo == TipoMidia.Imagem) return new PreviaImagem(destino, midia.Caminho);
        LibVLC motor;
        try { motor = await biblioteca.Value.WaitAsync(token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception erro) { throw new InvalidOperationException("Não foi possível iniciar o motor de prévia. Verifique os arquivos da instalação e reinicie o aplicativo.", erro); }
        token.ThrowIfCancellationRequested();
        var player = await Task.Run(() => new LibVLCSharp.Shared.MediaPlayer(motor)
        { EnableKeyInput = false, EnableMouseInput = false, EnableHardwareDecoding = false });
        return new ReprodutorMidiaVlc(destino, motor, player, midia);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref descartada, 1) != 0 || !biblioteca.IsValueCreated) return;
        LibVLC motor;
        try { motor = await biblioteca.Value; }
        catch { return; }
        await Task.Run(motor.Dispose);
    }

    private sealed class PreviaImagem(ContentControl superficie, string caminho) : IReprodutorMidia
    {
        private Image? imagem;
        public event Action<EventoReprodutor>? Alterado { add { } remove { } }
        public TempoMidia Posicao => TempoMidia.Zero;
        public async Task CarregarAsync(CancellationToken token)
        {
            var bitmap = await Task.Run(() =>
            {
                using var arquivo = File.OpenRead(caminho);
                var resultado = new BitmapImage(); resultado.BeginInit(); resultado.CacheOption = BitmapCacheOption.OnLoad;
                resultado.StreamSource = arquivo; resultado.EndInit(); resultado.Freeze(); return resultado;
            }, token);
            token.ThrowIfCancellationRequested();
            await superficie.Dispatcher.InvokeAsync(() =>
            {
                imagem = new Image { Source = bitmap, Stretch = Stretch.Uniform, Focusable = false }; superficie.Content = imagem;
            }, System.Windows.Threading.DispatcherPriority.Normal, token);
        }
        public Task ReproduzirAsync(CancellationToken token) => Task.CompletedTask;
        public Task PausarAsync(CancellationToken token) => Task.CompletedTask;
        public Task PosicionarAsync(TempoMidia posicao, CancellationToken token) => Task.CompletedTask;
        public async ValueTask DisposeAsync() => await superficie.Dispatcher.InvokeAsync(() =>
        {
            if (ReferenceEquals(superficie.Content, imagem)) superficie.Content = null;
        });
    }

    private sealed class ReprodutorMidiaVlc : IReprodutorMidia
    {
        private readonly ContentControl superficie;
        private readonly LibVLC biblioteca;
        private readonly LibVLCSharp.Shared.MediaPlayer player;
        private readonly Midia midia;
        private VideoView? video;
        private Media? arquivo;
        private volatile bool falhou;
        private bool liberado;
        private long posicaoSolicitada = -1;
        public event Action<EventoReprodutor>? Alterado;
        public TempoMidia Posicao
        {
            get
            {
                if (liberado) return TempoMidia.Zero;
                var solicitada = Interlocked.Read(ref posicaoSolicitada);
                return new(solicitada >= 0 ? solicitada : Math.Max(0, player.Time), 1000);
            }
        }

        public ReprodutorMidiaVlc(ContentControl superficie, LibVLC biblioteca, LibVLCSharp.Shared.MediaPlayer player, Midia midia)
        {
            this.superficie = superficie; this.biblioteca = biblioteca; this.player = player; this.midia = midia;
            player.EndReached += AoConcluir; player.EncounteredError += AoFalhar;
            player.TimeChanged += AoAtualizarTempo;
        }
        private void AoAtualizarTempo(object? sender, MediaPlayerTimeChangedEventArgs e)
        {
            var destino = Interlocked.Read(ref posicaoSolicitada);
            if (destino >= 0 && e.Time >= destino && e.Time <= destino + 1000) Interlocked.CompareExchange(ref posicaoSolicitada, -1, destino);
        }
        private void AoConcluir(object? sender, EventArgs e) => Alterado?.Invoke(new(true));
        private void AoFalhar(object? sender, EventArgs e)
        {
            falhou = true; Alterado?.Invoke(new(false, "Não foi possível reproduzir esta mídia. Verifique o arquivo e o dispositivo de áudio."));
        }
        public async Task CarregarAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await superficie.Dispatcher.InvokeAsync(() =>
            {
                if (midia.Tipo == TipoMidia.Video)
                {
                    video = new VideoView { MediaPlayer = player, Focusable = false }; superficie.Content = video;
                }
                else superficie.Content = null;
            }, System.Windows.Threading.DispatcherPriority.Normal, token);
            arquivo = new Media(biblioteca, midia.Caminho, FromType.FromPath);
            if (midia.Tipo == TipoMidia.Audio) arquivo.AddOption(":no-video");
            if (!await Task.Run(() => player.Play(arquivo), token)) throw new InvalidOperationException("O motor rejeitou a mídia.");
            await EsperarAsync(() => player.State is VLCState.Playing or VLCState.Ended, token);
        }
        public Task ReproduzirAsync(CancellationToken token) => ReproduzirAsync(token, restaurarAudio: true);

        private async Task ReproduzirAsync(CancellationToken token, bool restaurarAudio)
        {
            if (player.State is VLCState.Ended or VLCState.Stopped)
            {
                if (!await Task.Run(() => player.Play(arquivo!), token)) throw new InvalidOperationException("Não foi possível reiniciar a mídia.");
            }
            else await Task.Run(() => player.SetPause(false), token);
            await EsperarAsync(() => player.State is VLCState.Playing or VLCState.Ended, token);
            if (restaurarAudio) player.Mute = false;
        }
        public async Task CarregarPausadoAsync(CancellationToken token)
        {
            player.Mute = true;
            try { await CarregarAsync(token); await PausarAsync(token); }
            finally { player.Mute = false; }
        }
        public async Task PausarAsync(CancellationToken token)
        {
            if (player.State is VLCState.Paused or VLCState.Ended or VLCState.Stopped) return;
            await Task.Run(() => player.SetPause(true), token);
            await EsperarAsync(() => player.State is VLCState.Paused or VLCState.Ended, token);
        }
        public async Task PosicionarAsync(TempoMidia posicao, CancellationToken token)
        {
            var mutado = player.Mute;
            try
            {
                if (player.State is VLCState.Ended or VLCState.Stopped) { player.Mute = true; await ReproduzirAsync(token, restaurarAudio: false); }
                await PausarAsync(token);
                if (!player.IsSeekable) throw new InvalidOperationException("Esta mídia não permite busca por tempo.");
                var destino = checked((long)((decimal)posicao.Numerador * 1000 / posicao.Denominador));
                Interlocked.Exchange(ref posicaoSolicitada, destino);
                player.Time = destino;
            }
            finally { player.Mute = mutado; }
        }
        private async Task EsperarAsync(Func<bool> condicao, CancellationToken token)
        {
            var prazo = Stopwatch.StartNew();
            while (!condicao())
            {
                if (falhou) throw new InvalidOperationException("Falha ao decodificar a mídia.");
                if (prazo.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("O motor de prévia não respondeu. Tente abrir a mídia novamente.");
                await Task.Delay(25, token);
            }
            if (falhou) throw new InvalidOperationException("Falha ao decodificar a mídia.");
        }
        public async ValueTask DisposeAsync()
        {
            if (liberado) return;
            liberado = true; player.EndReached -= AoConcluir; player.EncounteredError -= AoFalhar;
            player.TimeChanged -= AoAtualizarTempo;
            await Task.Run(player.Stop);
            await superficie.Dispatcher.InvokeAsync(() =>
            {
                if (video is not null) { video.MediaPlayer = null; video.Dispose(); }
                if (ReferenceEquals(superficie.Content, video)) superficie.Content = null;
            });
            await Task.Run(() => { player.Dispose(); arquivo?.Dispose(); });
        }
    }
}
