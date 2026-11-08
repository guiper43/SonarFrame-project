using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using SonarFrame.Application;
using SonarFrame.Core;
using SonarFrame.Infrastructure;

namespace SonarFrame.Desktop;

public partial class JanelaPrincipal
{
    private PersistenciaProjeto? persistencia;
    private ConfiguracoesDesktop configuracoes = ConfiguracoesDesktop.Padrao;
    private readonly RepositorioConfiguracoes repositorioConfiguracoes;
    private readonly CatalogoProjetos catalogoProjetos;
    private readonly DispatcherTimer salvamentoAutomatico = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool emTransicao;
    private bool fechamentoAprovado;
    internal Func<Task>? EncerrarServicosAsync { get; set; }
    private bool PersistenciaOcupada => salvando || emTransicao;
    private bool Ocupado => PersistenciaOcupada || exportacaoEmAndamento;
    internal SessaoEdicao? SessaoAtual => sessao;
    internal ConfiguracoesDesktop ConfiguracoesAtuais => configuracoes;
    internal RepositorioConfiguracoes RepositorioConfiguracoes => repositorioConfiguracoes;
    internal void PararTarefasEmSegundoPlano() => salvamentoAutomatico.Stop();

    private void ConfigurarCicloDeVida()
    {
        try { configuracoes = repositorioConfiguracoes.Carregar(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { Loaded += (_, _) => Anunciar("Não foi possível ler as configurações. A pasta padrão será usada: " + e.Message); }
        salvamentoAutomatico.Tick += async (_, _) => await SalvarAutomaticamenteAsync();
        salvamentoAutomatico.Start();
    }
    internal async Task SalvarAutomaticamenteAsync()
    {
        if (PersistenciaOcupada || persistencia is null) return;
        salvando = true;
        try
        {
            if (await persistencia.SalvarAutomaticamenteSeNecessarioAsync()) { Atualizar(); Anunciar("Projeto salvo automaticamente."); }
        }
        catch (Exception e) { Anunciar("Falha no salvamento automático: " + e.Message); }
        finally { salvando = false; }
    }
    internal async Task<bool> SalvarAtualAsync()
    {
        if (persistencia is null) { Anunciar("Crie ou abra um projeto primeiro."); return false; }
        if (salvando) return false;
        salvando = true;
        try { await persistencia.SalvarAsync(); Atualizar(); Anunciar("Projeto salvo."); return true; }
        catch (Exception e) { Anunciar("Falha ao salvar: " + e.Message); return false; }
        finally { salvando = false; }
    }
    private async Task<bool> PodeSairAsync()
    {
        if (Ocupado) { Anunciar(exportacaoEmAndamento ? "Exportação em andamento. Aguarde a conclusão ou pressione Escape para cancelar." : "Aguarde a operação em andamento."); return false; }
        emTransicao = true; Estrutura.IsEnabled = false;
        try
        {
            if (sessao?.TemAlteracoes == true)
            {
                var escolha = MessageBox.Show(this, "Salvar as alterações antes de sair deste projeto? Sim salva, Não descarta as alterações não salvas e Cancelar mantém o projeto aberto.", "Salvar alterações", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
                if (escolha == MessageBoxResult.Cancel) return false;
                if (escolha == MessageBoxResult.Yes && !await SalvarAtualAsync()) return false;
            }
            if (persistencia is not null) await persistencia.FecharAsync(descartarAlteracoesNaoSalvas: true);
            await LimparPreviaAsync();
            return true;
        }
        catch (Exception e) { Anunciar("Não foi possível fechar o projeto: " + e.Message); return false; }
        finally { emTransicao = false; Estrutura.IsEnabled = true; }
    }
    private async void FechandoJanela(object? sender, CancelEventArgs e)
    {
        if (fechamentoAprovado) return;
        e.Cancel = true;
        if (!await PodeSairAsync()) return;
        salvamentoAutomatico.Stop();
        emTransicao = true; Estrutura.IsEnabled = false;
        Anunciar("Encerrando a prévia e liberando os recursos.");
        try
        {
            try { await EncerrarPreviaAsync(); }
            finally
            {
                if (EncerrarServicosAsync is { } encerrar) await encerrar();
            }
        }
        catch (Exception erro)
        {
            MessageBox.Show(this, "Não foi possível liberar todos os recursos ao encerrar: " + erro.Message,
                "Encerrar sonarFrame", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        fechamentoAprovado = true;

        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
    private async Task AbrirCaminhoAsync(string caminho)
    {
        if (Ocupado) return;
        if (sessao is not null && string.Equals(caminhoProjeto, Path.GetFullPath(caminho), StringComparison.OrdinalIgnoreCase))
        { Anunciar("Este projeto já está aberto."); return; }
        Projeto? projeto = null; var recuperado = false; var principalInvalido = false;
        emTransicao = true; Estrutura.IsEnabled = false;
        try
        {
            Projeto? principal = null;
            try { if (File.Exists(caminho)) principal = await repositorio.CarregarAsync(caminho); }
            catch (Exception e) when ((e is IOException or InvalidDataException) && File.Exists(caminho + ".recovery")) { principalInvalido = true; }
            projeto = principal;
            if (File.Exists(caminho + ".recovery"))
            {
                var candidato = await repositorio.CarregarAsync(caminho + ".recovery");
                if (principal is not null && candidato.Id != principal.Id) throw new InvalidDataException("A recuperação pertence a outro projeto.");
                var escolha = MessageBox.Show(this, "Há uma sessão que não foi encerrada normalmente. Recuperar o último estado disponível? Não abre a versão principal; Cancelar não abre o projeto.", "Recuperar projeto", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
                if (escolha == MessageBoxResult.Cancel) return;
                if (escolha == MessageBoxResult.Yes) { projeto = candidato; recuperado = true; }
            }
            if (projeto is null) throw new InvalidDataException("Não há uma versão principal válida. Abra novamente e aceite a recuperação disponível.");
        }
        catch (Exception e) { Anunciar("Não foi possível abrir: " + e.Message); return; }
        finally { emTransicao = false; Estrutura.IsEnabled = true; }
        if (!await PodeSairAsync()) return;
        if (principalInvalido) caminho = CatalogoProjetos.GerarCaminho(Path.GetDirectoryName(Path.GetFullPath(caminho))!, projeto.Nome + " recuperado");
        ExibirProjeto(projeto, !recuperado, caminho);
        if (principalInvalido) Anunciar("Recuperação aberta em um novo projeto. O arquivo principal inválido foi preservado.");
        if (!recuperado && File.Exists(caminho + ".recovery"))
        {
            try { await persistencia!.FecharAsync(); }
            catch (IOException e) { Anunciar("A versão principal foi aberta; a recuperação não pôde ser descartada: " + e.Message); }
        }
        var ausentes = ImportacaoMidias.EncontrarAusentes(projeto);
        if (ausentes.Count > 0) Anunciar($"{ausentes.Count} mídia(s) ausente(s). Selecione o item na biblioteca e use Localizar arquivo.");
    }
    private async void SalvarComo(object sender, RoutedEventArgs e)
    {
        if (Ocupado || sessao is null) return;
        var dialogo = new SaveFileDialog { Filter = "Projeto sonarFrame|*.sonarframe", DefaultExt = ".sonarframe", FileName = sessao.Atual.Nome };
        if (dialogo.ShowDialog(this) != true) return;
        if (Path.GetDirectoryName(dialogo.FileName) is { } pastaSalvar && ConfiguracoesDesktop.MesmaPasta(pastaSalvar, configuracoes.DiretorioExportacoes))
        { Anunciar("Escolha uma pasta diferente da pasta de vídeos exportados para não misturar arquivos do projeto com exportações."); return; }
        if (string.Equals(caminhoProjeto, dialogo.FileName, StringComparison.OrdinalIgnoreCase)) { await SalvarAtualAsync(); return; }
        salvando = true;
        try
        {
            var substituto = new PersistenciaProjeto(sessao, repositorio, dialogo.FileName, relogio);
            await substituto.SalvarAsync();
            var anterior = persistencia;
            persistencia = substituto; caminhoProjeto = dialogo.FileName;
            if (anterior is not null) await anterior.FecharAsync(descartarAlteracoesNaoSalvas: true);
            Atualizar(); Anunciar("Projeto salvo no novo local.");
        }
        catch (Exception ex) { Anunciar("Falha ao salvar como: " + ex.Message); }
        finally { salvando = false; }
    }
    internal sealed record LinhaProjeto(string Caminho, string Rotulo)
    {
        public override string ToString() => Rotulo;
    }

    private async void MeusProjetos(object sender, RoutedEventArgs e)
    {
        if (Ocupado) return;
        ResultadoCatalogo catalogo;
        emTransicao = true; Estrutura.IsEnabled = false;
        try { catalogo = await catalogoProjetos.LerAsync(configuracoes.DiretorioProjetos); }
        catch (Exception ex) { Anunciar("Não foi possível listar projetos: " + ex.Message); return; }
        finally { emTransicao = false; Estrutura.IsEnabled = true; }
        var painel = new StackPanel();
        painel.Children.Add(new TextBlock { Text = catalogo.Projetos.Count == 0 ? "Nenhum projeto nesta pasta. Crie um projeto ou altere a pasta nas Configurações." : "Selecione um projeto para abrir.", TextWrapping = TextWrapping.Wrap });
        var lista = new ListBox { Height = 240, DisplayMemberPath = "Rotulo", Margin = new Thickness(0, 12, 0, 12) };
        var linhas = catalogo.Projetos.Select(p => new LinhaProjeto(p.Caminho, $"Projeto: {p.Nome}. Caminho: {p.Caminho}. Modificado: {p.UltimaModificacaoUtc.ToLocalTime():g}{(p.TemRecuperacao ? ". Recuperação disponível" : "")}")).ToArray();
        lista.ItemsSource = linhas; AutomationProperties.SetName(lista, "Projetos salvos"); painel.Children.Add(lista);
        var dialogo = CriarDialogo("Meus projetos", painel); string? escolhido = null;
        var abrir = new Button { Content = "Abrir projeto", IsDefault = true, IsEnabled = false };
        lista.SelectionChanged += (_, _) => abrir.IsEnabled = lista.SelectedIndex >= 0;
        abrir.Click += (_, _) => { if (lista.SelectedIndex >= 0) { escolhido = linhas[lista.SelectedIndex].Caminho; dialogo.DialogResult = true; } };
        painel.Children.Add(abrir); AdicionarFechar(dialogo, painel);
        dialogo.Loaded += (_, _) => lista.Focus(); dialogo.ShowDialog();
        foreach (var erro in catalogo.Erros) Anunciar(erro);
        if (escolhido is not null) await AbrirCaminhoAsync(escolhido);
    }
    private void Configuracoes(object sender, RoutedEventArgs e)
    {
        var painel = new StackPanel();
        var pasta = new TextBox { Text = configuracoes.DiretorioProjetos, IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
        painel.Children.Add(new Label { Content = "Pasta de projetos", Target = pasta }); painel.Children.Add(pasta);
        var procurar = new Button { Content = "Escolher pasta…" };
        procurar.Click += (_, _) => { var seletor = new OpenFolderDialog { Title = "Pasta de projetos" }; if (seletor.ShowDialog(this) == true) pasta.Text = seletor.FolderName; };
        painel.Children.Add(procurar);
        painel.Children.Add(new TextBlock { Text = "A alteração vale para novos projetos. Projetos existentes continuam na pasta atual e podem ser abertos por Abrir projeto existente.", TextWrapping = TextWrapping.Wrap });
        var dialogo = CriarDialogo("Configurações", painel);
        var salvar = new Button { Content = "Salvar configurações", IsDefault = true };
        salvar.Click += (_, _) =>
        {
            try
            {
                var novaPastaProjetos = pasta.Text;
                var novaPastaExportacoes = ConfiguracoesDesktop.MesmaPasta(novaPastaProjetos, configuracoes.DiretorioExportacoes)
                    ? ConfiguracoesDesktop.PastaExportacoesPadrao(novaPastaProjetos, PastaDownloadsExportacao)
                    : configuracoes.DiretorioExportacoes;
                var proximo = new ConfiguracoesDesktop(novaPastaProjetos, novaPastaExportacoes);
                proximo.Validar();
                Directory.CreateDirectory(proximo.DiretorioProjetos);
                repositorioConfiguracoes.Salvar(proximo);
                configuracoes = proximo;
                dialogo.DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(dialogo, ex.Message, "Não foi possível salvar"); }
        };
        painel.Children.Add(salvar); AdicionarFechar(dialogo, painel, "Cancelar");
        dialogo.Loaded += (_, _) => pasta.Focus(); dialogo.ShowDialog();
    }
}
