using SonarFrame.Core;

namespace SonarFrame.Desktop;

public partial class JanelaPrincipal
{
    internal static string TempoFalado(TempoMidia tempo)
    {
        var segundos = tempo.Numerador / tempo.Denominador;
        var minutos = segundos / 60;
        segundos %= 60;
        return $"{minutos} {(minutos == 1 ? "minuto" : "minutos")} e {segundos} {(segundos == 1 ? "segundo" : "segundos")}";
    }

    internal static string AnuncioDeslocamento(TempoMidia anterior, TempoMidia destino, bool selecao)
    {
        if (selecao) return $"Seleção até {destino}.";
        var diferenca = destino - anterior;
        var delta = diferenca < TempoMidia.Zero ? anterior - destino : diferenca;
        var segundos = delta.Numerador / delta.Denominador;
        var quantidade = segundos == 0 ? "menos de 1 segundo" : $"{segundos} {(segundos == 1 ? "segundo" : "segundos")}";
        var movimento = diferenca == TempoMidia.Zero ? "" : $"{(diferenca > TempoMidia.Zero ? "Avançou" : "Retrocedeu")} {quantidade}. ";
        return $"{movimento}Atualmente em {TempoFalado(destino)}.";
    }
}
