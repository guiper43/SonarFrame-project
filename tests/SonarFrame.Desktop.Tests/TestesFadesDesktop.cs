using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SonarFrame.Application;
using SonarFrame.Core;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class TestesFadesDesktop
{
    [Fact]
    public Task DialogoExibeCamposPorTipoValidaSomaEAplicaOuRemoveFadesPreservandoFoco() =>
        TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var sessao = new SessaoEdicao(Projeto.Criar("Fades Desktop"));
            var video = new Midia(Guid.NewGuid(), "Cena", "cena.mp4", TipoMidia.Video, TempoMidia.Segundos(10), new(30, 1));
            var audio = new Midia(Guid.NewGuid(), "Locução", "locucao.mp3", TipoMidia.Audio, TempoMidia.Segundos(6));
            var foto = new Midia(Guid.NewGuid(), "Capa", "capa.png", TipoMidia.Imagem, null);
            sessao.Executar(new RegistrarMidia(video));
            sessao.Executar(new RegistrarMidia(audio));
            sessao.Executar(new RegistrarMidia(foto));
            sessao.Executar(new InserirClipe(video.Id, 0));
            sessao.Executar(new InserirClipe(foto.Id, 1, TempoMidia.Segundos(4)));
            sessao.Executar(new AdicionarCamada(audio.Id, TempoMidia.Segundos(1), TempoMidia.Zero, TempoMidia.Segundos(6)));

            janela.ExibirProjeto(sessao.Atual, salvo: true, caminho: caminho);
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();

            var conteudos = new AcoesEdicao(janela.SessaoAtual!).Conteudos();
            var itemVideo = conteudos.Single(c => c.MidiaId == video.Id);
            var itemAudio = conteudos.Single(c => c.MidiaId == audio.Id);
            var itemFoto = conteudos.Single(c => c.MidiaId == foto.Id);

            var dialogoAudio = janela.CriarDialogoFades(itemAudio, TipoMidia.Audio);
            Assert.NotNull(dialogoAudio.FadeInAudio);
            Assert.NotNull(dialogoAudio.FadeOutAudio);
            Assert.Null(dialogoAudio.FadeInVideo);
            Assert.Null(dialogoAudio.FadeOutVideo);

            var dialogoFoto = janela.CriarDialogoFades(itemFoto, TipoMidia.Imagem);
            Assert.Null(dialogoFoto.FadeInAudio);
            Assert.Null(dialogoFoto.FadeOutAudio);
            Assert.NotNull(dialogoFoto.FadeInVideo);
            Assert.NotNull(dialogoFoto.FadeOutVideo);

            var dialogoVideo = janela.CriarDialogoFades(itemVideo, TipoMidia.Video);
            dialogoVideo.Dialogo.WindowStartupLocation = WindowStartupLocation.Manual;
            dialogoVideo.Dialogo.Left = -10000; dialogoVideo.Dialogo.Top = -10000;
            dialogoVideo.Dialogo.ShowActivated = false; dialogoVideo.Dialogo.ShowInTaskbar = false;
            try
            {
                dialogoVideo.Dialogo.Show();
                Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(dialogoVideo.Erro));
                dialogoVideo.FadeInAudio!.Text = "6";
                dialogoVideo.FadeOutAudio!.Text = "5";
                dialogoVideo.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(dialogoVideo.ObterResultado());
                Assert.Contains("soma de fade-in e fade-out de áudio", dialogoVideo.Erro.Text);
                Assert.Same(dialogoVideo.FadeOutAudio, System.Windows.Input.FocusManager.GetFocusedElement(dialogoVideo.Dialogo));

                dialogoVideo.FadeOutAudio.Text = "2";
                dialogoVideo.FadeInVideo!.Text = "7";
                dialogoVideo.FadeOutVideo!.Text = "4";
                dialogoVideo.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(dialogoVideo.ObterResultado());
                Assert.Contains("soma de fade-in e fade-out de vídeo", dialogoVideo.Erro.Text);
                Assert.Same(dialogoVideo.FadeOutVideo, System.Windows.Input.FocusManager.GetFocusedElement(dialogoVideo.Dialogo));

                dialogoVideo.FadeInAudio.Text = "1";
                dialogoVideo.FadeOutAudio.Text = "2";
                dialogoVideo.FadeInVideo.Text = "1";
                dialogoVideo.FadeOutVideo.Text = "1";
                dialogoVideo.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var obtido = dialogoVideo.ObterResultado();
                Assert.NotNull(obtido);
                Assert.Equal((TempoMidia.Segundos(1), TempoMidia.Segundos(2), TempoMidia.Segundos(1), TempoMidia.Segundos(1)), obtido!.Value);
            }
            finally { dialogoVideo.Dialogo.Close(); }

            janela.LinhaDoTempo.SelectedIndex = 0;
            Assert.True(janela.BotaoFades.IsEnabled);
            Assert.False(janela.BotaoRemoverFades.IsEnabled);
            Assert.Contains("Fades de áudio: nenhum", janela.Propriedades.Text);
            Assert.Contains("Fades de vídeo: nenhum", janela.Propriedades.Text);

            janela.SolicitarFadesOverride = (_, _) => (TempoMidia.Segundos(1), TempoMidia.Segundos(2), TempoMidia.Segundos(1), TempoMidia.Segundos(2));
            janela.BotaoFades.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await janela.UltimaEdicao;

            Assert.True(janela.BotaoRemoverFades.IsEnabled);
            Assert.True(janela.MenuRemoverFades.IsEnabled);
            Assert.Contains("Fades de áudio: fade-in 00:00:01.000, fade-out 00:00:02.000", janela.Propriedades.Text);
            Assert.Contains("Fades de vídeo: fade-in 00:00:01.000, fade-out 00:00:02.000", janela.Propriedades.Text);
            Assert.Contains("Fades atualizados em Cena", janela.Situacao.Text);
            Assert.Same(janela.BotaoFades, System.Windows.Input.FocusManager.GetFocusedElement(janela));

            janela.BotaoRemoverFades.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await janela.UltimaEdicao;

            Assert.False(janela.BotaoRemoverFades.IsEnabled);
            Assert.False(janela.MenuRemoverFades.IsEnabled);
            Assert.Contains("Fades removidos de Cena.", janela.Situacao.Text);
            Assert.Contains("Fades de áudio: nenhum", janela.Propriedades.Text);
            Assert.Same(janela.BotaoFades, System.Windows.Input.FocusManager.GetFocusedElement(janela));

            var duracaoDizima = new TempoMidia(2, 3);
            var itemDizima = itemFoto with { Duracao = duracaoDizima, FadeInVideo = duracaoDizima };
            var dialogoDizima = janela.CriarDialogoFades(itemDizima, TipoMidia.Imagem);
            dialogoDizima.Dialogo.WindowStartupLocation = WindowStartupLocation.Manual;
            dialogoDizima.Dialogo.Left = -10000; dialogoDizima.Dialogo.Top = -10000;
            dialogoDizima.Dialogo.ShowActivated = false; dialogoDizima.Dialogo.ShowInTaskbar = false;
            try
            {
                dialogoDizima.Dialogo.Show();
                dialogoDizima.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var obtidoDizima = dialogoDizima.ObterResultado();
                Assert.NotNull(obtidoDizima);
                Assert.Equal(duracaoDizima, obtidoDizima!.Value.FadeInVideo);
            }
            finally { dialogoDizima.Dialogo.Close(); }
        });
}
