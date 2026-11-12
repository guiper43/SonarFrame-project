using SonarFrame.Core;

namespace SonarFrame.Application;

public sealed class SessaoEdicao
{
    private readonly object bloqueio = new();
    private readonly Stack<Projeto> desfazer = new();
    private readonly Stack<Projeto> refazer = new();
    private Projeto atual;
    private Projeto? salvo;
    private readonly Queue<NotificacaoEditor> mensagens = new();
    public SessaoEdicao(Projeto projeto, bool estaSalvo = false) { atual = projeto.Validar(); salvo = estaSalvo ? projeto : null; }
    public Projeto Atual { get { lock (bloqueio) return atual; } }
    public bool TemAlteracoes { get { lock (bloqueio) return atual != salvo; } }
    public bool PodeDesfazer { get { lock (bloqueio) return desfazer.Count > 0; } }
    public bool PodeRefazer { get { lock (bloqueio) return refazer.Count > 0; } }
    public IReadOnlyList<NotificacaoEditor> Mensagens { get { lock (bloqueio) return mensagens.ToArray(); } }
    public event Action<NotificacaoEditor>? Alterado;
    public void Executar(IComandoEdicao comando)
    {
        ArgumentNullException.ThrowIfNull(comando);
        try
        {
            lock (bloqueio)
            {
                var proximo = comando.Aplicar(atual).Validar();
                if (proximo == atual) return;
                desfazer.Push(atual); refazer.Clear(); atual = proximo;
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException)
        { Notificar(new("edicao.falhou", e.Message, true)); throw; }
        Notificar(new("edicao.concluida", "Edição aplicada."));
    }
    public void Desfazer() => Restaurar(desfazer, refazer, "edicao.desfeita", "Edição desfeita.");
    public void Refazer() => Restaurar(refazer, desfazer, "edicao.refeita", "Edição refeita.");
    private void Restaurar(Stack<Projeto> de, Stack<Projeto> ate, string codigo, string mensagem)
    {
        lock (bloqueio)
        {
            if (de.Count == 0) return;
            ate.Push(atual); atual = de.Pop();
        }
        Notificar(new(codigo, mensagem));
    }
    public void Posicionar(TempoMidia destino, bool estenderSelecao = false, bool reproducao = false)
    {
        lock (bloqueio)
        {
            destino = TempoMidia.Maximo(TempoMidia.Zero, TempoMidia.Minimo(destino, atual.Duracao));
            var selecao = reproducao ? atual.Selecao : estenderSelecao ? new SelecaoTemporal(atual.Selecao?.Ancora ?? atual.Cursor, destino) : null;
            atual = atual with { Cursor = destino, Selecao = selecao?.Inicio == selecao?.Fim ? null : selecao };
        }

        if (!reproducao) Notificar(new("cursor.deslocado", Atual.Cursor.ToString()));
    }
    public void Deslocar(TempoMidia delta, bool estenderSelecao = false) => Posicionar(Atual.Cursor + delta, estenderSelecao);
    public void SelecionarItem(Guid? id)
    {
        lock (bloqueio) atual = (atual with { ItemSelecionadoId = id }).Validar();
        Notificar(new("selecao.alterada", "Seleção alterada."));
    }
    public void AvancarQuadro(int direcao, bool estenderSelecao = false, INavegadorQuadros? navegador = null)
    {
        if (direcao is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direcao));
        var p = Atual;
        if (p.LinhaDoTempo.IsEmpty || (direcao < 0 && p.Cursor <= TempoMidia.Zero) || (direcao > 0 && p.Cursor >= p.Duracao)) return;
        var inicio = TempoMidia.Zero;
        foreach (var clipe in p.LinhaDoTempo)
        {
            var fim = inicio + clipe.Duracao;
            if (p.Cursor < fim || (direcao < 0 && p.Cursor == fim))
            {
                var midia = p.Biblioteca.Single(a => a.Id == clipe.MidiaId);
                var origem = clipe.InicioNaOrigem + p.Cursor - inicio;
                TempoMidia proximo;
                if (navegador is not null) proximo = navegador.ProximoQuadro(midia, origem, direcao);
                else
                {
                    if (midia.TaxaQuadrosVariavel || midia.TaxaQuadros is null) throw new InvalidOperationException("Esta mídia requer um navegador de quadros.");
                    proximo = origem.QuadroAdjacente(midia.TaxaQuadros, direcao);
                }
                if (direcao > 0 ? proximo <= origem : proximo >= origem) throw new InvalidOperationException("Timestamp de quadro inválido.");
                Posicionar(TempoMidia.Maximo(inicio, TempoMidia.Minimo(fim, inicio + proximo - clipe.InicioNaOrigem)), estenderSelecao);
                return;
            }
            inicio = fim;
        }
    }
    public void MarcarComoSalvo(Projeto snapshot) { lock (bloqueio) salvo = snapshot; }
    public void Notificar(NotificacaoEditor notificacao)
    {
        lock (bloqueio)
        {
            mensagens.Enqueue(notificacao);
            while (mensagens.Count > 100) mensagens.Dequeue();
        }
        if (Alterado is { } handlers)
            foreach (Action<NotificacaoEditor> handler in handlers.GetInvocationList())
                try { handler(notificacao); } catch {  }
    }
}
