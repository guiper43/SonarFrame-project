using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SonarFrame.Application;
using SonarFrame.Core;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class TestesLegendasDesktop
{
    [Fact]
    public Task ImportarSrtConfirmaSubstituicaoEEdicaoContextualPreservaPausaPosicaoEFoco() =>
        TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var sessao = new SessaoEdicao(Projeto.Criar("Legendas Desktop"));
            var video = new Midia(Guid.NewGuid(), "Aula", "aula.mp4", TipoMidia.Video, TempoMidia.Segundos(10), new(30, 1));
            sessao.Executar(new RegistrarMidia(video));
            sessao.Executar(new InserirClipe(video.Id, 0));

            janela.ExibirProjeto(sessao.Atual, salvo: true, caminho: caminho);
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();

            var pasta = Path.GetDirectoryName(caminho)!;
            var srt1 = Path.Combine(pasta, "primeira.srt");
            var srt2 = Path.Combine(pasta, "segunda.srt");
            await File.WriteAllTextAsync(srt1, "1\n00:00:00,000 --> 00:00:03,000\nLegenda inicial\n\n2\n00:00:05,000 --> 00:00:08,000\nSegunda fala\n", Encoding.UTF8);
            await File.WriteAllTextAsync(srt2, "1\n00:00:01,000 --> 00:00:04,000\nLegenda substituta\n", Encoding.UTF8);

            var importacao = new DialogoImportacao(janela, janela.SessaoAtual!, new LeitorMetadadosFalso());
            importacao.WindowStartupLocation = WindowStartupLocation.Manual;
            importacao.Left = -10000; importacao.Top = -10000;
            importacao.ShowActivated = false; importacao.ShowInTaskbar = false;
            try
            {
                importacao.Show();
                await importacao.ImportarLegendaSrtAsync(janela.SessaoAtual!, srt1);
                Assert.True(importacao.ImportouLegendas);
                Assert.Equal(2, janela.SessaoAtual!.Atual.Legendas.Length);
                Assert.Equal("Legenda inicial", janela.SessaoAtual.Atual.Legendas[0].Texto);

                var confirmacaoModal = importacao.CriarDialogoSubstituicaoLegendas(janela.SessaoAtual.Atual.Legendas.Length);
                confirmacaoModal.Dialogo.WindowStartupLocation = WindowStartupLocation.Manual;
                confirmacaoModal.Dialogo.Left = -10000; confirmacaoModal.Dialogo.Top = -10000;
                confirmacaoModal.Dialogo.ShowActivated = false; confirmacaoModal.Dialogo.ShowInTaskbar = false;
                try
                {
                    confirmacaoModal.Dialogo.Show();
                    confirmacaoModal.Substituir.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(confirmacaoModal.Confirmou());
                }
                finally { confirmacaoModal.Dialogo.Close(); }

                importacao.ConfirmarSubstituicaoLegendasOverride = _ => false;
                await importacao.ImportarLegendaSrtAsync(janela.SessaoAtual!, srt2);
                Assert.Equal(2, janela.SessaoAtual.Atual.Legendas.Length);
                Assert.Equal("Legenda inicial", janela.SessaoAtual.Atual.Legendas[0].Texto);

                importacao.ConfirmarSubstituicaoLegendasOverride = _ => true;
                await importacao.ImportarLegendaSrtAsync(janela.SessaoAtual!, srt2);
                Assert.Single(janela.SessaoAtual.Atual.Legendas);
                Assert.Equal("Legenda substituta", janela.SessaoAtual.Atual.Legendas[0].Texto);

                await importacao.ImportarLegendaSrtAsync(janela.SessaoAtual!, srt1);
                Assert.Equal(2, janela.SessaoAtual.Atual.Legendas.Length);
            }
            finally { importacao.Close(); }

            janela.SessaoAtual!.Posicionar(TempoMidia.Segundos(4));
            janela.Atualizar();
            Assert.False(janela.MenuEditarLegenda.IsEnabled);
            Assert.False(janela.BotaoEditarLegenda.IsEnabled);

            janela.SessaoAtual.Posicionar(TempoMidia.Segundos(1));
            janela.LinhaDoTempo.SelectedIndex = 0;
            janela.Atualizar();
            Assert.True(janela.MenuEditarLegenda.IsEnabled);
            Assert.True(janela.BotaoEditarLegenda.IsEnabled);
            Assert.Contains("Legenda ativa (00:00:00.000 até 00:00:03.000): Legenda inicial", janela.Propriedades.Text);

            var legendaAtiva = new AcoesEdicao(janela.SessaoAtual).LegendaAtiva()!;
            var dialogo = janela.CriarDialogoLegenda(legendaAtiva);
            dialogo.Dialogo.WindowStartupLocation = WindowStartupLocation.Manual;
            dialogo.Dialogo.Left = -10000; dialogo.Dialogo.Top = -10000;
            dialogo.Dialogo.ShowActivated = false; dialogo.Dialogo.ShowInTaskbar = false;
            try
            {
                dialogo.Dialogo.Show();
                Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(dialogo.Erro));
                Assert.Equal(9, dialogo.Posicao.Items.Count);
                Assert.Equal(7, dialogo.Posicao.SelectedIndex);
                Assert.Equal(2, dialogo.Escopo.Items.Count);

                dialogo.Texto.Text = "   ";
                dialogo.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(dialogo.ObterResultado());
                Assert.Contains("Informe o texto da legenda", dialogo.Erro.Text);
                Assert.Same(dialogo.Texto, System.Windows.Input.FocusManager.GetFocusedElement(dialogo.Dialogo));

                dialogo.Texto.Text = "Legenda revisada";
                dialogo.Tamanho.Text = "200";
                dialogo.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(dialogo.ObterResultado());
                Assert.Contains("entre 8 e 120", dialogo.Erro.Text);
                Assert.Same(dialogo.Tamanho, System.Windows.Input.FocusManager.GetFocusedElement(dialogo.Dialogo));

                dialogo.Tamanho.Text = "34";
                dialogo.Cor.SelectedIndex = 1;
                dialogo.Negrito.IsChecked = true;
                dialogo.Posicao.SelectedIndex = 1;
                dialogo.Escopo.SelectedIndex = 1;
                dialogo.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var resultado = dialogo.ObterResultado();
                Assert.NotNull(resultado);
                Assert.Equal("Legenda revisada", resultado!.Value.Texto);
                Assert.Equal(34, resultado.Value.Estilo.Tamanho);
                Assert.Equal("#FFFF00", resultado.Value.Estilo.Cor);
                Assert.True(resultado.Value.Estilo.Negrito);
                Assert.Equal(PosicaoVisual.SuperiorCentro, resultado.Value.Estilo.Posicao);
                Assert.Equal(EscopoEstiloLegenda.TodasDoProjeto, resultado.Value.Escopo);
            }
            finally { dialogo.Dialogo.Close(); }

            janela.SolicitarEdicaoLegendaOverride = _ => null;
            janela.MenuEditarLegenda.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await janela.UltimaEdicao;
            Assert.Equal(TempoMidia.Segundos(1), janela.SessaoAtual.Atual.Cursor);
            Assert.Contains("Edição de legenda cancelada", janela.Situacao.Text);
            Assert.True(janela.LinhaDoTempo.IsKeyboardFocusWithin);

            var novoEstilo = new EstiloLegenda("Verdana", 32, "#FFFF00", Negrito: true, Italico: false, Fundo: true, PosicaoVisual.SuperiorCentro);
            janela.SolicitarEdicaoLegendaOverride = _ => ("Legenda confirmada", novoEstilo, EscopoEstiloLegenda.TodasDoProjeto);
            janela.MenuEditarLegenda.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await janela.UltimaEdicao;

            Assert.Equal(TempoMidia.Segundos(1), janela.SessaoAtual.Atual.Cursor);
            Assert.Equal("Legenda confirmada", janela.SessaoAtual.Atual.Legendas[0].Texto);
            Assert.Equal("Segunda fala", janela.SessaoAtual.Atual.Legendas[1].Texto);
            Assert.Equal(novoEstilo, janela.SessaoAtual.Atual.Legendas[0].Estilo);
            Assert.Equal(novoEstilo, janela.SessaoAtual.Atual.Legendas[1].Estilo);
            Assert.Contains("Legenda atualizada", janela.Situacao.Text);
            Assert.True(janela.LinhaDoTempo.IsKeyboardFocusWithin);
        });

    [Fact]
    public Task EscolherLegendaSimultaneaPermiteEditarCadaTextoECancelarPreservandoCursorEFoco() =>
        TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            var sessao = new SessaoEdicao(Projeto.Criar("Legendas simultâneas"));
            var video = new Midia(Guid.NewGuid(), "Vídeo", "video.mp4", TipoMidia.Video, TempoMidia.Segundos(4));
            sessao.Executar(new RegistrarMidia(video));
            sessao.Executar(new InserirClipe(video.Id, 0));
            var legendas = LeitorSrt.Ler("1\n00:00:01,000 --> 00:00:03,000\nPrimeira\n\n2\n00:00:01,000 --> 00:00:03,000\nSegunda");
            sessao.Executar(new DefinirLegendas(legendas));
            sessao.Posicionar(TempoMidia.Segundos(2));
            janela.ExibirProjeto(sessao.Atual, salvo: true, caminho: caminho);
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            janela.LinhaDoTempo.SelectedIndex = 0;
            for (var indice = 0; indice < legendas.Length; indice++)
            {
                var escolha = janela.CriarDialogoEscolhaLegenda(legendas);
                escolha.Dialogo.WindowStartupLocation = WindowStartupLocation.Manual;
                escolha.Dialogo.Left = -10000; escolha.Dialogo.Top = -10000;
                escolha.Dialogo.ShowActivated = false; escolha.Dialogo.ShowInTaskbar = false;
                try
                {
                    escolha.Dialogo.Show();
                    Assert.Equal("Legendas ativas nesta posição", AutomationProperties.GetName(escolha.Lista));
                    Assert.Contains("Cima e Baixo", AutomationProperties.GetHelpText(escolha.Lista));
                    Assert.True(escolha.Lista.IsKeyboardFocusWithin);
                    Assert.Contains(legendas[indice].Texto, Assert.IsType<string>(escolha.Lista.Items[indice]));
                    escolha.Lista.SelectedIndex = indice;
                    escolha.Confirmar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var selecionada = escolha.ObterResultado();
                    Assert.Equal(legendas[indice].Id, selecionada!.Id);
                    janela.SolicitarLegendaAtivaOverride = ativas => ativas.Single(l => l.Id == selecionada.Id);
                    janela.SolicitarEdicaoLegendaOverride = l => (l.Texto + " revisada", l.Estilo, EscopoEstiloLegenda.ApenasSelecionada);
                    await janela.EditarLegendaAsync(janela.LinhaDoTempo);
                    Assert.Equal(legendas[indice].Texto + " revisada", janela.SessaoAtual!.Atual.Legendas[indice].Texto);
                    Assert.Equal(TempoMidia.Segundos(2), janela.SessaoAtual.Atual.Cursor);
                    Assert.True(janela.LinhaDoTempo.IsKeyboardFocusWithin);
                }
                finally { escolha.Dialogo.Close(); }
            }
            var antes = janela.SessaoAtual!.Atual;
            janela.SolicitarLegendaAtivaOverride = _ => null;
            janela.SolicitarEdicaoLegendaOverride = _ => throw new InvalidOperationException("Não deve abrir o editor após cancelar a escolha.");
            await janela.EditarLegendaAsync(janela.LinhaDoTempo);
            Assert.Equal(antes, janela.SessaoAtual.Atual);
            Assert.Contains("Edição de legenda cancelada", janela.Situacao.Text);
            Assert.True(janela.LinhaDoTempo.IsKeyboardFocusWithin);
        });

    private sealed class LeitorMetadadosFalso : ILeitorMetadadosMidia
    {
        public Task<Midia> LerAsync(string caminho, CancellationToken cancellationToken = default)
            => Task.FromResult(new Midia(Guid.NewGuid(), Path.GetFileName(caminho), caminho, TipoMidia.Video, TempoMidia.Segundos(5)));
    }
}
