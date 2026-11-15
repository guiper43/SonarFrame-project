using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using SonarFrame.Core;

namespace SonarFrame.Infrastructure;

public sealed partial class PreparadorComposicaoFfmpeg
{
    private sealed record CorteFade(
        TempoMidia FadeInAudio, TempoMidia AtrasoInAudio,
        TempoMidia FadeOutAudio, TempoMidia RestanteOutAudio,
        TempoMidia FadeInVideo, TempoMidia AtrasoInVideo,
        TempoMidia FadeOutVideo, TempoMidia RestanteOutVideo);

    private sealed record BlocoRecortado(Projeto Projeto, IReadOnlyDictionary<Guid, CorteFade> Cortes);

    private static bool PodeUnir(Clipe anterior, Clipe seguinte)
        => anterior.MidiaId == seguinte.MidiaId && anterior.Volume == seguinte.Volume
            && anterior.InicioNaOrigem + anterior.Duracao == seguinte.InicioNaOrigem
            && anterior.FadeOutAudio == TempoMidia.Zero && anterior.FadeOutVideo == TempoMidia.Zero
            && seguinte.FadeInAudio == TempoMidia.Zero && seguinte.FadeInVideo == TempoMidia.Zero;

    private static Projeto SemAudioNaSequencia(Projeto projeto)
        => Normalizar(projeto with { LinhaDoTempo = [.. projeto.LinhaDoTempo.Select(c => c with { Volume = 100, FadeInAudio = TempoMidia.Zero, FadeOutAudio = TempoMidia.Zero })] });

    private static Projeto Normalizar(Projeto projeto)
    {
        var clipes = ImmutableArray.CreateBuilder<Clipe>();
        foreach (var clipe in projeto.LinhaDoTempo)
        {
            if (clipes.Count > 0 && clipes[^1] is { } anterior && PodeUnir(anterior, clipe))
                clipes[^1] = anterior with
                {
                    Duracao = anterior.Duracao + clipe.Duracao,
                    FadeOutAudio = clipe.FadeOutAudio,
                    FadeOutVideo = clipe.FadeOutVideo
                };
            else clipes.Add(clipe);
        }
        return projeto with { LinhaDoTempo = clipes.ToImmutable(), ItemSelecionadoId = null };
    }

    private static string ChaveConteudo(Projeto projeto, bool? audio = null, IReadOnlyDictionary<Guid, CorteFade>? cortes = null)
    {
        if (audio == false) projeto = SemAudioNaSequencia(projeto);
        object Conteudo(Clipe clipe)
        {
            var midia = projeto.Biblioteca.Single(m => m.Id == clipe.MidiaId);
            if (audio == false && midia.Tipo == TipoMidia.Audio || audio == true && midia.Tipo == TipoMidia.Imagem)
                return new { clipe.Duracao };
            var arquivo = IdentificarMetadados(midia);
            var corte = cortes is not null && cortes.TryGetValue(clipe.Id, out var c) ? c : null;
            return new
            {
                arquivo,
                Origem = midia.Tipo == TipoMidia.Imagem ? TempoMidia.Zero : clipe.InicioNaOrigem,
                clipe.Duracao,
                Volume = audio == false ? 100 : clipe.Volume,
                FadeInAudio = audio == false ? TempoMidia.Zero : corte?.FadeInAudio ?? clipe.FadeInAudio,
                AtrasoInAudio = audio == false ? TempoMidia.Zero : corte?.AtrasoInAudio ?? TempoMidia.Zero,
                FadeOutAudio = audio == false ? TempoMidia.Zero : corte?.FadeOutAudio ?? clipe.FadeOutAudio,
                RestanteOutAudio = audio == false ? TempoMidia.Zero : corte?.RestanteOutAudio ?? TempoMidia.Zero,
                FadeInVideo = audio == true ? TempoMidia.Zero : corte?.FadeInVideo ?? clipe.FadeInVideo,
                AtrasoInVideo = audio == true ? TempoMidia.Zero : corte?.AtrasoInVideo ?? TempoMidia.Zero,
                FadeOutVideo = audio == true ? TempoMidia.Zero : corte?.FadeOutVideo ?? clipe.FadeOutVideo,
                RestanteOutVideo = audio == true ? TempoMidia.Zero : corte?.RestanteOutVideo ?? TempoMidia.Zero
            };
        }
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Perfil = 6, Audio = audio, Sequencia = Normalizar(projeto).LinhaDoTempo.Select(Conteudo),
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
                && seguinte.MidiaId == clipe.MidiaId && clipe.InicioNaOrigem + clipe.Duracao == seguinte.InicioNaOrigem
                && clipe.FadeOutAudio == TempoMidia.Zero && clipe.FadeOutVideo == TempoMidia.Zero
                && seguinte.FadeInAudio == TempoMidia.Zero && seguinte.FadeInVideo == TempoMidia.Zero;
            if (!continuaOrigem && fimClipe > inicio && fimClipe < fim && NaGrade(fimClipe)) fim = fimClipe;
            yield return (inicio, fim);
            inicio = fim;
        }
    }
}
