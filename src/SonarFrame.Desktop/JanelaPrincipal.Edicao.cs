using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Microsoft.Win32;

namespace SonarFrame.Desktop;

public partial class JanelaPrincipal
{
    internal Task UltimaEdicao { get; private set; } = Task.CompletedTask;
    private bool editandoFades;
    internal sealed record DialogoConfiguracaoFades(
        Window Dialogo,
        TextBox? FadeInAudio,
        TextBox? FadeOutAudio,
        TextBox? FadeInVideo,
        TextBox? FadeOutVideo,
        TextBlock Erro,
        Button Confirmar,
        Func<(TempoMidia FadeInAudio, TempoMidia FadeOutAudio, TempoMidia FadeInVideo, TempoMidia FadeOutVideo)?> ObterResultado);
    internal Func<ItemComposicao, TipoMidia, (TempoMidia FadeInAudio, TempoMidia FadeOutAudio, TempoMidia FadeInVideo, TempoMidia FadeOutVideo)?>? SolicitarFadesOverride { get; set; }
    private void DescreverMidia(Guid id)
    {
        if (sessao is null) return;
        var midia = sessao.Atual.Biblioteca.Single(a => a.Id == id);
        Propriedades.Text = $"{midia.Nome}\nTipo: {Tipo(midia.Tipo)}\nDuração: {midia.Duracao?.ToString() ?? "definida ao inserir"}\nArquivo: {midia.Caminho}";
        if (!File.Exists(midia.Caminho)) Propriedades.Text += "\nArquivo ausente. Use Localizar arquivo na biblioteca.";
        if (midia.TaxaQuadrosVariavel) Propriedades.Text += "\nTaxa de quadros possivelmente variável; use navegação por segundos.";
    }
    private void DescreverConteudo(ItemComposicao item)
    {
        var tipo = sessao!.Atual.Biblioteca.Single(a => a.Id == item.MidiaId).Tipo;
        var texto = $"{item.Nome}\n{(item.EhCamada ? "Camada independente" : "Sequência principal")}\nInício na composição: {item.Inicio}\nDuração: {item.Duracao}";
        if (tipo != TipoMidia.Imagem)
        {
            texto += $"\nVolume: {item.Volume}%";
            texto += item.FadeInAudio > TempoMidia.Zero || item.FadeOutAudio > TempoMidia.Zero
                ? $"\nFades de áudio: fade-in {item.FadeInAudio}, fade-out {item.FadeOutAudio}"
                : "\nFades de áudio: nenhum";
        }
        if (tipo != TipoMidia.Audio)
        {
            texto += item.FadeInVideo > TempoMidia.Zero || item.FadeOutVideo > TempoMidia.Zero
                ? $"\nFades de vídeo: fade-in {item.FadeInVideo}, fade-out {item.FadeOutVideo}"
                : "\nFades de vídeo: nenhum";
        }
        Propriedades.Text = texto;
    }
    private async Task AplicarAcaoAsync(Action<AcoesEdicao> acao, string mensagem)
    {
        if (sessao is null || !await PrepararEdicaoAsync()) return;
        try { acao(new AcoesEdicao(sessao)); exibirPropriedadesBiblioteca = false; Atualizar(); Anunciar(mensagem); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { Anunciar(e.Message); }
    }
    private void DividirNoCursor(object sender, RoutedEventArgs e) => UltimaEdicao = DividirNoCursorAsync();
    private async Task DividirNoCursorAsync()
    {
        if (sessao is null || !await PrepararEdicaoAsync()) return;
        try
        {
            var posicao = sessao.Atual.Cursor;
            var nome = new AcoesEdicao(sessao).Conteudos().FirstOrDefault(c => !c.EhCamada && c.Inicio < posicao && c.Inicio + c.Duracao > posicao)?.Nome;
            new AcoesEdicao(sessao).DividirNoCursor();
            exibirPropriedadesBiblioteca = false; Atualizar();
            Anunciar($"Corte realizado em {posicao}. {nome} dividido em Parte 1 e Parte 2.");
        }
        catch (Exception erro) when (erro is ArgumentException or InvalidOperationException) { Anunciar(erro.Message); }
    }
    private void ExcluirIntervaloSelecionado(object sender, RoutedEventArgs e) => UltimaEdicao = ExcluirIntervaloSelecionadoAsync();
    private async Task ExcluirIntervaloSelecionadoAsync()
    {
        if (sessao is null || !await PrepararEdicaoAsync()) return;
        try
        {
            var selecao = sessao.Atual.Selecao;
            new AcoesEdicao(sessao).ExcluirSelecao();
            exibirPropriedadesBiblioteca = false; Atualizar();
            FocarConteudoSelecionado();
            Anunciar($"Intervalo de {selecao!.Inicio} até {selecao.Fim} excluído e lacuna fechada. Posição {sessao.Atual.Cursor}. Duração da composição {sessao.Atual.Duracao}.");
        }
        catch (Exception erro) when (erro is ArgumentException or InvalidOperationException) { Anunciar(erro.Message); }
    }
    private async void AlterarVolume(object sender, RoutedEventArgs e)
    {
        if (sessao is null) return;
        try
        {
            var selecionado = new AcoesEdicao(sessao).Selecionado();
            var valores = SolicitarNumeros("Volume do conteúdo", [("Volume, em porcentagem", (decimal)selecionado.Volume, 0m, 200m, true)]);
            if (valores is not null) await AplicarAcaoAsync(a => a.DefinirVolumeSelecionado((int)valores[0]), $"Volume definido para {valores[0]}%.");
        }
        catch (InvalidOperationException ex) { Anunciar(ex.Message); }
        BotaoVolume.Focus();
    }
    private void ConfigurarFades(object sender, RoutedEventArgs e) => UltimaEdicao = ConfigurarFadesAsync(BotaoFades);
    private void ConfigurarFadesMenu(object sender, RoutedEventArgs e) => UltimaEdicao = ConfigurarFadesAsync(LinhaDoTempo);
    private void RemoverFades(object sender, RoutedEventArgs e) => UltimaEdicao = RemoverFadesAsync(BotaoFades);
    private void RemoverFadesMenu(object sender, RoutedEventArgs e) => UltimaEdicao = RemoverFadesAsync(LinhaDoTempo);
    internal async Task ConfigurarFadesAsync(UIElement destinoFoco)
    {
        if (sessao is null || Ocupado || editandoFades) return;
        editandoFades = true;
        try
        {
            if (!await PrepararEdicaoAsync()) return;
            var selecionado = new AcoesEdicao(sessao).Selecionado();
            var midia = sessao.Atual.Biblioteca.Single(a => a.Id == selecionado.MidiaId);
            var valores = SolicitarFadesOverride is { } overrideFades
                ? overrideFades(selecionado, midia.Tipo)
                : SolicitarFades(selecionado, midia.Tipo);
            if (valores is { } f)
            {
                var mensagem = ResumoFadesAnuncio(selecionado.Nome, midia.Tipo, f.FadeInAudio, f.FadeOutAudio, f.FadeInVideo, f.FadeOutVideo);
                await AplicarAcaoAsync(a => a.DefinirFadesSelecionado(f.FadeInAudio, f.FadeOutAudio, f.FadeInVideo, f.FadeOutVideo), mensagem);
            }
        }
        catch (InvalidOperationException ex) { Anunciar(ex.Message); }
        finally { editandoFades = false; }
        if (ReferenceEquals(destinoFoco, LinhaDoTempo)) FocarConteudoSelecionado();
        else RestaurarFocoAposDialogo(destinoFoco, BotaoFades);
    }
    internal async Task RemoverFadesAsync(UIElement destinoFoco)
    {
        if (sessao is null || Ocupado || editandoFades) return;
        editandoFades = true;
        try
        {
            var selecionado = new AcoesEdicao(sessao).Selecionado();
            await AplicarAcaoAsync(a => a.RemoverFadesSelecionado(), $"Fades removidos de {selecionado.Nome}.");
        }
        catch (InvalidOperationException ex) { Anunciar(ex.Message); }
        finally { editandoFades = false; }
        if (ReferenceEquals(destinoFoco, LinhaDoTempo)) FocarConteudoSelecionado();
        else RestaurarFocoAposDialogo(BotaoFades, LinhaDoTempo);
    }
    private static string ResumoFadesAnuncio(string nome, TipoMidia tipo, TempoMidia inAudio, TempoMidia outAudio, TempoMidia inVideo, TempoMidia outVideo)
    {
        if (inAudio == TempoMidia.Zero && outAudio == TempoMidia.Zero && inVideo == TempoMidia.Zero && outVideo == TempoMidia.Zero)
            return $"Fades removidos de {nome}.";
        var partes = new List<string>();
        if (tipo != TipoMidia.Imagem) partes.Add($"áudio (fade-in {inAudio}, fade-out {outAudio})");
        if (tipo != TipoMidia.Audio) partes.Add($"vídeo (fade-in {inVideo}, fade-out {outVideo})");
        return $"Fades atualizados em {nome}: {string.Join("; ", partes)}.";
    }
    private (TempoMidia FadeInAudio, TempoMidia FadeOutAudio, TempoMidia FadeInVideo, TempoMidia FadeOutVideo)? SolicitarFades(ItemComposicao item, TipoMidia tipo)
    {
        var configuracao = CriarDialogoFades(item, tipo);
        configuracao.Dialogo.ShowDialog();
        return configuracao.ObterResultado();
    }
    internal DialogoConfiguracaoFades CriarDialogoFades(ItemComposicao item, TipoMidia tipo)
    {
        (TempoMidia FadeInAudio, TempoMidia FadeOutAudio, TempoMidia FadeInVideo, TempoMidia FadeOutVideo)? resultado = null;
        var duracaoMax = decimal.Round(Segundos(item.Duracao), 6);
        var duracaoTexto = duracaoMax.ToString("0.######", CultureInfo.CurrentCulture);
        var painel = new StackPanel();
        painel.Children.Add(new TextBlock
        {
            Text = $"Informe a duração em segundos (0 remove o efeito). Duração do conteúdo: {duracaoTexto} s.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        (TextBox Caixa, string TextoInicial, TempoMidia ValorOriginal) CriarCampo(string rotulo, string nomeAcessivel, TempoMidia valor)
        {
            var inicial = decimal.Round(Segundos(valor), 6).ToString("0.######", CultureInfo.CurrentCulture);
            var caixa = new TextBox { Text = inicial, Margin = new Thickness(0, 4, 0, 12) };
            painel.Children.Add(new Label { Content = rotulo, Target = caixa });
            AutomationProperties.SetName(caixa, nomeAcessivel);
            painel.Children.Add(caixa);
            return (caixa, inicial, valor);
        }
        (TextBox Caixa, string TextoInicial, TempoMidia ValorOriginal)? fadeInAudio = null, fadeOutAudio = null, fadeInVideo = null, fadeOutVideo = null;
        if (tipo != TipoMidia.Imagem)
        {
            fadeInAudio = CriarCampo("Fade-_in de áudio, em segundos", "Fade-in de áudio, em segundos", item.FadeInAudio);
            fadeOutAudio = CriarCampo("Fade-_out de áudio, em segundos", "Fade-out de áudio, em segundos", item.FadeOutAudio);
        }
        if (tipo != TipoMidia.Audio)
        {
            fadeInVideo = CriarCampo("Fade-in de _vídeo, em segundos", "Fade-in de vídeo, em segundos", item.FadeInVideo);
            fadeOutVideo = CriarCampo("Fade-out de ví_deo, em segundos", "Fade-out de vídeo, em segundos", item.FadeOutVideo);
        }
        var erro = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetLiveSetting(erro, AutomationLiveSetting.Assertive);
        painel.Children.Add(erro);
        var dialogo = CriarDialogo("Fades do conteúdo", painel);
        var confirmar = new Button { Content = "Aplicar", IsDefault = true };
        void InformarErro(string mensagem, TextBox destino)
        {
            erro.Text = mensagem;
            (UIElementAutomationPeer.FromElement(erro) ?? UIElementAutomationPeer.CreatePeerForElement(erro))
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            destino.Focus();
            destino.SelectAll();
        }
        bool TentarLer((TextBox Caixa, string TextoInicial, TempoMidia ValorOriginal)? campo, string nome, out TempoMidia tempo)
        {
            tempo = TempoMidia.Zero;
            if (campo is not { } c) return true;
            if (string.Equals(c.Caixa.Text.Trim(), c.TextoInicial, StringComparison.Ordinal) && c.ValorOriginal <= item.Duracao)
            {
                tempo = c.ValorOriginal;
                return true;
            }
            if (!decimal.TryParse(c.Caixa.Text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.CurrentCulture, out var valor)
                || valor < 0m || valor > duracaoMax)
            {
                InformarErro($"{nome}: informe um número entre 0 e {duracaoTexto}.", c.Caixa);
                return false;
            }
            tempo = TempoMidia.Minimo(item.Duracao, Tempo(valor));
            return true;
        }
        confirmar.Click += (_, _) =>
        {
            if (!TentarLer(fadeInAudio, "Fade-in de áudio, em segundos", out var inAudio)) return;
            if (!TentarLer(fadeOutAudio, "Fade-out de áudio, em segundos", out var outAudio)) return;
            if (inAudio + outAudio > item.Duracao)
            {
                InformarErro($"A soma de fade-in e fade-out de áudio não pode ultrapassar a duração do conteúdo ({duracaoTexto} s).", fadeOutAudio!.Value.Caixa);
                return;
            }
            if (!TentarLer(fadeInVideo, "Fade-in de vídeo, em segundos", out var inVideo)) return;
            if (!TentarLer(fadeOutVideo, "Fade-out de vídeo, em segundos", out var outVideo)) return;
            if (inVideo + outVideo > item.Duracao)
            {
                InformarErro($"A soma de fade-in e fade-out de vídeo não pode ultrapassar a duração do conteúdo ({duracaoTexto} s).", fadeOutVideo!.Value.Caixa);
                return;
            }
            resultado = (inAudio, outAudio, inVideo, outVideo);
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) dialogo.DialogResult = true;
            else dialogo.Close();
        };
        painel.Children.Add(confirmar);
        AdicionarFechar(dialogo, painel, "Cancelar");
        var primeiroCampo = fadeInAudio?.Caixa ?? fadeInVideo!.Value.Caixa;
        dialogo.Loaded += (_, _) => { primeiroCampo.Focus(); primeiroCampo.SelectAll(); };
        return new(dialogo, fadeInAudio?.Caixa, fadeOutAudio?.Caixa, fadeInVideo?.Caixa, fadeOutVideo?.Caixa, erro, confirmar, () => resultado);
    }
    private static decimal Segundos(TempoMidia tempo) => (decimal)tempo.Numerador / tempo.Denominador;
    private static TempoMidia Tempo(decimal segundos) => new((long)decimal.Round(segundos * 1_000_000), 1_000_000);
    private async void AdicionarCamadaIndependente(object sender, RoutedEventArgs e)
    {
        if (sessao is null || Biblioteca.SelectedItems.Count != 1) { Anunciar("Selecione uma mídia da biblioteca."); return; }
        var midia = sessao.Atual.Biblioteca.Single(a => a.Id == ((Linha)Biblioteca.SelectedItem).Id);
        var disponivel = sessao.Atual.Duracao - sessao.Atual.Cursor;
        if (disponivel <= TempoMidia.Zero) { Anunciar("Posicione o cursor antes do fim da composição para adicionar uma camada."); return; }
        var duracao = TempoMidia.Minimo(midia.Duracao ?? TempoMidia.Segundos(5), disponivel);
        var valores = SolicitarValoresCamada(sessao.Atual.Cursor, TempoMidia.Zero, duracao);
        if (valores is not null && await ExecutarAsync(new AdicionarCamada(midia.Id, Tempo(valores[0]), Tempo(valores[1]), Tempo(valores[2]))))
            Anunciar("Camada adicionada. A duração da sequência principal foi preservada.");
        BotaoAdicionarCamada.Focus();
    }
    private decimal[]? SolicitarValoresCamada(TempoMidia inicio, TempoMidia origem, TempoMidia duracao) => SolicitarNumeros("Intervalo da camada",
        [("Início na composição, em segundos", Segundos(inicio), 0m, 864000m, false),
         ("Início no arquivo original, em segundos", Segundos(origem), 0m, 864000m, false),
         ("Duração, em segundos", Segundos(duracao), 0.000001m, 864000m, false)]);
    private async void EditarCamada(object sender, RoutedEventArgs e)
    {
        if (sessao is null) return;
        var camada = sessao.Atual.Camadas.SingleOrDefault(l => l.Clipe.Id == sessao.Atual.ItemSelecionadoId);
        if (camada is null) { Anunciar("Selecione uma camada da composição."); return; }
        var valores = SolicitarValoresCamada(camada.Inicio, camada.Clipe.InicioNaOrigem, camada.Clipe.Duracao);
        if (valores is not null && await ExecutarAsync(new AtualizarCamada(camada.Clipe.Id, Tempo(valores[0]), Tempo(valores[1]), Tempo(valores[2])))) Anunciar("Intervalo da camada atualizado.");
        BotaoEditarCamada.Focus();
    }
    private async void ExcluirCamada(object sender, RoutedEventArgs e)
    {
        if (sessao?.Atual.ItemSelecionadoId is not { } id) return;
        if (await ExecutarAsync(new RemoverCamada(id))) Anunciar("Camada removida. A sequência principal foi preservada.");
        LinhaDoTempo.Focus();
    }
    private async void LocalizarMidia(object sender, RoutedEventArgs e)
    {
        if (Ocupado || sessao is null || Biblioteca.SelectedItems.Count != 1) { Anunciar("Selecione uma mídia da biblioteca para localizar seu arquivo."); return; }
        var id = ((Linha)Biblioteca.SelectedItem).Id;
        var dialogo = new OpenFileDialog { Title = "Localizar arquivo original", Filter = "Mídias|*.mp4;*.mov;*.png;*.jpg;*.jpeg;*.mp3;*.wav;*.m4a;*.ogg" };
        if (dialogo.ShowDialog(this) != true) return;
        emTransicao = true; Estrutura.IsEnabled = false;
        try
        {
            await LimparPreviaAsync();
            await new AcoesEdicao(sessao).ReligarAsync(id, dialogo.FileName, leitorMetadados);
            exibirPropriedadesBiblioteca = true; Atualizar();
            Anunciar("Localização da mídia atualizada; as edições foram preservadas.");
        }
        catch (Exception ex) { Anunciar("Não foi possível religar a mídia: " + ex.Message); }
        finally { emTransicao = false; Estrutura.IsEnabled = true; Biblioteca.Focus(); }
    }
    private decimal[]? SolicitarNumeros(string titulo, (string Nome, decimal Valor, decimal Minimo, decimal Maximo, bool Inteiro)[] campos)
    {
        decimal[]? resultado = null;
        var painel = new StackPanel(); var entradas = new List<TextBox>();
        foreach (var campo in campos)
        {
            var entrada = new TextBox { Text = campo.Valor.ToString("0.######", CultureInfo.CurrentCulture), Margin = new Thickness(0, 4, 0, 12) };
            painel.Children.Add(new Label { Content = campo.Nome, Target = entrada });
            AutomationProperties.SetName(entrada, campo.Nome); painel.Children.Add(entrada); entradas.Add(entrada);
        }
        var erro = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(erro, AutomationLiveSetting.Assertive); painel.Children.Add(erro);
        var dialogo = CriarDialogo(titulo, painel); var aplicar = new Button { Content = "Aplicar", IsDefault = true };
        aplicar.Click += (_, _) =>
        {
            var valores = new decimal[campos.Length];
            for (var i = 0; i < campos.Length; i++)
            {
                var campo = campos[i];
                if (!decimal.TryParse(entradas[i].Text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.CurrentCulture, out valores[i]) || valores[i] < campo.Minimo || valores[i] > campo.Maximo || (campo.Inteiro && valores[i] != decimal.Truncate(valores[i])))
                {
                    erro.Text = $"{campo.Nome}: informe {(campo.Inteiro ? "um inteiro" : "um número")} entre {campo.Minimo} e {campo.Maximo}.";
                    (UIElementAutomationPeer.FromElement(erro) ?? UIElementAutomationPeer.CreatePeerForElement(erro))?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                    entradas[i].Focus(); entradas[i].SelectAll(); return;
                }
            }
            resultado = valores; dialogo.DialogResult = true;
        };
        painel.Children.Add(aplicar); AdicionarFechar(dialogo, painel, "Cancelar");
        dialogo.Loaded += (_, _) => { entradas[0].Focus(); entradas[0].SelectAll(); };
        dialogo.ShowDialog(); return resultado;
    }
}
