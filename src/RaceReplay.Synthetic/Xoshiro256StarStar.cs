using System.Numerics;

namespace RaceReplay.Synthetic;

/// <summary>
/// The xoshiro256** 1.0 generator (Blackman and Vigna). The generator owns its algorithm rather than using
/// <see cref="Random"/>, whose output .NET doesn't promise to keep stable across versions, so a seed reproduces
/// the same race everywhere (docs/04 §11.3).
/// </summary>
public sealed class Xoshiro256StarStar
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;
    private double? _spareGaussian;

    /// <summary>Starts from an explicit state, which must not be all zero.</summary>
    public Xoshiro256StarStar(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        if ((s0 | s1 | s2 | s3) == 0)
        {
            throw new ArgumentException("The state must not be all zero.");
        }

        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
    }

    /// <summary>Seeds the state from SplitMix64, as the algorithm's authors recommend.</summary>
    public static Xoshiro256StarStar FromSeed(ulong seed)
    {
        ulong state = seed;
        return new Xoshiro256StarStar(
            SplitMix64.Next(ref state), SplitMix64.Next(ref state), SplitMix64.Next(ref state), SplitMix64.Next(ref state));
    }

    public ulong NextUInt64()
    {
        ulong result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Uniform in [0, 1), using the top 53 bits.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform in [<paramref name="min"/>, <paramref name="max"/>).</summary>
    public double NextDouble(double min, double max) => min + ((max - min) * NextDouble());

    /// <summary>Uniform in [0, <paramref name="maxExclusive"/>), without modulo bias (Lemire's method).</summary>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        ulong range = (ulong)maxExclusive;
        ulong high = Math.BigMul(NextUInt64(), range, out ulong low);
        if (low < range)
        {
            ulong threshold = (0UL - range) % range;
            while (low < threshold)
            {
                high = Math.BigMul(NextUInt64(), range, out low);
            }
        }

        return (int)high;
    }

    /// <summary>Uniform in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minInclusive, maxExclusive);
        return minInclusive + NextInt(maxExclusive - minInclusive);
    }

    /// <summary>True with probability <paramref name="probability"/>.</summary>
    public bool NextBool(double probability) => NextDouble() < probability;

    /// <summary>Standard normal (Box–Muller), mean 0 and standard deviation 1.</summary>
    public double NextGaussian()
    {
        if (_spareGaussian is double spare)
        {
            _spareGaussian = null;
            return spare;
        }

        double u1 = 1.0 - NextDouble(); // (0, 1], so the logarithm is finite
        double u2 = NextDouble();
        double radius = Math.Sqrt(-2.0 * Math.Log(u1));
        double angle = 2.0 * Math.PI * u2;
        _spareGaussian = radius * Math.Sin(angle);
        return radius * Math.Cos(angle);
    }

    /// <summary>Fisher–Yates shuffle in place.</summary>
    public void Shuffle<T>(Span<T> items)
    {
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}

/// <summary>SplitMix64 (Steele, Lea and Flood), used only to expand a seed into a xoshiro state.</summary>
internal static class SplitMix64
{
    public static ulong Next(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        return z ^ (z >> 31);
    }
}

/// <summary>Independent random channels per concern, so changing one part of the generator doesn't reshuffle the rest.</summary>
public enum RandomChannel
{
    Track = 1,
    Field = 2,
    Race = 3,
    Observation = 4,
}

public static class SyntheticRandom
{
    /// <summary>The generator for one <paramref name="channel"/> of the race with <paramref name="seed"/>.</summary>
    public static Xoshiro256StarStar For(int seed, RandomChannel channel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seed);
        return Xoshiro256StarStar.FromSeed(((ulong)seed << 8) | (ulong)channel);
    }
}