using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;

namespace SonarFrame.ProvaReproducao;

internal sealed class JanelaProva : Window
{
    private readonly string pasta;
    private readonly FerramentasMidia ferramentas;
    private readonly CancellationTokenSource cancelamento = new();
    private readonly LibVLC biblioteca;
    private readonly LibVLCSharp.Shared.MediaPlayer reprodutor;
    private readonly VideoView video = new() { Focusable = false };
    private readonly Image foto = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
    private readonly TextBlock situacao = new() { Text = "Preparando amostras…", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock posicao = new() { Text = "Posição: 00:00" };
    private readonly ComboBox midias = new() { MinWidth = 180, Margin = new Thickness(4) };
    private readonly TextBox destino = new() { Text = "3", Width = 80, Margin = new Thickness(4) };
    private readonly StackPanel comandos = new() { Orientation = Orientation.Horizontal, IsEnabled = false };
    private readonly List<Control> controlesTemporais = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly List<object> eventos = [];
    private readonly Stopwatch relogio = Stopwatch.StartNew();
    private readonly bool automatico;
    private Task trabalho = Task.CompletedTask;
    private bool encerrando;
    private bool liberado;
    private bool imagemAtiva;
    private long geracao;
    private readonly double inicializacaoMs;

    public JanelaProva(string pasta, string ffmpeg, string ffprobe, bool automatico, bool benchmark)
    {
        this.pasta = pasta; this.automatico = automatico;
        ferramentas = new(ffmpeg, ffprobe, pasta);
        Title = "sonarFrame — Prova técnica de reprodução"; Width = 1000; Height = 620;
        var inicializacao = Stopwatch.StartNew();
        biblioteca = new LibVLC("--no-video-title-show", "--no-spu", "--no-osd", "--avcodec-hw=none");
        inicializacaoMs = inicializacao.Elapsed.TotalMilliseconds;
        reprodutor = new LibVLCSharp.Shared.MediaPlayer(biblioteca) { EnableKeyInput = false, EnableMouseInput = false, EnableHardwareDecoding = false };
        if (automatico) { reprodutor.Volume = 0; ShowInTaskbar = false; ShowActivated = false; Left = -10000; Top = -10000; IsHitTestVisible = false; }
        video.MediaPlayer = reprodutor;
        var painel = new DockPanel { Margin = new Thickness(16) };
        var inferior = new StackPanel(); DockPanel.SetDock(inferior, Dock.Bottom);
        inferior.Children.Add(comandos); inferior.Children.Add(posicao); inferior.Children.Add(situacao);
        inferior.Children.Add(new TextBlock { Text = "Laboratório isolado. Busca não garante quadro exato. Tab percorre controles; Alt+4 leva a Reproduzir/Pausar ou Abrir para imagem estática.", TextWrapping = TextWrapping.Wrap });
        painel.Children.Add(inferior);
        foreach (var arquivo in new[] { "origem.mp4", "origem.mov", "camada.wav", "foto.png", "previa.mp4" }) midias.Items.Add(arquivo);
        midias.SelectedIndex = 0;
        AutomationProperties.SetName(midias, "Mídia de teste"); comandos.Children.Add(midias);
        var abrir = Botao("Abrir", AbrirAsync);
        var alternar = Botao("Reproduzir/Pausar", AlternarAsync);
        controlesTemporais.Add(alternar);
        controlesTemporais.Add(Botao("Parar", async () => { await PararAsync(); Anunciar("Parado."); }));
        AutomationProperties.SetName(destino, "Destino da busca, em segundos"); comandos.Children.Add(destino);
        controlesTemporais.Add(destino);
        controlesTemporais.Add(Botao("Buscar", async () =>
        {
            if (!double.TryParse(destino.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var segundos) || !double.IsFinite(segundos) || segundos < 0)
                throw new InvalidOperationException("Informe segundos válidos.");
            if (imagemAtiva || reprodutor.Length <= 0 || segundos * 1000 >= reprodutor.Length) throw new InvalidOperationException("Escolha um destino dentro da mídia carregada.");
            reprodutor.SetPause(true); await EsperarAsync(() => reprodutor.State == VLCState.Paused, cancelamento.Token);
            reprodutor.Time = (long)(segundos * 1000); Anunciar("Busca solicitada; reprodução pausada.");
        }));
        controlesTemporais.Add(Botao("Consultar posição", () => { Anunciar(posicao.Text); return Task.CompletedTask; }));
        var cancelar = new Button { Content = "Cancelar preparação", Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left };
        cancelar.Click += (_, _) => cancelamento.Cancel(); inferior.Children.Add(cancelar);
        AutomationProperties.SetLiveSetting(situacao, AutomationLiveSetting.Polite);
        var area = new Grid(); area.Children.Add(video); area.Children.Add(foto); painel.Children.Add(area); Content = painel;
        PreviewKeyDown += (_, e) =>
        {
            if (e.SystemKey == Key.D4 && Keyboard.Modifiers == ModifierKeys.Alt) { (imagemAtiva ? abrir : alternar).Focus(); e.Handled = true; }
        };
        reprodutor.Playing += AoReproduzir; reprodutor.Paused += AoPausar;
        reprodutor.EndReached += AoConcluir; reprodutor.EncounteredError += AoFalhar;
        timer.Tick += (_, _) => { if (!liberado) posicao.Text = imagemAtiva ? "Imagem estática" : $"Posição: {Math.Max(0, reprodutor.Time) / 1000.0:0.000} de {Math.Max(0, reprodutor.Length) / 1000.0:0.000} segundos"; };
        timer.Start();
        Loaded += (_, _) => trabalho = IniciarAsync(benchmark, cancelar);
        Closing += AoFechar;
    }

    private Button Botao(string nome, Func<Task> acao)
    {
        var botao = new Button { Content = nome, Margin = new Thickness(4), Padding = new Thickness(6) };
        botao.Click += (_, _) => trabalho = ExecutarAcaoAsync(acao);
        comandos.Children.Add(botao); return botao;
    }

    private async Task ExecutarAcaoAsync(Func<Task> acao)
    {
        var foco = Keyboard.FocusedElement;
        comandos.IsEnabled = false;
        try { await acao(); }
        catch (OperationCanceledException) { Anunciar("Operação cancelada."); }
        catch (Exception erro) { Anunciar(erro.Message); }
        finally
        {
            if (!encerrando) { comandos.IsEnabled = true; foco?.Focus(); }
        }
    }

    private void Registrar(string estado)
    {
        var versao = Interlocked.Read(ref geracao);
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (encerrando || versao != geracao) return;
            eventos.Add(new { estado, decorridoMs = relogio.Elapsed.TotalMilliseconds });
            Anunciar(estado);
        });
    }
    private void AoReproduzir(object? sender, EventArgs e) => Registrar("Reproduzindo.");
    private void AoPausar(object? sender, EventArgs e) => Registrar("Pausado.");
    private void AoConcluir(object? sender, EventArgs e) => Registrar("Concluído.");
    private void AoFalhar(object? sender, EventArgs e) => Registrar("Falha ao reproduzir a mídia.");
    private void Anunciar(string mensagem)
    {
        situacao.Text = mensagem;
        (UIElementAutomationPeer.FromElement(situacao) ?? UIElementAutomationPeer.CreatePeerForElement(situacao))?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private async Task PararAsync()
    {
        Interlocked.Increment(ref geracao);
        await Task.Run(reprodutor.Stop);
    }

    private async Task TocarAsync(string caminho, CancellationToken token)
    {
        await PararAsync(); token.ThrowIfCancellationRequested();
        using var midia = new Media(biblioteca, caminho, FromType.FromPath);
        FerramentasMidia.Exigir(reprodutor.Play(midia), "O motor rejeitou a reprodução.");
        await EsperarAsync(() => reprodutor.State == VLCState.Playing || reprodutor.State == VLCState.Error, token);
        FerramentasMidia.Exigir(reprodutor.State != VLCState.Error, "Falha de decodificação.");
    }

    private async Task AbrirAsync()
    {
        await PararAsync();
        var nome = (string)midias.SelectedItem;
        imagemAtiva = nome.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        foreach (var controle in controlesTemporais) controle.IsEnabled = !imagemAtiva;
        foto.Visibility = imagemAtiva ? Visibility.Visible : Visibility.Collapsed;
        video.Visibility = imagemAtiva ? Visibility.Collapsed : Visibility.Visible;
        if (imagemAtiva)
        {
            foto.Source = CarregarImagem(ferramentas.Arquivo(nome)); posicao.Text = "Imagem estática";
            Anunciar("Imagem estática aberta. Controles de reprodução e busca por tempo indisponíveis.");
        }
        else await TocarAsync(ferramentas.Arquivo(nome), cancelamento.Token);
    }

    private Task AlternarAsync()
    {
        if (imagemAtiva) { Anunciar("Imagem estática, sem reprodução temporal."); return Task.CompletedTask; }
        if (reprodutor.State is VLCState.Stopped or VLCState.Ended or VLCState.NothingSpecial) return AbrirAsync();
        reprodutor.SetPause(reprodutor.IsPlaying); return Task.CompletedTask;
    }

    private async Task IniciarAsync(bool benchmark, Button cancelar)
    {
        var resultado = new Dictionary<string, object?>
        {
            ["inicioUtc"] = DateTimeOffset.UtcNow, ["sistema"] = Environment.OSVersion.ToString(),
            ["processadoresLogicos"] = Environment.ProcessorCount, ["processo64Bits"] = Environment.Is64BitProcess,
            ["processador"] = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            ["inicializacaoMotorMs"] = inicializacaoMs, ["decodificacao"] = "software; aceleração de hardware não validada",
            ["libvlc"] = biblioteca.Version, ["nvda"] = "Pendente de validação manual", ["audioFisico"] = "Pendente de escuta manual; automação usa volume zero"
        };
        var codigo = 0;
        try
        {
            resultado["preparacao"] = await ferramentas.PrepararAsync(cancelamento.Token);
            resultado["composicao"] = await ferramentas.VerificarAsync(cancelamento.Token);
            resultado["cancelamentoFfmpeg"] = await ferramentas.VerificarCancelamentoAsync(cancelamento.Token);
            if (automatico)
            {
                resultado["reproducao"] = await MedirReproducaoAsync(cancelamento.Token);
                if (benchmark)
                {
                    var medicoes = new List<object>();
                    foreach (var duracao in new[] { 60, 600, 1800 })
                    {
                        medicoes.Add(await ferramentas.MedirPreparacaoAsync(duracao, cancelamento.Token));
                        await File.WriteAllTextAsync(Path.Combine(pasta, "benchmark.json"), JsonSerializer.Serialize(medicoes, new JsonSerializerOptions { WriteIndented = true }));
                    }
                    resultado["benchmarkSintetico"] = medicoes;
                }
                resultado["resultado"] = "Verificações automatizadas aprovadas; validações manuais pendentes";
            }
            else { comandos.IsEnabled = true; midias.Focus(); Anunciar("Amostras prontas. Selecione uma mídia e acione Abrir."); }
        }
        catch (OperationCanceledException) { resultado["resultado"] = "Cancelado"; codigo = 2; Anunciar("Preparação cancelada. Feche e reabra para repetir."); }
        catch (Exception erro) { resultado["erro"] = erro.ToString(); codigo = 1; Anunciar(erro.Message); }
        finally
        {
            cancelar.IsEnabled = false;
            if (automatico)
            {
                await LiberarAsync(); resultado["descarteConcluido"] = true;
                resultado["eventos"] = eventos;
                await File.WriteAllTextAsync(Path.Combine(pasta, "resultado.json"), JsonSerializer.Serialize(resultado, new JsonSerializerOptions { WriteIndented = true }));
                System.Windows.Application.Current.Shutdown(codigo);
            }
        }
    }

    private async Task<object> MedirReproducaoAsync(CancellationToken token)
    {
        var medicoes = new List<object>();
        foreach (var nome in new[] { "origem.mp4", "origem.mov", "camada.wav", "previa.mp4" })
        {
            var medidor = Stopwatch.StartNew();
            await TocarAsync(ferramentas.Arquivo(nome), token);
            var inicio = medidor.Elapsed.TotalMilliseconds;
            await EsperarAsync(() => reprodutor.Time >= 400, token);
            reprodutor.SetPause(true); await EsperarAsync(() => reprodutor.State == VLCState.Paused, token);
            var antes = reprodutor.Time; await Task.Delay(250, token);
            FerramentasMidia.Exigir(Math.Abs(reprodutor.Time - antes) <= 40, "Relógio continuou avançando durante pausa.");
            medidor.Restart(); reprodutor.Time = 3000;
            await EsperarAsync(() => Math.Abs(reprodutor.Time - 3000) <= 100, token);
            var busca = medidor.Elapsed.TotalMilliseconds;
            medicoes.Add(new { midia = nome, inicioAteEstadoPlayingMs = inicio, buscaAteRelogioMs = busca, destinoMs = 3000, relogioObtidoMs = reprodutor.Time, precisaoVisual = "não inferida do relógio" });
            if (nome == "previa.mp4")
            {
                var capturas = new List<object>();
                foreach (var (destinoMs, canal) in new[] { (1000L, 0), (3000L, 2), (4500L, 1) })
                {
                    medidor.Restart(); reprodutor.Time = destinoMs;
                    var caminho = ferramentas.Arquivo($"busca-{destinoMs}.png");
                    var corresponde = false;
                    while (medidor.Elapsed < TimeSpan.FromSeconds(5) && !corresponde)
                    {
                        token.ThrowIfCancellationRequested();
                        if (File.Exists(caminho)) File.Delete(caminho);
                        if (reprodutor.TakeSnapshot(0, caminho, 160, 90))
                        {
                            await EsperarAsync(() => File.Exists(caminho), token);
                            try
                            {
                                var bitmap = new FormatConvertedBitmap(CarregarImagem(caminho), PixelFormats.Rgb24, null, 0);
                                var pixel = new byte[3]; bitmap.CopyPixels(new Int32Rect(80, 45, 1, 1), pixel, 3, 0);
                                corresponde = pixel[canal] > 70 && pixel[canal] > pixel[(canal + 1) % 3] + 40;
                            }
                            catch (IOException) { }
                        }
                        if (!corresponde) await Task.Delay(100, token);
                    }
                    FerramentasMidia.Exigir(corresponde, $"Busca pausada para {destinoMs} ms não apresentou a cor esperada.");
                    capturas.Add(new { destinoMs, ateCorEsperadaMs = medidor.Elapsed.TotalMilliseconds, limite = "cor de região temporal; não prova quadro exato" });
                }
                medicoes.Add(new { capturas });
                reprodutor.SetPause(false);
                await EsperarAsync(() => reprodutor.State == VLCState.Ended, token);
            }
            await PararAsync();
            using var arquivo = new FileStream(ferramentas.Arquivo(nome), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        var imagem = CarregarImagem(ferramentas.Arquivo("foto.png"));
        FerramentasMidia.Exigir(imagem.PixelWidth == 640, "Foto não decodificada por WPF.");
        comandos.IsEnabled = true;
        midias.SelectedItem = "foto.png";
        await AbrirAsync();
        FerramentasMidia.Exigir(controlesTemporais.All(c => !c.IsEnabled) && midias.IsEnabled && foto.Visibility == Visibility.Visible,
            "Imagem estática deve desabilitar controles temporais e preservar a escolha de mídia.");
        midias.SelectedItem = "origem.mov";
        await AbrirAsync();
        FerramentasMidia.Exigir(controlesTemporais.All(c => c.IsEnabled) && video.Visibility == Visibility.Visible,
            "Abrir vídeo após imagem deve reabilitar controles temporais.");
        await PararAsync();
        comandos.IsEnabled = false;
        using (var inexistente = new Media(biblioteca, ferramentas.Arquivo("ausente.mp4"), FromType.FromPath))
        {
            var falhou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void AoErro(object? sender, EventArgs e) => falhou.TrySetResult();
            reprodutor.EncounteredError += AoErro;
            try { reprodutor.Play(inexistente); await falhou.Task.WaitAsync(TimeSpan.FromSeconds(10), token); }
            finally { reprodutor.EncounteredError -= AoErro; }
        }
        await PararAsync();
        for (var i = 0; i < 3; i++)
        {
            using var jogador = new LibVLCSharp.Shared.MediaPlayer(biblioteca);
            using var midia = new Media(biblioteca, ferramentas.Arquivo("camada.wav"), FromType.FromPath);
            jogador.Volume = 0; jogador.Play(midia);
            await EsperarAsync(() => jogador.State == VLCState.Playing, token);
            await Task.Run(jogador.Stop, token);
        }
        return new { medicoes, pausaEstavel = true, termino = true, erroMidiaAusente = true, ciclosDescarte = 3, arquivosLiberados = true, fotoWpf = true, controlesTemporaisRespeitamTipoMidia = true };
    }

    private static BitmapImage CarregarImagem(string caminho)
    {
        using var arquivo = File.OpenRead(caminho);
        var imagem = new BitmapImage(); imagem.BeginInit(); imagem.CacheOption = BitmapCacheOption.OnLoad;
        imagem.StreamSource = arquivo; imagem.EndInit(); imagem.Freeze(); return imagem;
    }

    private static async Task EsperarAsync(Func<bool> condicao, CancellationToken token)
    {
        var prazo = Stopwatch.StartNew();
        while (!condicao())
        {
            if (prazo.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("O motor não confirmou a operação em dez segundos.");
            await Task.Delay(20, token);
        }
    }

    private async Task LiberarAsync()
    {
        if (liberado) return;
        encerrando = true; timer.Stop(); video.MediaPlayer = null;
        reprodutor.Playing -= AoReproduzir; reprodutor.Paused -= AoPausar;
        reprodutor.EndReached -= AoConcluir; reprodutor.EncounteredError -= AoFalhar;
        await Task.Run(() => { reprodutor.Stop(); reprodutor.Dispose(); biblioteca.Dispose(); });
        video.Dispose(); liberado = true;
    }

    private async void AoFechar(object? sender, CancelEventArgs e)
    {
        if (liberado) return;
        e.Cancel = true;
        if (encerrando) return;
        encerrando = true; cancelamento.Cancel();
        await trabalho; await LiberarAsync(); Close();
    }
}
