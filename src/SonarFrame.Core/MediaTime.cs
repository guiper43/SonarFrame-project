using System.Numerics;
using System.Text.Json.Serialization;

namespace SonarFrame.Core;

/// <summary>Exact rational seconds. Intervals use [start, end) semantics.</summary>
public readonly record struct MediaTime : IComparable<MediaTime>
{
    public long Numerator { get; }
    private readonly long denominator;
    public long Denominator => denominator == 0 ? 1 : denominator;
    [JsonConstructor]
    public MediaTime(long numerator, long denominator)
    {
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = (long)(numerator / gcd);
        this.denominator = (long)(denominator / gcd);
    }
    private long Scale => Denominator == 0 ? 1 : Denominator;
    public static MediaTime Zero => new(0, 1);
    public static MediaTime Seconds(long seconds) => new(seconds, 1);
    private static MediaTime From(BigInteger n, BigInteger d)
    {
        var gcd = BigInteger.GreatestCommonDivisor(n, d);
        return new(checked((long)(n / gcd)), checked((long)(d / gcd)));
    }
    public static MediaTime operator +(MediaTime a, MediaTime b) => From((BigInteger)a.Numerator * b.Scale + (BigInteger)b.Numerator * a.Scale, (BigInteger)a.Scale * b.Scale);
    public static MediaTime operator -(MediaTime a, MediaTime b) => From((BigInteger)a.Numerator * b.Scale - (BigInteger)b.Numerator * a.Scale, (BigInteger)a.Scale * b.Scale);
    public int CompareTo(MediaTime other) => ((BigInteger)Numerator * other.Scale).CompareTo((BigInteger)other.Numerator * Scale);
    public static bool operator <(MediaTime a, MediaTime b) => a.CompareTo(b) < 0;
    public static bool operator >(MediaTime a, MediaTime b) => a.CompareTo(b) > 0;
    public static bool operator <=(MediaTime a, MediaTime b) => a.CompareTo(b) <= 0;
    public static bool operator >=(MediaTime a, MediaTime b) => a.CompareTo(b) >= 0;
    public static MediaTime Min(MediaTime a, MediaTime b) => a < b ? a : b;
    public static MediaTime Max(MediaTime a, MediaTime b) => a > b ? a : b;
    public MediaTime AdjacentFrame(FrameRate rate, int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var step = rate.FrameDuration;
        var numerator = (BigInteger)Numerator * step.Denominator;
        var denominator = (BigInteger)Scale * step.Numerator;
        var index = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (remainder < 0) { index--; remainder += denominator; }
        index += direction > 0 ? 1 : remainder == 0 ? -1 : 0;
        return From(index * step.Numerator, step.Denominator);
    }
    public bool Equals(MediaTime other) => CompareTo(other) == 0;
    public override int GetHashCode() => HashCode.Combine(Numerator, Scale);
    public override string ToString()
    {
        var ms = checked((long)((BigInteger)Numerator * 1000 / Scale));
        return $"{ms / 3600000:00}:{ms / 60000 % 60:00}:{ms / 1000 % 60:00}.{ms % 1000:000}";
    }
}

public sealed record FrameRate(int Numerator, int Denominator)
{
    public MediaTime FrameDuration => Numerator > 0 && Denominator > 0
        ? new(Denominator, Numerator) : throw new ArgumentException("Taxa de quadros inválida.");
}
