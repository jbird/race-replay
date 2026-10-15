using System.Globalization;

namespace RaceReplay.Domain;

/// <summary>
/// Identifies a session. OpenF1 keys are small positive integers. Synthetic races use a reserved range starting at
/// <see cref="SyntheticBase"/>, so one key type serves both sources without type checks (docs/02 §7).
/// </summary>
public readonly record struct SessionKey(int Value)
{
    /// <summary>First key of the reserved synthetic range, far above OpenF1's keys.</summary>
    public const int SyntheticBase = 1_000_000_000;

    /// <summary>Largest seed that still maps into the synthetic range.</summary>
    public const int MaxSyntheticSeed = int.MaxValue - SyntheticBase;

    /// <summary>The key of the synthetic race generated from <paramref name="seed"/>.</summary>
    public static SessionKey Synthetic(int seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(seed, MaxSyntheticSeed);
        return new SessionKey(SyntheticBase + seed);
    }

    /// <summary>Whether the key lies in the reserved synthetic range.</summary>
    public bool IsSynthetic => Value >= SyntheticBase;

    /// <summary>The seed of a synthetic key.</summary>
    /// <exception cref="InvalidOperationException">The key is not synthetic.</exception>
    public int SyntheticSeed => IsSynthetic
        ? Value - SyntheticBase
        : throw new InvalidOperationException($"Session key {Value} is not a synthetic key.");

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}