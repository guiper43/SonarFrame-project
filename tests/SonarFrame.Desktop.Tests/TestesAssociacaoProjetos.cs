using System.IO;
using System.Windows;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class TestesAssociacaoProjetos
{
    [Fact]
    public Task ArgumentoAbreProjetoComEspacosEAcentosEPreservaFoco() =>
        TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            caminho = Path.Combine(Path.GetDirectoryName(caminho)!, "Edição de férias.SONARFRAME");
            var projeto = Projeto.Criar("Férias em família");
            await new RepositorioProjetosSqlite().SalvarAsync(caminho, projeto);
            File.Delete(caminho + ".recovery");
            janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
            await janela.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await janela.AbrirArgumentosAsync([caminho]);
            Assert.Equal(projeto.Id, janela.SessaoAtual!.Atual.Id);
            Assert.False(janela.SessaoAtual.TemAlteracoes);
            Assert.Equal(Visibility.Visible, janela.Editor.Visibility);
            Assert.Same(janela.BotaoAdicionar, System.Windows.Input.FocusManager.GetFocusedElement(janela));
            await janela.AbrirArgumentosAsync([caminho]);
            Assert.Contains("já está aberto", janela.Situacao.Text);
        });

    [Fact]
    public Task ArgumentosInvalidosEArquivosAusentesNaoCriamProjeto() =>
        TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            await janela.AbrirArgumentosAsync([]);
            Assert.Null(janela.SessaoAtual);
            await janela.AbrirArgumentosAsync([caminho, caminho]);
            Assert.Contains("apenas um arquivo", janela.Situacao.Text);
            await janela.AbrirArgumentosAsync([Path.ChangeExtension(caminho, ".mp4")]);
            Assert.Contains("extensão .sonarframe", janela.Situacao.Text);
            await janela.AbrirArgumentosAsync([caminho]);
            Assert.Contains("não encontrado", janela.Situacao.Text);
            Assert.Contains(janela.Mensagens.Items.Cast<string>(), m => m.Contains("não encontrado"));
            Assert.Null(janela.SessaoAtual);
            Assert.False(File.Exists(caminho));
        });

    [Fact]
    public Task ProjetoCorrompidoEAnunciadoSemAlterarArquivo() =>
        TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
        {
            const string conteudo = "Isto não é um projeto SQLite.";
            await File.WriteAllTextAsync(caminho, conteudo);
            await janela.AbrirArgumentosAsync([caminho]);
            Assert.Null(janela.SessaoAtual);
            Assert.Contains("Não foi possível abrir", janela.Situacao.Text);
            Assert.Equal(conteudo, await File.ReadAllTextAsync(caminho));
        });
}
