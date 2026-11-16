using System.Collections.Immutable;
using SonarFrame.Core;

namespace SonarFrame.Application;

public sealed record ItemComposicao(
    Guid Id,
    string Nome,
    Guid MidiaId,
    TempoMidia Inicio,
    TempoMidia Duracao,
    int Volume,
    bool EhCamada,
    LayoutVisual? Layout = null)
{
    public LayoutVisual LayoutEfetivo => Layout ?? (EhCamada ? LayoutVisual.PadraoCamada : LayoutVisual.PadraoSequencia);
}

public sealed class AcoesEdicao(SessaoEdicao sessao)
{
    public IReadOnlyList<ItemComposicao> Conteudos()
    {
        var projeto = sessao.Atual;
        var posicao = TempoMidia.Zero;
        var resultado = new List<ItemComposicao>();
        foreach (var clipe in projeto.LinhaDoTempo)
        {
            resultado.Add(new(clipe.Id, clipe.Nome, clipe.MidiaId, posicao, clipe.Duracao, clipe.Volume, false, clipe.Layout));
            posicao += clipe.Duracao;
        }
        resultado.AddRange(projeto.Camadas.Select(l => new ItemComposicao(l.Clipe.Id, l.Clipe.Nome, l.Clipe.MidiaId, l.Inicio, l.Clipe.Duracao, l.Clipe.Volume, true, l.Clipe.Layout)));
        return resultado.OrderBy(c => c.Inicio).ThenBy(c => c.EhCamada).ToArray();
    }
    public ImmutableArray<string> ConflitosVisuais(TempoMidia inicio, TempoMidia duracao, LayoutVisual layout, Guid? ignorarClipeId = null)
    {
        var projeto = sessao.Atual;
        var fim = inicio + duracao;
        var nomes = ImmutableArray.CreateBuilder<string>();
        foreach (var camada in projeto.Camadas)
        {
            if (camada.Clipe.Id == ignorarClipeId) continue;
            var midia = projeto.Biblioteca.Single(a => a.Id == camada.Clipe.MidiaId);
            if (midia.Tipo == TipoMidia.Audio) continue;
            var inicioCamada = camada.Inicio;
            var fimCamada = camada.Inicio + camada.Clipe.Duracao;
            if (inicio >= fimCamada || inicioCamada >= fim) continue;
            var outroLayout = camada.Clipe.ObterLayout(ehCamada: true);
            var mesmaRegiao = layout.Modo == ModoExibicaoVisual.Substituir
                || outroLayout.Modo == ModoExibicaoVisual.Substituir
                || layout.Posicao == outroLayout.Posicao;
            if (mesmaRegiao && !nomes.Contains(camada.Clipe.Nome))
                nomes.Add(camada.Clipe.Nome);
        }
        return nomes.ToImmutable();
    }
    public void DividirNoCursor()
    {
        var cursor = sessao.Atual.Cursor;
        var item = Conteudos().FirstOrDefault(c => !c.EhCamada && cursor > c.Inicio && cursor < c.Inicio + c.Duracao)
            ?? throw new InvalidOperationException("Posicione o cursor dentro de um clipe da sequência para dividi-lo.");
        sessao.Executar(new DividirClipe(item.Id, cursor - item.Inicio));
    }
    public void ExcluirSelecao()
    {
        var selecao = sessao.Atual.Selecao ?? throw new InvalidOperationException("Selecione um intervalo antes de excluir.");
        sessao.Executar(new ExcluirIntervalo(selecao.Inicio, selecao.Fim));
    }
    public void DefinirVolumeSelecionado(int volume)
    {
        var item = Selecionado();
        if (sessao.Atual.Biblioteca.Single(a => a.Id == item.MidiaId).Tipo == TipoMidia.Imagem)
            throw new InvalidOperationException("Imagens não possuem volume.");
        sessao.Executar(new DefinirVolume(item.Id, volume));
    }
    public ItemComposicao Selecionado() => Conteudos().SingleOrDefault(c => c.Id == sessao.Atual.ItemSelecionadoId)
        ?? throw new InvalidOperationException("Selecione um conteúdo da composição.");

    public async Task ReligarAsync(Guid midiaId, string novoCaminho, ILeitorMetadadosMidia leitorMetadados, CancellationToken token = default)
    {
        var original = sessao.Atual.Biblioteca.Single(a => a.Id == midiaId);
        var substituto = await leitorMetadados.LerAsync(novoCaminho, token);
        if (substituto.Tipo != original.Tipo || substituto.Duracao != original.Duracao || substituto.TaxaQuadros != original.TaxaQuadros)
            throw new InvalidOperationException("A mídia escolhida tem tipo, duração ou taxa de quadros diferentes da original. Escolha o arquivo original na nova localização.");
        sessao.Executar(new ReligarMidia(midiaId, substituto.Caminho));
    }
}
