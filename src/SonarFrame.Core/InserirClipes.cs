using System.Collections.Immutable;

namespace SonarFrame.Core;

public sealed record InserirClipes(ImmutableArray<Guid> MidiasIds, TempoMidia? DuracaoImagem = null, int? Indice = null) : IComandoEdicao
{
    public Projeto Aplicar(Projeto projeto)
    {
        if (MidiasIds.IsDefaultOrEmpty) throw new ArgumentException("Selecione pelo menos uma mídia.");
        var indice = Indice ?? projeto.LinhaDoTempo.Length;
        foreach (var id in MidiasIds)
            projeto = new InserirClipe(id, indice++, DuracaoImagem).Aplicar(projeto);
        return projeto;
    }
}
