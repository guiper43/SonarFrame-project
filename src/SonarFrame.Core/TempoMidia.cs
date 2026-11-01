using System.Numerics;
using System.Text.Json.Serialization;

namespace SonarFrame.Core;

public readonly record struct TempoMidia : IComparable<TempoMidia>
{
    [JsonPropertyName("Numerator")] public long Numerador { get; }
    private readonly long denominador;
    [JsonPropertyName("Denominator")] public long Denominador => denominador == 0 ? 1 : denominador;
    [JsonConstructor]
    public TempoMidia(long numerador, long denominador)
    {
        if (denominador <= 0) throw new ArgumentOutOfRangeException(nameof(denominador));
        var mdc = BigInteger.GreatestCommonDivisor(numerador, denominador);
        Numerador = (long)(numerador / mdc);
        this.denominador = (long)(denominador / mdc);
    }
    private long Escala => Denominador == 0 ? 1 : Denominador;
    public static TempoMidia Zero => new(0, 1);
    public static TempoMidia Segundos(long segundos) => new(segundos, 1);
    private static TempoMidia De(BigInteger n, BigInteger d)
    {
        var mdc = BigInteger.GreatestCommonDivisor(n, d);
        return new(checked((long)(n / mdc)), checked((long)(d / mdc)));
    }
    public static TempoMidia operator +(TempoMidia a, TempoMidia b) => De((BigInteger)a.Numerador * b.Escala + (BigInteger)b.Numerador * a.Escala, (BigInteger)a.Escala * b.Escala);
    public static TempoMidia operator -(TempoMidia a, TempoMidia b) => De((BigInteger)a.Numerador * b.Escala - (BigInteger)b.Numerador * a.Escala, (BigInteger)a.Escala * b.Escala);
    public int CompareTo(TempoMidia outro) => ((BigInteger)Numerador * outro.Escala).CompareTo((BigInteger)outro.Numerador * Escala);
    public static bool operator <(TempoMidia a, TempoMidia b) => a.CompareTo(b) < 0;
    public static bool operator >(TempoMidia a, TempoMidia b) => a.CompareTo(b) > 0;
    public static bool operator <=(TempoMidia a, TempoMidia b) => a.CompareTo(b) <= 0;
    public static bool operator >=(TempoMidia a, TempoMidia b) => a.CompareTo(b) >= 0;
    public static TempoMidia Minimo(TempoMidia a, TempoMidia b) => a < b ? a : b;
    public static TempoMidia Maximo(TempoMidia a, TempoMidia b) => a > b ? a : b;
    public TempoMidia QuadroAdjacente(TaxaQuadros taxa, int direcao)
    {
        if (direcao is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direcao));
        var passo = taxa.DuracaoQuadro;
        var numerador = (BigInteger)Numerador * passo.Denominador;
        var denominador = (BigInteger)Escala * passo.Numerador;
        var indice = BigInteger.DivRem(numerador, denominador, out var resto);
        if (resto < 0) { indice--; resto += denominador; }
        indice += direcao > 0 ? 1 : resto == 0 ? -1 : 0;
        return De(indice * passo.Numerador, passo.Denominador);
    }
    public bool Equals(TempoMidia outro) => CompareTo(outro) == 0;
    public override int GetHashCode() => HashCode.Combine(Numerador, Escala);
    public override string ToString()
    {
        var ms = checked((long)((BigInteger)Numerador * 1000 / Escala));
        return $"{ms / 3600000:00}:{ms / 60000 % 60:00}:{ms / 1000 % 60:00}.{ms % 1000:000}";
    }
}

public sealed record TaxaQuadros([property: JsonPropertyName("Numerator")] int Numerador, [property: JsonPropertyName("Denominator")] int Denominador)
{
    [JsonPropertyName("FrameDuration")] public TempoMidia DuracaoQuadro => Numerador > 0 && Denominador > 0
        ? new(Denominador, Numerador) : throw new ArgumentException("Taxa de quadros inválida.");
}
