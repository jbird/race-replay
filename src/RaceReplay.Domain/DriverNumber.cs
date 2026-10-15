using System.Globalization;

namespace RaceReplay.Domain;

/// <summary>A car's race number, which identifies the driver within a session.</summary>
public readonly record struct DriverNumber(int Value)
{
    public const int MinValue = 0;

    /// <summary>Real numbers are 1–99; the bound keeps them well inside the bundle's 16-bit field.</summary>
    public const int MaxValue = 999;

    public int Value { get; } = Value is >= MinValue and <= MaxValue
        ? Value
        : throw new ArgumentOutOfRangeException(nameof(Value), Value, $"Driver numbers run from {MinValue} to {MaxValue}.");

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}