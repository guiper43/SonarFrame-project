using System.IO;
using System.Windows;
using SonarFrame.Application;
using SonarFrame.Core;
using Xunit;

namespace SonarFrame.Desktop.Tests;

public sealed class PreviaNativaFactAttribute : FactAttribute
{
    public PreviaNativaFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SONARFRAME_MIDIAS_PREVIA")))
            Skip = "Execute testar-previa-desktop.ps1 para testar o motor nativo com as amostras locais.";
    }
}

public sealed class TestesPreviaNativa
{
    [PreviaNativaFact]
    public Task RetomarVideoReordenadoRestauraAudioDoPlayer() => TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
    {
        var pasta = Environment.GetEnvironmentVariable("SONARFRAME_MIDIAS_PREVIA")!;
        var video = new Midia(Guid.NewGuid(), "Vídeo", Path.Combine(pasta, "origem.mp4"), TipoMidia.Video, TempoMidia.Segundos(8));
        var primeiro = new Clipe(Guid.NewGuid(), video.Id, "Primeiro", TempoMidia.Zero, TempoMidia.Segundos(3));
        var segundo = primeiro with { Id = Guid.NewGuid(), Nome = "Segundo", InicioNaOrigem = TempoMidia.Segundos(3) };
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        janela.ExibirProjeto(Projeto.Criar("Ordem e áudio") with { Biblioteca = [video], LinhaDoTempo = [primeiro, segundo] }, salvo: true, caminho: caminho);
        await janela.ReproduzirComposicaoAsync(false);
        janela.LinhaDoTempo.SelectedIndex = 1;
        janela.BotaoMoverAntes.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var prazo = DateTime.UtcNow.AddSeconds(10);
        while (janela.SessaoAtual!.Atual.LinhaDoTempo[0].Id != segundo.Id && DateTime.UtcNow < prazo) await Task.Delay(25);
        Assert.Equal(segundo.Id, janela.SessaoAtual!.Atual.LinhaDoTempo[0].Id);
        await janela.ReproduzirComposicaoAsync(false);
        await Task.Delay(250);
        await janela.ReproduzirComposicaoAsync(false);
        Assert.Equal(EstadoPrevia.Pausada, janela.PreviaAtual.Atual.Estado);
        var view = Assert.IsType<LibVLCSharp.WPF.VideoView>(janela.SuperficiePrevia.Content);
        view.MediaPlayer!.Mute = true;
        await janela.ReproduzirComposicaoAsync(false);
        Assert.Equal(EstadoPrevia.Reproduzindo, janela.PreviaAtual.Atual.Estado);
        Assert.False(view.MediaPlayer.Mute);
        await janela.LimparPreviaAsync();
    }, prazoSegundos: 180);

    [MatrizFormatosNativaFact]
    public Task BibliotecaNativaAbreTodosOsFormatosImportaveis() => TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
    {
        var pasta = Environment.GetEnvironmentVariable("SONARFRAME_MIDIAS_MATRIZ")!;
        var midias = new[] { "jpg", "jpeg", "png", "mp4", "mov", "mp3", "wav", "m4a", "ogg", "opus.ogg" }
            .Select(e => new Midia(Guid.NewGuid(), e, Path.Combine(pasta, "mídia de teste." + e),
                e is "jpg" or "jpeg" or "png" ? TipoMidia.Imagem : e is "mp4" or "mov" ? TipoMidia.Video : TipoMidia.Audio,
                e is "jpg" or "jpeg" or "png" ? null : TempoMidia.Segundos(2))).ToArray();
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        janela.ExibirProjeto(Projeto.Criar("Formatos nativos") with { Biblioteca = [.. midias] }, salvo: true, caminho: caminho);
        for (var i = 0; i < midias.Length; i++)
        {
            janela.Biblioteca.SelectedIndex = i;
            await janela.AbrirPreviaSelecionadaAsync();
            var esperado = midias[i].Tipo == TipoMidia.Imagem ? EstadoPrevia.Imagem : EstadoPrevia.Reproduzindo;
            Assert.True(janela.PreviaAtual.Atual.Estado == esperado, $"{midias[i].Nome}: {janela.PreviaAtual.Atual.Estado}, {janela.PreviaAtual.Atual.Mensagem}");
        }
        await janela.LimparPreviaAsync();
        foreach (var midia in midias) { using var arquivo = new FileStream(midia.Caminho, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        Assert.False(janela.SessaoAtual!.TemAlteracoes);
    }, prazoSegundos: 180);

    [PreviaNativaFact]
    public Task ComposicaoNativaPreparaReproduzEPausaNoCursor() => TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
    {
        var pasta = Environment.GetEnvironmentVariable("SONARFRAME_MIDIAS_PREVIA")!;
        var video = new Midia(Guid.NewGuid(), "Vídeo", Path.Combine(pasta, "origem.mp4"), TipoMidia.Video, TempoMidia.Segundos(8));
        var foto = new Midia(Guid.NewGuid(), "Foto", Path.Combine(pasta, "foto.png"), TipoMidia.Imagem, null);
        var projeto = Projeto.Criar("Composição nativa") with { Biblioteca = [video, foto], LinhaDoTempo = [
            new(Guid.NewGuid(), video.Id, video.Nome, new(13, 10), TempoMidia.Segundos(2)),
            new(Guid.NewGuid(), foto.Id, foto.Nome, TempoMidia.Zero, TempoMidia.Segundos(1))] };
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        janela.ExibirProjeto(projeto, salvo: true, caminho: caminho);
        await janela.NavegarTemporalAsync(System.Windows.Input.Key.Right, System.Windows.Input.ModifierKeys.None);
        Assert.NotNull(janela.QuadroDaComposicao.Source);
        Assert.True(janela.BotaoReproduzirPrevia.IsEnabled);
        Assert.Equal(EstadoPrevia.Vazia, janela.PreviaAtual.Atual.Estado);
        janela.SessaoAtual!.Posicionar(new(1, 2));
        await janela.ReproduzirComposicaoAsync(false);
        Assert.Equal(EstadoPrevia.Reproduzindo, janela.PreviaAtual.Atual.Estado);
        Assert.Contains("composição", janela.TituloPrevia.Text);
        await Task.Delay(250);
        await janela.ReproduzirComposicaoAsync(false);
        Assert.Equal(EstadoPrevia.Pausada, janela.PreviaAtual.Atual.Estado);
        Assert.InRange((double)janela.SessaoAtual.Atual.Cursor.Numerador / janela.SessaoAtual.Atual.Cursor.Denominador, 0.45, 2.0);
        Assert.False(janela.SessaoAtual.PodeDesfazer);
        Assert.NotNull(janela.QuadroDaComposicao.Source);
        Assert.Equal(Visibility.Visible, janela.QuadroDaComposicao.Visibility);
        await janela.NavegarTemporalAsync(System.Windows.Input.Key.End, System.Windows.Input.ModifierKeys.None);
        janela.SincronizarCursorComposicao();
        Assert.Equal(projeto.Duracao, janela.SessaoAtual.Atual.Cursor);
        Assert.NotNull(janela.QuadroDaComposicao.Source);
        await janela.ReproduzirComposicaoAsync(false);
        Assert.Equal(EstadoPrevia.Reproduzindo, janela.PreviaAtual.Atual.Estado);
        Assert.Equal(Visibility.Collapsed, janela.QuadroDaComposicao.Visibility);
        await janela.ReproduzirComposicaoAsync(false);
        janela.SessaoAtual.Posicionar(new(1, 5)); janela.SessaoAtual.Posicionar(new(7, 10), true);
        await janela.ReproduzirComposicaoAsync(true);
        Assert.Equal(new TempoMidia(1, 2), janela.PreviaAtual.Atual.Midia!.Duracao);
        var limite = DateTime.UtcNow.AddSeconds(10);
        while (janela.PreviaAtual.Atual.Estado == EstadoPrevia.Reproduzindo && DateTime.UtcNow < limite) await Task.Delay(50);
        Assert.Equal(EstadoPrevia.Concluida, janela.PreviaAtual.Atual.Estado);
        await janela.UltimoQuadro;
        Assert.Equal(new TempoMidia(7, 10), janela.SessaoAtual.Atual.Cursor);
        Assert.Equal("Reproduzir", janela.BotaoReproduzirPrevia.Content);
        await janela.ReproduzirComposicaoAsync(false);
        Assert.Equal(projeto.Duracao, janela.PreviaAtual.Atual.Midia!.Duracao);
        await janela.LimparPreviaAsync();
        Assert.Equal(EstadoPrevia.Vazia, janela.PreviaAtual.Atual.Estado);
    }, prazoSegundos: 180);

    [PreviaNativaFact]
    public Task PreviaNativaAbreImagemVideoAudioERecuperaFalhas() => TestesIntegracaoEditor.ComJanela(async (janela, caminho) =>
    {
        var pasta = Environment.GetEnvironmentVariable("SONARFRAME_MIDIAS_PREVIA")!;
        var midias = new[]
        {
            new Midia(Guid.NewGuid(), "Foto", Path.Combine(pasta, "foto.png"), TipoMidia.Imagem, null),
            new Midia(Guid.NewGuid(), "Vídeo", Path.Combine(pasta, "origem.mp4"), TipoMidia.Video, TempoMidia.Segundos(8)),
            new Midia(Guid.NewGuid(), "Áudio", Path.Combine(pasta, "camada.wav"), TipoMidia.Audio, TempoMidia.Segundos(8)),
            new Midia(Guid.NewGuid(), "Ausente", Path.Combine(pasta, "ausente.mp4"), TipoMidia.Video, TempoMidia.Segundos(8))
        };
        janela.ShowActivated = false; janela.ShowInTaskbar = false; janela.Left = -10000; janela.Top = -10000; janela.Show();
        janela.ExibirProjeto(Projeto.Criar("Teste nativo") with { Biblioteca = [.. midias] }, salvo: true, caminho: caminho);
        var original = janela.SessaoAtual!.Atual;
        janela.Biblioteca.SelectedIndex = 0; await janela.AbrirPreviaSelecionadaAsync();
        Assert.Equal(EstadoPrevia.Imagem, janela.PreviaAtual.Atual.Estado); Assert.NotNull(janela.SuperficiePrevia.Content);
        Assert.False(janela.BotaoReproduzirPrevia.IsEnabled);
        janela.Biblioteca.SelectedIndex = 1; await janela.AbrirPreviaSelecionadaAsync();
        Assert.Equal(EstadoPrevia.Reproduzindo, janela.PreviaAtual.Atual.Estado);
        await janela.PreviaAtual.PosicionarAsync(TempoMidia.Segundos(3));
        Assert.Equal(EstadoPrevia.Pausada, janela.PreviaAtual.Atual.Estado);
        Assert.InRange((double)janela.PreviaAtual.Posicao.Numerador / janela.PreviaAtual.Posicao.Denominador, 2.9, 3.1);
        janela.Biblioteca.SelectedIndex = 2; await janela.AbrirPreviaSelecionadaAsync();
        Assert.Equal(EstadoPrevia.Reproduzindo, janela.PreviaAtual.Atual.Estado);
        Assert.Equal(Visibility.Collapsed, janela.SuperficiePrevia.Visibility);
        await janela.PreviaAtual.PararAsync(); Assert.Equal(EstadoPrevia.Pausada, janela.PreviaAtual.Atual.Estado);
        janela.Biblioteca.SelectedIndex = 3; await janela.AbrirPreviaSelecionadaAsync();
        Assert.Equal(EstadoPrevia.Erro, janela.PreviaAtual.Atual.Estado);
        janela.Biblioteca.SelectedIndex = 0; await janela.AbrirPreviaSelecionadaAsync();
        Assert.Equal(EstadoPrevia.Imagem, janela.PreviaAtual.Atual.Estado);
        Assert.Same(original, janela.SessaoAtual.Atual); Assert.False(janela.SessaoAtual.TemAlteracoes);
        await janela.LimparPreviaAsync(); Assert.Null(janela.SuperficiePrevia.Content);
        foreach (var midia in midias.Take(3))
        {
            using var arquivo = new FileStream(midia.Caminho, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }, prazoSegundos: 180);
}

public sealed class MatrizFormatosNativaFactAttribute : FactAttribute
{
    public MatrizFormatosNativaFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SONARFRAME_MIDIAS_MATRIZ")))
            Skip = "Execute testar-previa-desktop.ps1 para validar todos os formatos no player nativo.";
    }
}
