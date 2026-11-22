using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class TestesPropriedadesVisuaisDesktop
{
    [Fact]
    public Task DialogoLimitaAlturaERolaAteOControleFocado() =>
        TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var video = new Midia(Guid.NewGuid(), "Vídeo", "video.mp4", TipoMidia.Video, TempoMidia.Segundos(10));
            var projeto = Projeto.Criar("Rolagem") with
            {
                Biblioteca = [video],
                LinhaDoTempo = [new(Guid.NewGuid(), video.Id, video.Nome, TempoMidia.Zero, TempoMidia.Segundos(10))]
            };
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            janela.ExibirProjeto(projeto, salvo: true, caminho: caminho);
            var configuracao = janela.CriarDialogoPropriedadesVisuais("Camada visual", "Aplicar", video,
                true, false, TempoMidia.Zero, TempoMidia.Zero, TempoMidia.Segundos(2), LayoutVisual.PadraoCamada);
            var dialogo = configuracao.Dialogo;
            Assert.Equal(SystemParameters.WorkArea.Height, dialogo.MaxHeight);
            dialogo.MaxHeight = 400;
            dialogo.ShowActivated = false; dialogo.ShowInTaskbar = false;
            try
            {
                dialogo.Show();
                await dialogo.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var rolagem = Assert.IsType<ScrollViewer>(Assert.IsType<Border>(dialogo.Content).Child);
                Assert.True(dialogo.ActualHeight <= 400);
                Assert.True(rolagem.ExtentHeight > rolagem.ViewportHeight);
                Assert.True(configuracao.Confirmar.Focus());
                await dialogo.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.True(rolagem.VerticalOffset > 0);
                Assert.Same(configuracao.Confirmar, FocusManager.GetFocusedElement(dialogo));
            }
            finally { dialogo.Close(); }
        });

    [Fact]
    public Task DialogoPropriedadesVisuaisSuportaNovePosicoesTamanhosPersonalizadosESubstituicaoPorTeclado()
    {
        return TestesIntegracaoEditor.ComJanela((janela, caminho) =>
        {
            var video = new Midia(Guid.NewGuid(), "Vídeo", "video.mp4", TipoMidia.Video, TempoMidia.Segundos(10));
            var foto = new Midia(Guid.NewGuid(), "Logo", "logo.png", TipoMidia.Imagem, null);
            var projeto = Projeto.Criar("Propriedades Desktop") with
            {
                Biblioteca = [video, foto],
                LinhaDoTempo = [new(Guid.NewGuid(), video.Id, video.Nome, TempoMidia.Zero, TempoMidia.Segundos(10))]
            };
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            janela.ExibirProjeto(projeto, salvo: true, caminho: caminho);

            var dialogo = janela.CriarDialogoPropriedadesVisuais(
                "Adicionar camada visual",
                "Adicionar camada",
                foto,
                ehCamada: true,
                permitirOpcaoSequencia: false,
                inicioInicial: TempoMidia.Segundos(1),
                origemInicial: TempoMidia.Zero,
                duracaoInicial: TempoMidia.Segundos(3),
                layoutInicial: LayoutVisual.PadraoCamada);

            Assert.Equal(9, dialogo.Posicao.Items.Count);
            Assert.Equal(4, dialogo.Tamanho.Items.Count);
            Assert.Equal(2, dialogo.Ajuste.Items.Count);
            Assert.Equal(0, dialogo.ModoExibicao.SelectedIndex);
            Assert.Equal(4, dialogo.Posicao.SelectedIndex);
            Assert.Equal(3, dialogo.Tamanho.SelectedIndex);

            dialogo.Posicao.SelectedIndex = (int)PosicaoVisual.SuperiorDireita;
            dialogo.Tamanho.SelectedIndex = 0;
            Assert.Equal("25", dialogo.EscalaPersonalizada.Text);

            dialogo.Tamanho.SelectedIndex = 3;
            dialogo.EscalaPersonalizada.Text = "33";
            dialogo.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(dialogo.ObterResultado());
            Assert.Contains("passos de 5%", dialogo.Erro.Text);

            dialogo.EscalaPersonalizada.Text = "35";
            dialogo.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var resultadoSobrepor = dialogo.ObterResultado();
            Assert.NotNull(resultadoSobrepor);
            Assert.Equal(
                new LayoutVisual(ModoExibicaoVisual.Sobrepor, PosicaoVisual.SuperiorDireita, 35, AjusteVisual.PermitirBordas),
                resultadoSobrepor!.Layout);

            var dialogoSubstituir = janela.CriarDialogoPropriedadesVisuais(
                "Inserir imagem",
                "Adicionar à composição",
                foto,
                ehCamada: true,
                permitirOpcaoSequencia: true,
                inicioInicial: TempoMidia.Segundos(9),
                origemInicial: TempoMidia.Zero,
                duracaoInicial: TempoMidia.Segundos(1),
                layoutInicial: LayoutVisual.PadraoCamada,
                duracaoSequenciaInicial: TempoMidia.Segundos(5));

            Assert.Equal(3, dialogoSubstituir.ModoExibicao.Items.Count);
            Assert.Equal("1", dialogoSubstituir.Duracao.Text);
            dialogoSubstituir.ModoExibicao.SelectedIndex = 2;
            Assert.Equal("5", dialogoSubstituir.Duracao.Text);
            dialogoSubstituir.ModoExibicao.SelectedIndex = 1;
            Assert.Equal("1", dialogoSubstituir.Duracao.Text);
            dialogoSubstituir.Ajuste.SelectedIndex = 1;
            dialogoSubstituir.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var resultadoSubstituir = dialogoSubstituir.ObterResultado();
            Assert.NotNull(resultadoSubstituir);
            Assert.False(resultadoSubstituir!.InserirNaSequencia);
            Assert.Equal(
                new LayoutVisual(ModoExibicaoVisual.Substituir, PosicaoVisual.Centro, 100, AjusteVisual.PreencherInteiro),
                resultadoSubstituir.Layout);

            var projetoRacional = Projeto.Criar("Racional") with
            {
                Biblioteca = [video, foto],
                LinhaDoTempo = [new(Guid.NewGuid(), foto.Id, foto.Nome, TempoMidia.Zero, new TempoMidia(8, 3))],
                Camadas = [new(new(Guid.NewGuid(), foto.Id, foto.Nome, TempoMidia.Zero, new TempoMidia(2, 3)), new TempoMidia(2, 1))]
            };
            janela.ExibirProjeto(projetoRacional, salvo: true, caminho: caminho);
            var dialogoRacional = janela.CriarDialogoPropriedadesVisuais(
                "Propriedades e intervalo da camada",
                "Aplicar",
                foto,
                ehCamada: true,
                permitirOpcaoSequencia: false,
                inicioInicial: new TempoMidia(2, 1),
                origemInicial: TempoMidia.Zero,
                duracaoInicial: new TempoMidia(2, 3),
                layoutInicial: LayoutVisual.PadraoCamada,
                ignorarClipeId: projetoRacional.Camadas[0].Clipe.Id);
            dialogoRacional.Posicao.SelectedIndex = (int)PosicaoVisual.InferiorCentro;
            dialogoRacional.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var resultadoRacional = dialogoRacional.ObterResultado();
            Assert.NotNull(resultadoRacional);
            Assert.Equal(new TempoMidia(2, 3), resultadoRacional!.Duracao);

            var dialogoSequenciaCurta = janela.CriarDialogoPropriedadesVisuais(
                "Propriedades visuais do clipe",
                "Aplicar",
                foto,
                ehCamada: false,
                permitirOpcaoSequencia: false,
                inicioInicial: TempoMidia.Zero,
                origemInicial: TempoMidia.Zero,
                duracaoInicial: new TempoMidia(8, 3),
                layoutInicial: LayoutVisual.PadraoSequencia,
                ignorarClipeId: projetoRacional.LinhaDoTempo[0].Id);
            dialogoSequenciaCurta.Duracao.Text = "1";
            dialogoSequenciaCurta.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(dialogoSequenciaCurta.ObterResultado());
            Assert.Contains("deixaria camadas fora da composição", dialogoSequenciaCurta.Erro.Text);

            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task InsercaoAproveitaSelecaoTemporalAnunciaSobreposicaoSimultaneaEPermiteEdicao()
    {
        return TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var video = new Midia(Guid.NewGuid(), "Vídeo", "video.mp4", TipoMidia.Video, TempoMidia.Segundos(10));
            var foto1 = new Midia(Guid.NewGuid(), "Marca 1", "marca1.png", TipoMidia.Imagem, null);
            var foto2 = new Midia(Guid.NewGuid(), "Marca 2", "marca2.png", TipoMidia.Imagem, null);
            var audio = new Midia(Guid.NewGuid(), "Trilha", "trilha.wav", TipoMidia.Audio, TempoMidia.Segundos(10));
            var sessao = new SessaoEdicao(Projeto.Criar("Fluxo Visual"));
            sessao.Executar(new RegistrarMidia(video));
            sessao.Executar(new RegistrarMidia(foto1));
            sessao.Executar(new RegistrarMidia(foto2));
            sessao.Executar(new RegistrarMidia(audio));
            sessao.Executar(new InserirClipe(video.Id, 0));
            sessao.Posicionar(TempoMidia.Segundos(2));
            sessao.Posicionar(TempoMidia.Segundos(6), true);

            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            janela.ExibirProjeto(sessao.Atual, salvo: true, caminho: caminho);
            janela.Biblioteca.SelectedIndex = 1;

            var dialogoPreenchendoSelecao = janela.CriarDialogoPropriedadesVisuais(
                "Adicionar camada visual",
                "Adicionar camada",
                foto1,
                ehCamada: true,
                permitirOpcaoSequencia: false,
                inicioInicial: janela.SessaoAtual!.Atual.Selecao!.Inicio,
                origemInicial: TempoMidia.Zero,
                duracaoInicial: janela.SessaoAtual.Atual.Selecao.Fim - janela.SessaoAtual.Atual.Selecao.Inicio,
                layoutInicial: LayoutVisual.PadraoCamada);
            Assert.Equal("2", dialogoPreenchendoSelecao.InicioComposicao.Text);
            Assert.Equal("4", dialogoPreenchendoSelecao.Duracao.Text);

            var layoutCanto = new LayoutVisual(ModoExibicaoVisual.Sobrepor, PosicaoVisual.SuperiorEsquerda, 25);
            janela.SolicitarPropriedadesVisuaisOverride = () => new(
                InserirNaSequencia: false,
                Inicio: TempoMidia.Segundos(2),
                InicioNaOrigem: TempoMidia.Zero,
                Duracao: TempoMidia.Segundos(4),
                Layout: layoutCanto);
            await janela.AdicionarCamadaIndependenteAsync();

            Assert.Single(janela.SessaoAtual.Atual.Camadas);
            Assert.Equal(layoutCanto, janela.SessaoAtual.Atual.Camadas[0].Clipe.Layout);
            Assert.Contains("Superior à esquerda", janela.Propriedades.Text);
            Assert.Contains("Pequeno (limite de 25%)", janela.Propriedades.Text);

            janela.Biblioteca.SelectedIndex = 2;
            janela.SolicitarPropriedadesVisuaisOverride = () => new(
                InserirNaSequencia: false,
                Inicio: TempoMidia.Segundos(3),
                InicioNaOrigem: TempoMidia.Zero,
                Duracao: TempoMidia.Segundos(3),
                Layout: layoutCanto);
            await janela.AdicionarCamadaIndependenteAsync();

            Assert.Equal(2, janela.SessaoAtual.Atual.Camadas.Length);
            Assert.Contains("sobreposição simultânea na mesma região com Marca 1", janela.Situacao.Text);
            Assert.Contains("Sobreposição simultânea na mesma região com: Marca 1", janela.Propriedades.Text);

            var layoutEditado = new LayoutVisual(ModoExibicaoVisual.Sobrepor, PosicaoVisual.InferiorDireita, 75);
            janela.SolicitarPropriedadesVisuaisOverride = () => new(
                InserirNaSequencia: false,
                Inicio: TempoMidia.Segundos(3),
                InicioNaOrigem: TempoMidia.Zero,
                Duracao: TempoMidia.Segundos(3),
                Layout: layoutEditado);
            await janela.EditarPropriedadesVisuaisAsync();

            Assert.Equal(layoutEditado, janela.SessaoAtual.Atual.Camadas[1].Clipe.Layout);
            Assert.Contains("Inferior à direita", janela.Propriedades.Text);
            Assert.Contains("Grande (75%)", janela.Propriedades.Text);
            Assert.DoesNotContain("Sobreposição simultânea", janela.Propriedades.Text);

            await janela.NavegarTemporalAsync(Key.Right, ModifierKeys.Control);
            janela.LinhaDoTempo.SelectedIndex = 0;
            janela.SolicitarPropriedadesVisuaisOverride = () => new(
                InserirNaSequencia: true,
                Inicio: TempoMidia.Zero,
                InicioNaOrigem: TempoMidia.Zero,
                Duracao: TempoMidia.Segundos(10),
                Layout: new LayoutVisual(ModoExibicaoVisual.Substituir, PosicaoVisual.Centro, 100, AjusteVisual.PreencherInteiro));
            await janela.EditarPropriedadesVisuaisAsync();
            Assert.Equal("Propriedades visuais atualizadas.", janela.Situacao.Text);

            janela.SessaoAtual.Executar(new AdicionarCamada(audio.Id, TempoMidia.Segundos(1), TempoMidia.Zero, TempoMidia.Segundos(2)));
            janela.Atualizar();
            Assert.False(janela.BotaoPropriedadesVisuais.IsEnabled);
            Assert.False(janela.MenuPropriedadesVisuais.IsEnabled);

            Assert.True(await janela.SalvarAtualAsync());
            var recarregado = await new RepositorioProjetosSqlite().CarregarAsync(caminho);
            Assert.Equal(layoutCanto, recarregado.Camadas[0].Clipe.Layout);
            Assert.Equal(layoutEditado, recarregado.Camadas[1].Clipe.Layout);
        });
    }
}
