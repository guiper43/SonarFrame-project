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
        Propriedades.Text = $"{item.Nome}\n{(item.EhCamada ? "Camada independente" : "Sequência principal")}\nInício na composição: {item.Inicio}\nDuração: {item.Duracao}\nVolume: {item.Volume}%";
    }
    private async Task AplicarAcaoAsync(Action<AcoesEdicao> acao, string mensagem)
    {
        if (sessao is null || !await PrepararEdicaoAsync()) return;
        try { acao(new AcoesEdicao(sessao)); exibirPropriedadesBiblioteca = false; Atualizar(); Anunciar(mensagem); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { Anunciar(e.Message); }
    }
    private async void DividirNoCursor(object sender, RoutedEventArgs e)
    {
        if (sessao is null || !await PrepararEdicaoAsync()) return;
        try
        {
            var posicao = sessao.Atual.Cursor;
            new AcoesEdicao(sessao).DividirNoCursor();
            exibirPropriedadesBiblioteca = false; Atualizar();
            Anunciar($"Corte realizado em {posicao}.");
        }
        catch (Exception erro) when (erro is ArgumentException or InvalidOperationException) { Anunciar(erro.Message); }
    }
    private async void ExcluirIntervaloSelecionado(object sender, RoutedEventArgs e)
    {
        await AplicarAcaoAsync(a => a.ExcluirSelecao(), "Intervalo excluído e lacuna fechada.");
        NavegadorTemporal.Focus();
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
