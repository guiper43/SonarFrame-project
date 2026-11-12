using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using SonarFrame.Core;

namespace SonarFrame.Infrastructure;

public sealed partial class PreparadorComposicaoFfmpeg
{
    private static Projeto Normalizar(Projeto projeto)
    {
        var clipes = ImmutableArray.CreateBuilder<Clipe>();
        foreach (var clipe in projeto.LinhaDoTempo)
        {
            if (clipes.Count > 0 && clipes[^1] is { } anterior && anterior.MidiaId == clipe.MidiaId && anterior.Volume == clipe.Volume
                && anterior.InicioNaOrigem + anterior.Duracao == clipe.InicioNaOrigem)
                clipes[^1] = anterior with { Duracao = anterior.Duracao + clipe.Duracao };
            else clipes.Add(clipe);
        }
        return projeto with { LinhaDoTempo = clipes.ToImmutable(), ItemSelecionadoId = null };
    }

    private static string ChaveConteudo(Projeto projeto, bool? audio = null)
    {
        if (audio == false) projeto = Normalizar(projeto with { LinhaDoTempo = [.. projeto.LinhaDoTempo.Select(c => c with { Volume = 100 })] });
        object Conteudo(Clipe clipe)
        {
            var midia = projeto.Biblioteca.Single(m => m.Id == clipe.MidiaId);
            if (audio == false && midia.Tipo == TipoMidia.Audio || audio == true && midia.Tipo == TipoMidia.Imagem)
                return new { clipe.Duracao };
            var arquivo = IdentificarMetadados(midia);
            return new { arquivo, Origem = midia.Tipo == TipoMidia.Imagem ? TempoMidia.Zero : clipe.InicioNaOrigem,
                clipe.Duracao, Volume = audio == false ? 100 : clipe.Volume };
        }
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Perfil = 4, Audio = audio, Sequencia = Normalizar(projeto).LinhaDoTempo.Select(Conteudo),
            Camadas = projeto.Camadas.Where(c => audio is null || projeto.Biblioteca.Single(m => m.Id == c.Clipe.MidiaId).Tipo != (audio.Value ? TipoMidia.Imagem : TipoMidia.Audio))
                .Select(c => new { c.Inicio, Clipe = Conteudo(c.Clipe) })
        })));
    }

    private static IEnumerable<(TempoMidia Inicio, TempoMidia Fim)> Segmentos(Projeto projeto)
    {
        var inicioClipe = TempoMidia.Zero;
        var indice = 0;
        for (var inicio = TempoMidia.Zero; inicio < projeto.Duracao;)
        {
            while (indice < projeto.LinhaDoTempo.Length - 1 && inicioClipe + projeto.LinhaDoTempo[indice].Duracao <= inicio)
                inicioClipe += projeto.LinhaDoTempo[indice++].Duracao;
            var clipe = projeto.LinhaDoTempo[indice];
            var origem = clipe.InicioNaOrigem + inicio - inicioClipe;
            var proximoNaOrigem = TempoMidia.Segundos(checked((origem.Numerador / origem.Denominador / 5 + 1) * 5));
            var proximo = inicio + proximoNaOrigem - origem;
            var fim = TempoMidia.Minimo(inicio + TempoMidia.Segundos(5), projeto.Duracao);
            bool NaGrade(TempoMidia tempo) => (BigInteger)tempo.Numerador * 30 % tempo.Denominador == 0;
            if (proximo > inicio && proximo < fim && NaGrade(proximo)) fim = proximo;
            var fimClipe = inicioClipe + clipe.Duracao;
            var continuaOrigem = indice + 1 < projeto.LinhaDoTempo.Length && projeto.LinhaDoTempo[indice + 1] is { } seguinte
                && seguinte.MidiaId == clipe.MidiaId && clipe.InicioNaOrigem + clipe.Duracao == seguinte.InicioNaOrigem;
            if (!continuaOrigem && fimClipe > inicio && fimClipe < fim && NaGrade(fimClipe)) fim = fimClipe;
            yield return (inicio, fim);
            inicio = fim;
        }
    }
}
