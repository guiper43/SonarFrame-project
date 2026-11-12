using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class TestesExportacaoDesktop
{
    [Fact]
    public Task ExportacaoPermiteSalvamentoAutomaticoEManualSemMoverFoco()
    {
        var relogio = new RelogioManual();
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var exportador = new ExportadorControlado();
            janela.ConfigurarExportacao(exportador);
            janela.ExibirProjeto(Amostra(Path.GetDirectoryName(caminho)!), caminho: caminho);
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            var exportacao = janela.ExecutarExportacaoAsync(new(Path.Combine(janela.PastaDownloadsExportacao, "test.mp4"), Orientacao.Horizontal, ModoEnquadramento.Letterbox));
            try
            {
                await exportador.Iniciada.Task;
                var foco = FocusManager.GetFocusedElement(janela);
                relogio.Avancar(119);
                await janela.SalvarAutomaticamenteAsync();
                Assert.False(File.Exists(caminho));
                relogio.Avancar(1);
                await janela.SalvarAutomaticamenteAsync();
                var repositorio = new RepositorioProjetosSqlite();
                Assert.Equal(janela.SessaoAtual!.Atual.Id, (await repositorio.CarregarAsync(caminho)).Id);
                Assert.True(File.Exists(caminho + ".recovery"));
                Assert.False(janela.SessaoAtual.TemAlteracoes);
                Assert.Same(foco, FocusManager.GetFocusedElement(janela));
                Assert.False(exportacao.IsCompleted);
                Assert.False(janela.RegiaoLinhaDoTempo.IsEnabled);
                Assert.True(janela.BotaoSalvar.IsEnabled);

                janela.SessaoAtual.Posicionar(TempoMidia.Segundos(1));
                Assert.True(TecladoTeste.Pressionar(janela.BotaoProgressoExportacao, Key.S, ModifierKeys.Control));
                using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (janela.SessaoAtual.TemAlteracoes) await Task.Delay(10, prazo.Token);
                Assert.Equal(TempoMidia.Segundos(1), (await repositorio.CarregarAsync(caminho)).Cursor);
                Assert.Same(foco, FocusManager.GetFocusedElement(janela));
                Assert.False(exportacao.IsCompleted);
            }
            finally
            {
                janela.BotaoCancelarExportacao.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await exportacao;
            }
        }, relogio: relogio);
    }

    private sealed class RelogioManual : TimeProvider
    {
        private DateTimeOffset agora = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => agora;
        public void Avancar(int segundos) => agora = agora.AddSeconds(segundos);
    }

    [Fact]
    public Task DialogoApresentaMp41080pSemOrientacaoPreSelecionadaENavegaPorTeclado() =>
        TestesIntegracaoEditor.ComJanela((janela, caminho) =>
        {
            var pasta = Path.GetDirectoryName(caminho)!;
            janela.ExibirProjeto(Amostra(pasta), salvo: true, caminho: caminho);
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();

            var config = janela.CriarDialogoExportacao();
            config.Dialogo.WindowStartupLocation = WindowStartupLocation.Manual;
            config.Dialogo.Left = -10000; config.Dialogo.Top = -10000;
            config.Dialogo.ShowActivated = false; config.Dialogo.ShowInTaskbar = false;
            try
            {
                config.Dialogo.Show();
                Assert.Equal("Exportar vídeo (MP4)", config.Dialogo.Title);
                Assert.Equal("Orientação (escolha obrigatória)", AutomationProperties.GetName(config.Orientacao));
                Assert.Equal(0, config.Resolucao.SelectedIndex);
                Assert.Equal("1080p", config.Resolucao.SelectedItem);
                Assert.Equal(-1, config.Orientacao.SelectedIndex);
                Assert.Null(config.Orientacao.SelectedItem);
                Assert.Equal(0, config.Enquadramento.SelectedIndex);
                Assert.Equal("Permitir bordas", config.Enquadramento.SelectedItem);
                Assert.Equal(0, config.DestinoEnvio.SelectedIndex);
                Assert.Equal("Pasta Downloads (padrão)", config.DestinoEnvio.SelectedItem);
                Assert.Equal(janela.PastaDownloadsExportacao, config.PastaDestino.Text);
                Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(config.Erro));

                config.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(config.ObterResultado());
                Assert.Contains("orientação", config.Erro.Text, StringComparison.OrdinalIgnoreCase);
                Assert.Same(config.Orientacao, FocusManager.GetFocusedElement(config.Dialogo));

                Pressionar(config.Orientacao, Key.Down);
                Assert.Equal(0, config.Orientacao.SelectedIndex);
                Pressionar(config.Orientacao, Key.Down);
                Assert.Equal(1, config.Orientacao.SelectedIndex);
                Assert.Equal("9:16 (vertical)", config.Orientacao.SelectedItem);

                var itemOrientacao = (ListBoxItem)config.Orientacao.ItemContainerGenerator.ContainerFromIndex(1);
                Assert.True(itemOrientacao.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                Assert.Same(
                    config.Enquadramento.ItemContainerGenerator.ContainerFromIndex(0),
                    FocusManager.GetFocusedElement(config.Dialogo));
                var itemEnquadramento = (ListBoxItem)config.Enquadramento.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.True(itemEnquadramento.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)));
                Assert.Same(itemOrientacao, FocusManager.GetFocusedElement(config.Dialogo));

                Pressionar(config.Resolucao, Key.Down);
                Assert.Equal(1, config.Resolucao.SelectedIndex);
                Assert.Equal("720p", config.Resolucao.SelectedItem);

                Pressionar(config.Enquadramento, Key.Right);
                Assert.Equal(1, config.Enquadramento.SelectedIndex);
                Assert.Equal("Preencher a tela", config.Enquadramento.SelectedItem);

                config.NomeArquivo.Text = "CON";
                config.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(config.ObterResultado());
                Assert.Contains("nome de arquivo válido", config.Erro.Text, StringComparison.OrdinalIgnoreCase);

                config.NomeArquivo.Text = ".mp4";
                config.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(config.ObterResultado());
                Assert.Contains("nome de arquivo válido", config.Erro.Text, StringComparison.OrdinalIgnoreCase);

                config.NomeArquivo.Text = "video..mp4";
                config.PastaDestino.Text = pasta;
                Assert.Equal(1, config.DestinoEnvio.SelectedIndex);
                config.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(config.ObterResultado());
                Assert.Contains("misturar", config.Erro.Text, StringComparison.OrdinalIgnoreCase);

                var pastaPersonalizada = Path.Combine(pasta, "videos-exportados");
                Directory.CreateDirectory(pastaPersonalizada);
                Pressionar(config.DestinoEnvio, Key.Up);
                Assert.Equal(0, config.DestinoEnvio.SelectedIndex);
                Assert.Equal(janela.PastaDownloadsExportacao, config.PastaDestino.Text);
                Pressionar(config.DestinoEnvio, Key.Down);
                Assert.Equal(1, config.DestinoEnvio.SelectedIndex);
                Assert.Equal("Outra pasta selecionada", config.DestinoEnvio.SelectedItem);
                config.NomeArquivo.Text = "video-final";
                config.PastaDestino.Text = pastaPersonalizada;
                config.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var resultado = config.ObterResultado();
                Assert.NotNull(resultado);
                Assert.Equal(Path.Combine(pastaPersonalizada, "video-final.mp4"), resultado.Destino);
                Assert.Equal(Orientacao.Vertical, resultado.Orientacao);
                Assert.Equal(ModoEnquadramento.Preencher, resultado.Enquadramento);
                Assert.Equal(720, resultado.Altura);
                Assert.Equal(pastaPersonalizada, janela.RepositorioConfiguracoes.Carregar().DiretorioExportacoes);

                var segundoDialogo = janela.CriarDialogoExportacao();
                segundoDialogo.Dialogo.WindowStartupLocation = WindowStartupLocation.Manual;
                segundoDialogo.Dialogo.Left = -10000; segundoDialogo.Dialogo.Top = -10000;
                segundoDialogo.Dialogo.ShowActivated = false; segundoDialogo.Dialogo.ShowInTaskbar = false;
                try
                {
                    segundoDialogo.Dialogo.Show();
                    Assert.Equal(1, segundoDialogo.DestinoEnvio.SelectedIndex);
                    Assert.Equal("Outra pasta selecionada", segundoDialogo.DestinoEnvio.SelectedItem);
                    Assert.Equal(pastaPersonalizada, segundoDialogo.PastaDestino.Text);
                    Pressionar(segundoDialogo.Orientacao, Key.Down);
                    Pressionar(segundoDialogo.DestinoEnvio, Key.Up);
                    Assert.Equal(0, segundoDialogo.DestinoEnvio.SelectedIndex);
                    Assert.Equal(janela.PastaDownloadsExportacao, segundoDialogo.PastaDestino.Text);
                    segundoDialogo.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(janela.PastaDownloadsExportacao, janela.RepositorioConfiguracoes.Carregar().DiretorioExportacoes);
                }
                finally { if (segundoDialogo.Dialogo.IsVisible) segundoDialogo.Dialogo.Close(); }
            }
            finally { if (config.Dialogo.IsVisible) config.Dialogo.Close(); }
            return Task.CompletedTask;
        });

    [Fact]
    public Task ExportacaoAtualizaProgressoBloqueiaEdicaoEOfereceAbrirVideoEPasta() =>
        TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var pasta = Path.GetDirectoryName(caminho)!;
            var pastaSaida = janela.PastaDownloadsExportacao;
            var destino = Path.Combine(pastaSaida, "exportado.mp4");
            var exportador = new ExportadorControlado();
            janela.ConfigurarExportacao(exportador);
            janela.ExibirProjeto(Amostra(pasta), salvo: true, caminho: caminho);
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();

            janela.SolicitarOpcoesExportacaoOverride = () =>
                new OpcoesExportacao(destino, Orientacao.Horizontal, ModoEnquadramento.Letterbox, 1080);

            janela.BotaoExportar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await exportador.Iniciada.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(janela.RegiaoBiblioteca.IsEnabled);
            Assert.False(janela.RegiaoPropriedades.IsEnabled);
            Assert.False(janela.RegiaoLinhaDoTempo.IsEnabled);
            Assert.False(janela.RegiaoPrevia.IsEnabled);
            Assert.True(janela.RegiaoComandos.IsEnabled);
            Assert.Equal(Visibility.Visible, janela.BotaoProgressoExportacao.Visibility);
            Assert.Equal(Visibility.Visible, janela.BotaoCancelarExportacao.Visibility);
            janela.BotaoSalvar.Focus();
            Assert.True(TecladoTeste.Pressionar(janela.BotaoSalvar, Key.Tab, ModifierKeys.Shift));
            Assert.Same(janela.BotaoSalvar, FocusManager.GetFocusedElement(janela));

            exportador.Reportar(new ProgressoOperacao("Exportando vídeo", 42, TimeSpan.FromSeconds(15)));
            await janela.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Assert.Contains("42%", janela.EstadoExportacao.Text);
            Assert.Contains("15 segundos", janela.EstadoExportacao.Text);

            janela.BotaoProgressoExportacao.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("42%", janela.Situacao.Text);

            await File.WriteAllTextAsync(destino, "mp4");
            exportador.Concluir();
            await janela.UltimaExportacao.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(janela.RegiaoBiblioteca.IsEnabled);
            Assert.True(janela.RegiaoLinhaDoTempo.IsEnabled);
            Assert.Equal(Visibility.Collapsed, janela.BotaoProgressoExportacao.Visibility);
            Assert.Equal(Visibility.Visible, janela.BotaoAbrirVideoExportado.Visibility);
            Assert.Equal(Visibility.Visible, janela.BotaoAbrirPastaExportada.Visibility);
            Assert.Same(janela.BotaoAbrirVideoExportado, FocusManager.GetFocusedElement(janela));

            var abertos = new List<(string Caminho, bool Pasta)>();
            janela.AbrirDestinoExportadoOverride = (alvo, ehPasta) => abertos.Add((alvo, ehPasta));
            janela.BotaoAbrirVideoExportado.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            janela.BotaoAbrirPastaExportada.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal([(destino, false), (pastaSaida, true)], abertos);
        }, exportador: null);

    [Fact]
    public Task CancelamentoEFalhaRegistramMensagensRecentesERestauramInterface() =>
        TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var pasta = Path.GetDirectoryName(caminho)!;
            var destino = Path.Combine(janela.PastaDownloadsExportacao, "falha.mp4");
            var exportador = new ExportadorControlado();
            janela.ConfigurarExportacao(exportador);
            janela.ExibirProjeto(Amostra(pasta), salvo: true, caminho: caminho);
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();

            var opcoes = new OpcoesExportacao(destino, Orientacao.Vertical, ModoEnquadramento.Preencher, 720);
            var tarefaCancelada = janela.ExecutarExportacaoAsync(opcoes);
            await exportador.Iniciada.Task.WaitAsync(TimeSpan.FromSeconds(5));
            janela.BotaoCancelarExportacao.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await tarefaCancelada.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(janela.RegiaoLinhaDoTempo.IsEnabled);
            Assert.Contains("Exportação cancelada", janela.Situacao.Text);

            var exportadorFalha = new ExportadorControlado();
            janela.ConfigurarExportacao(exportadorFalha);
            var tarefaFalha = janela.ExecutarExportacaoAsync(opcoes);
            await exportadorFalha.Iniciada.Task.WaitAsync(TimeSpan.FromSeconds(5));
            exportadorFalha.Falhar(new IOException("Disco cheio no destino."));
            await tarefaFalha.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(janela.RegiaoLinhaDoTempo.IsEnabled);
            Assert.Contains("Disco cheio no destino.", janela.Situacao.Text);
            Assert.Contains(janela.Mensagens.Items.Cast<string>(), m => m.Contains("Disco cheio no destino."));
        });

    [Fact]
    public Task DownloadsComProjetosUsaSubpastaSeguraECancelamentoDoDialogoRestauraFocoVisivel() =>
        TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var pasta = Path.GetDirectoryName(caminho)!;
            janela.ExibirProjeto(Amostra(pasta), salvo: true, caminho: caminho);
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();

            await File.WriteAllTextAsync(Path.Combine(janela.PastaDownloadsExportacao, "outro.sonarframe"), "sqlite");
            var subpastaSegura = Path.Combine(janela.PastaDownloadsExportacao, "sonarFrame-exportacoes");

            var config = janela.CriarDialogoExportacao();
            config.Dialogo.WindowStartupLocation = WindowStartupLocation.Manual;
            config.Dialogo.Left = -10000; config.Dialogo.Top = -10000;
            config.Dialogo.ShowActivated = false; config.Dialogo.ShowInTaskbar = false;
            try
            {
                config.Dialogo.Show();
                Assert.Equal(0, config.DestinoEnvio.SelectedIndex);
                Assert.Equal("Pasta Downloads (padrão)", config.DestinoEnvio.SelectedItem);
                Assert.Equal(subpastaSegura, config.PastaDestino.Text);

                Pressionar(config.Orientacao, Key.Down);
                config.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var resultado = config.ObterResultado();
                Assert.NotNull(resultado);
                Assert.Equal(subpastaSegura, Path.GetDirectoryName(resultado.Destino));
                Assert.Equal(subpastaSegura, janela.RepositorioConfiguracoes.Carregar().DiretorioExportacoes);
            }
            finally { if (config.Dialogo.IsVisible) config.Dialogo.Close(); }

            FocusManager.SetFocusedElement(janela, janela.MenuExportar);
            janela.SolicitarOpcoesExportacaoOverride = () => null;
            janela.MenuExportar.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await janela.UltimaExportacao;
            Assert.Same(janela.BotaoExportar, FocusManager.GetFocusedElement(janela));
        });

    private static Projeto Amostra(string pasta)
    {
        var sessao = new SessaoEdicao(Projeto.Criar("Projeto Exportação"));
        var midia = new Midia(Guid.NewGuid(), "Cena", Path.Combine(pasta, "cena.mp4"), TipoMidia.Video, TempoMidia.Segundos(6), new(30, 1));
        sessao.Executar(new RegistrarMidia(midia));
        sessao.Executar(new InserirClipe(midia.Id, 0));
        return sessao.Atual;
    }

    private static void Pressionar(UIElement origem, Key tecla)
    {
        var fonte = PresentationSource.FromVisual(origem) ?? PresentationSource.FromVisual(Window.GetWindow(origem));
        var evento = new KeyEventArgs(new TecladoTeste(), fonte!, Environment.TickCount, tecla)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        origem.RaiseEvent(evento);
        Assert.True(evento.Handled);
    }

    private sealed class ExportadorControlado : IExportador
    {
        private readonly TaskCompletionSource conclusao = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IProgress<ProgressoOperacao>? progressoAtual;
        public TaskCompletionSource Iniciada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ExportarAsync(Projeto snapshot, OpcoesExportacao opcoes, IProgress<ProgressoOperacao> progresso, CancellationToken cancellationToken = default)
        {
            progressoAtual = progresso;
            Iniciada.TrySetResult();
            using var registro = cancellationToken.Register(() => conclusao.TrySetCanceled(cancellationToken));
            await conclusao.Task;
        }

        public void Reportar(ProgressoOperacao progresso) => progressoAtual?.Report(progresso);
        public void Concluir() => conclusao.TrySetResult();
        public void Falhar(Exception erro) => conclusao.TrySetException(erro);
    }
}
